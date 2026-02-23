using System.Text;
using System.Text.Json;

namespace Clawleash.Server.Security;

/// <summary>
/// E2EE検証ミドルウェア（オプション）
/// </summary>
public class E2eeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<E2eeMiddleware> _logger;
    private readonly KeyManager _keyManager;

    public E2eeMiddleware(RequestDelegate next, ILogger<E2eeMiddleware> logger, KeyManager keyManager)
    {
        _next = next;
        _logger = logger;
        _keyManager = keyManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check for E2EE header
        if (context.Request.Headers.TryGetValue("X-E2EE-Session", out var sessionId))
        {
            var session = sessionId.ToString();
            var sharedSecret = _keyManager.GetSharedSecret(session);

            if (sharedSecret != null)
            {
                context.Items["E2eeSessionId"] = session;
                context.Items["E2eeEnabled"] = true;
                _logger.LogDebug("E2EE session verified: {SessionId}", session);
            }
        }

        await _next(context);
    }
}

/// <summary>
/// E2EE拡張メソッド
/// </summary>
public static class E2eeExtensions
{
    public static bool IsE2eeEnabled(this HttpContext context)
    {
        return context.Items.TryGetValue("E2eeEnabled", out var enabled) && enabled is true;
    }

    public static string? GetE2eeSessionId(this HttpContext context)
    {
        return context.Items.TryGetValue("E2eeSessionId", out var sessionId)
            ? sessionId?.ToString()
            : null;
    }
}
