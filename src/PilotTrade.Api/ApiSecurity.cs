namespace PilotTrade.Api;

using System.Net;

public static class ApiSecurity
{
    public const string OwnerHeader = "X-PilotTrade-Owner-Token";
    public const string CsrfHeader = "X-PilotTrade-CSRF";
    private static readonly string[] DevOrigins = { "http://localhost:5173", "http://127.0.0.1:5173" };

    public static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]" || host == "::1";

    // Parsed, never substring-matched: http://localhost.evil.test and http://evil.test/localhost both fail.
    public static bool IsAllowedOrigin(string? origin, string? requestHost)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.UserInfo.Length > 0 || uri.PathAndQuery != "/" || uri.Fragment.Length > 0 || origin.EndsWith('/')) return false;
        if (!IsLoopbackHost(uri.Host)) return false;
        return string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase)
            || DevOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static IResult Forbidden() => Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);

    // Every /api route: loopback caller, loopback Host (blocks DNS rebinding), valid owner token.
    public static async ValueTask<object?> RequireOwner(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var remote = http.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote) || !IsLoopbackHost(http.Request.Host.Host)) return Forbidden();

        var auth = http.RequestServices.GetRequiredService<IOwnerAuthorizationService>();
        if (!await auth.AuthorizeAsync(http.Request.Headers[OwnerHeader].ToString(), string.Empty, http.RequestAborted)) return Forbidden();
        return await next(ctx);
    }

    // State-changing routes also need an allowed Origin and a single-use CSRF token.
    public static async ValueTask<object?> RequireBrowserGuards(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (!IsAllowedOrigin(http.Request.Headers.Origin.ToString(), http.Request.Host.Value)) return Forbidden();
        var csrf = http.RequestServices.GetRequiredService<ICsrfTokenService>();
        if (!csrf.Validate(http.Request.Headers[CsrfHeader].ToString())) return Forbidden();
        return await next(ctx);
    }

    // Maps broker failures to fixed codes; a broker body or exception detail never reaches the client.
    public static async ValueTask<object?> SanitizeBrokerErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try { return await next(ctx); }
        catch (BrokerException ex)
        {
            var status = ex.Kind switch
            {
                BrokerErrorKind.CredentialsMissing => StatusCodes.Status409Conflict,
                BrokerErrorKind.AccountNotFound or BrokerErrorKind.NotFound => StatusCodes.Status404NotFound,
                BrokerErrorKind.AccountMismatch => StatusCodes.Status409Conflict,
                BrokerErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
                BrokerErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
                BrokerErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway
            };
            return Results.Json(new { error = ex.Kind.ToString() }, statusCode: status);
        }
    }
}
