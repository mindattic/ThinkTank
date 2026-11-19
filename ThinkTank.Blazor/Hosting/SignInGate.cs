using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using MindAttic.Authentication;
using MindAttic.Authentication.Web;

namespace ThinkTank.Blazor.Hosting;

/// <summary>What the gate does with one request.</summary>
public enum GateDecision
{
    /// <summary>Let it through.</summary>
    Allow,
    /// <summary>Nobody is signed in: a page goes to /login, anything else gets 401.</summary>
    Challenge,
    /// <summary>Signed in, but not an allowed account: signed out and 403.</summary>
    Forbidden,
    /// <summary>Signed in and allowed, but without a TOTP step while two-factor is required: sent to enrolment.</summary>
    EnrollMfa,
}

/// <summary>
/// ThinkTank's access rule (TT-T46, after Prose.Hub's <c>HostedGate</c>). Nothing that spends an API key is
/// reachable without a signed-in, allowed account, in either hosting mode.
///
/// <para>The rule is one function, <see cref="Decide"/>, enforced twice so neither is a single point of failure:
/// as middleware right after authentication (it sees every request, the Blazor hub <c>/_blazor</c> and its
/// WebSocket upgrade included, before anything is served), and as the authorization <em>fallback policy</em>
/// (every endpoint without a policy of its own, so a route added later is closed without anyone remembering).</para>
///
/// <para>Anonymous access is an allow-list of exact method + path pairs (<see cref="IsAnonymousAllowed"/>): the
/// sign-in pages, the two sign-in posts, <c>/healthz</c>, <c>/healthz/db</c> and the error page.</para>
/// </summary>
public static class SignInGate
{
    public const string LoginRateLimitPolicy = "thinktank-login";
    public const string HealthPath = "/healthz";
    public const string HealthDbPath = "/healthz/db";
    public const string LoginPath = "/login";
    public const string MfaPath = "/mfa";
    public const string MfaSetupPath = "/account/mfa/setup";
    public const string ErrorPath = "/Error";

    // ── The rule ─────────────────────────────────────────────────────────────

    /// <summary>The only requests served without a login. Exact paths; an optional trailing slash is tolerated.</summary>
    public static bool IsAnonymousAllowed(string method, PathString path)
    {
        var p = Normalize(path);
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            return Is(p, HealthPath) || Is(p, HealthDbPath) || Is(p, LoginPath) || Is(p, MfaPath) || Is(p, ErrorPath);
        if (HttpMethods.IsPost(method))
            return Is(p, "/_ma-auth/login") || Is(p, "/_ma-auth/mfa-challenge");
        return false;
    }

    /// <summary>Pages a signed-in user may reach before the TOTP step: the account pages and the auth posts.</summary>
    public static bool IsAuthSurface(PathString path) =>
        path.StartsWithSegments("/account") || path.StartsWithSegments("/_ma-auth")
        || path.StartsWithSegments(LoginPath) || path.StartsWithSegments(MfaPath)
        || path.StartsWithSegments(HealthPath) || Is(Normalize(path), ErrorPath);

    /// <summary>The whole access rule, pure.</summary>
    public static GateDecision Decide(string method, PathString path, ClaimsPrincipal? user, ThinkTankHosting hosting)
    {
        if (IsAnonymousAllowed(method, path)) return GateDecision.Allow;
        if (user?.Identity?.IsAuthenticated != true) return GateDecision.Challenge;
        if (!hosting.IsUserAllowed(user.Identity.Name)) return GateDecision.Forbidden;
        if (hosting.RequireMfa && !user.HasClaim(MaClaims.Amr, "mfa") && !IsAuthSurface(path)) return GateDecision.EnrollMfa;
        return GateDecision.Allow;
    }

    /// <summary>
    /// A same-site path only: starts with one <c>/</c>, no <c>//</c> or <c>/\</c>, no control characters.
    /// Stricter than the auth library's own check on purpose.
    /// </summary>
    public static bool IsSafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/') return false;
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return false;
        foreach (var ch in url)
            if (ch < 0x20 || ch == 0x7F) return false;
        return true;
    }

    /// <summary>True for a browser navigation that should be sent to a page instead of being answered with a status code.</summary>
    public static bool IsPageNavigation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path;
        if (path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_framework") || path.StartsWithSegments("/_content"))
            return false;
        return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(PathString path)
    {
        var value = path.Value ?? "";
        return value.Length > 1 ? value.TrimEnd('/') : value;
    }

    private static bool Is(string normalizedPath, string expected) =>
        string.Equals(normalizedPath, expected, StringComparison.OrdinalIgnoreCase);

    // ── Services ─────────────────────────────────────────────────────────────

    /// <summary>Fallback policy, antiforgery cookie posture and the sign-in rate limit.</summary>
    public static IServiceCollection AddSignInGate(this IServiceCollection services, ThinkTankHosting hosting)
    {
        // Everything without a policy of its own gets this one, and a bare [Authorize] (every Razor page) too, so
        // the allow-list holds on both. Resource is the HttpContext under endpoint routing.
        var gatePolicy = new AuthorizationPolicyBuilder()
            .RequireAssertion(context => context.Resource is HttpContext http
                ? Decide(http.Request.Method, http.Request.Path, context.User, hosting) == GateDecision.Allow
                : context.User.Identity?.IsAuthenticated == true && hosting.IsUserAllowed(context.User.Identity.Name))
            .Build();
        services.AddAuthorizationBuilder().SetFallbackPolicy(gatePolicy).SetDefaultPolicy(gatePolicy);

        services.AddAntiforgery(o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = hosting.IsPublic ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = hosting.LoginPermitLimit,
                    Window = TimeSpan.FromSeconds(hosting.LoginWindowSeconds),
                    QueueLimit = 0,
                }));
        });
        return services;
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rate limiter, authentication, the gate, then MindAttic.Authentication's own middleware (authorization with
    /// the fallback policy, its forced password-change / TOTP-enrolment step, and the CSP on the auth pages).
    /// Must sit before static files and every endpoint.
    /// </summary>
    public static WebApplication UseSignInGate(this WebApplication app, ThinkTankHosting hosting)
    {
        app.UseRateLimiter();
        app.UseAuthentication();

        app.Use(async (context, next) =>
        {
            var request = context.Request;
            switch (Decide(request.Method, request.Path, context.User, hosting))
            {
                case GateDecision.Challenge:
                    if (IsPageNavigation(request))
                    {
                        var target = $"{request.Path}{request.QueryString}";
                        context.Response.Redirect(IsSafeReturnUrl(target) && target != "/"
                            ? $"{LoginPath}?returnUrl={Uri.EscapeDataString(target)}"
                            : LoginPath);
                    }
                    else
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;

                case GateDecision.Forbidden:
                    // A real account that is not allowed here: end its session rather than leave it signed in to nothing.
                    await context.SignOutAsync(MaSchemes.Cookie);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync("This account is not permitted on ThinkTank.");
                    return;

                case GateDecision.EnrollMfa:
                    if (IsPageNavigation(request)) context.Response.Redirect(MfaSetupPath);
                    else context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
            }

            // The sign-in posts redirect to the form's returnUrl. Refuse anything that is not a plain same-site
            // path before the auth library sees it.
            if (HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments("/_ma-auth") && request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(context.RequestAborted);
                var returnUrl = form["returnUrl"].ToString();
                if (returnUrl.Length > 0 && !IsSafeReturnUrl(returnUrl))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }

            await next();
        });

        app.UseMindAtticAuthentication();
        return app;
    }
}
