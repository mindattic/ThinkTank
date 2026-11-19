using System.Net;

namespace ThinkTank.Core.Services;

/// <summary>
/// What a user is told when an LLM call fails (TT-T44). A provider's raw error text can carry account names,
/// request ids, quota details or a fragment of the request, so it goes to the server log only; the page, the
/// transcript and the diagnostics panel get one of these plain sentences plus a short reference that matches
/// the log line.
/// </summary>
public static class ProviderErrorText
{
    /// <summary>A short random id to print beside the friendly text and in the log entry with the details.</summary>
    public static string NewReference() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// A sentence for <paramref name="ex"/> that is safe to show and to store. <paramref name="who"/> names the
    /// participant or provider ("Claude"); null reads "This provider".
    /// </summary>
    public static string Describe(Exception ex, string? who = null, string? reference = null)
    {
        var subject = string.IsNullOrWhiteSpace(who) ? "This provider" : who.Trim();
        var text = Classify(ex) switch
        {
            Kind.KeyRefused => $"{subject} is unavailable right now (the provider refused ThinkTank's key).",
            Kind.NoKey => $"{subject} is unavailable: no API key is set up for this provider.",
            Kind.OutOfCredit => $"{subject} is unavailable: the provider account is out of credit.",
            Kind.RateLimited => $"{subject} is busy (rate limited). Try again in a minute.",
            Kind.ProviderDown => $"{subject} had a problem on the provider's side. Try again shortly.",
            Kind.Timeout => $"{subject} took too long to answer.",
            Kind.LimitReached => FindUserFacing(ex)!.Message,   // our own limit messages are written for users
            _ => $"{subject} couldn't answer because of an unexpected error.",
        };
        return reference is null ? text : $"{text} (ref {reference})";
    }

    private static UserFacingException? FindUserFacing(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is UserFacingException u) return u;
        return null;
    }

    private enum Kind { Unknown, KeyRefused, NoKey, OutOfCredit, RateLimited, ProviderDown, Timeout, LimitReached }

    private static Kind Classify(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case UserFacingException:
                    return Kind.LimitReached;
                case HttpRequestException { StatusCode: { } status }:
                    return status switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Kind.KeyRefused,
                        HttpStatusCode.PaymentRequired => Kind.OutOfCredit,
                        HttpStatusCode.TooManyRequests => Kind.RateLimited,
                        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => Kind.Timeout,
                        >= HttpStatusCode.InternalServerError => Kind.ProviderDown,
                        _ => Kind.Unknown,
                    };
                case TimeoutException:
                case TaskCanceledException:
                    return Kind.Timeout;
                case InvalidOperationException when e.Message.Contains("No API key configured", StringComparison.OrdinalIgnoreCase):
                    return Kind.NoKey;
                case InvalidOperationException when e.Message.Contains("Fix or replace the key", StringComparison.OrdinalIgnoreCase):
                    return Kind.KeyRefused;
            }
        }
        return Kind.Unknown;
    }
}

/// <summary>
/// Thrown by ThinkTank's own limits (spend, budget, input). Its message is written for the user and is shown as is.
/// </summary>
public sealed class UserFacingException(string message) : Exception(message);
