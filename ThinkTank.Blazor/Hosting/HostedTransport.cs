using Microsoft.AspNetCore.HttpOverrides;

namespace ThinkTank.Blazor.Hosting;

/// <summary>
/// Transport rules and response headers (TT-T45): forwarded headers from the platform proxy, the host
/// allow-list, HTTPS redirect and HSTS, and the security headers with a Content-Security-Policy that fits
/// Blazor Server. First in the pipeline, before authentication and every endpoint.
/// </summary>
public static class HostedTransport
{
    public const string HealthPath = "/healthz";

    /// <summary>
    /// The page CSP. Blazor Server needs no inline script and no eval; it needs its own scripts, a WebSocket back
    /// to the same host, and inline style attributes (components set <c>style="..."</c>). MindAttic.Authentication
    /// sets its own stricter, nonce-based policy on its pages (/login, /mfa, /account, /_ma-auth); that one is kept.
    /// </summary>
    public static string ContentSecurityPolicy(HttpRequest request)
    {
        var host = request.Host.Value ?? "";
        var socket = request.IsHttps ? $"wss://{host}" : $"ws://{host} wss://{host}";
        return "default-src 'self'; " +
               "script-src 'self'; " +
               "style-src 'self' 'unsafe-inline'; " +
               "img-src 'self' data:; " +
               "font-src 'self' data:; " +
               $"connect-src 'self' {socket}; " +
               "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    }

    public static WebApplication UseThinkTankTransport(this WebApplication app, ThinkTankHosting hosting)
    {
        if (hosting.IsPublic && hosting.TrustForwardedHeaders)
        {
            // The platform's front end is the only way in and appends the real client address and scheme.
            // Must run first: HTTPS redirect and the secure cookie read Request.Scheme, and behind a
            // TLS-terminating proxy the unforwarded scheme is http, which would redirect-loop every request.
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = hosting.ForwardLimit,
            };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }

        if (hosting.IsPublic)
        {
            app.Use(async (context, next) =>
            {
                var request = context.Request;
                // The platform's health probe calls by address, over plain HTTP.
                if (!IsHealthProbe(request))
                {
                    if (!IsHostAllowed(request.Host.Host, hosting.AllowedHosts))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }
                    if (hosting.RequireHttps && !request.IsHttps)
                    {
                        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                            context.Response.Redirect($"https://{request.Host.Host}{request.PathBase}{request.Path}{request.QueryString}",
                                permanent: true, preserveMethod: true);
                        else
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }
                }
                await next();
            });
        }
        else if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        var isDevelopment = app.Environment.IsDevelopment();
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (hosting.IsPublic && context.Request.IsHttps && hosting.HstsMaxAgeDays > 0)
                    headers.StrictTransportSecurity = $"max-age={hosting.HstsMaxAgeDays * 86_400L}; includeSubDomains";
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                if (!headers.ContainsKey("X-Frame-Options")) headers.XFrameOptions = "DENY";
                // The auth pages set their own nonce policy first; never replace it. Not in Development, where
                // hot reload injects a script that talks to its own port.
                if (!isDevelopment && !headers.ContainsKey("Content-Security-Policy"))
                    headers.ContentSecurityPolicy = ContentSecurityPolicy(context.Request);
                return Task.CompletedTask;
            });
            await next();
        });

        return app;
    }

    /// <summary>Host header check: exact, case-insensitive match against the configured names.</summary>
    public static bool IsHostAllowed(string? host, IReadOnlyList<string> allowed) =>
        !string.IsNullOrEmpty(host) && allowed.Any(a => string.Equals(a, host, StringComparison.OrdinalIgnoreCase));

    private static bool IsHealthProbe(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Path.StartsWithSegments(HealthPath);
}
