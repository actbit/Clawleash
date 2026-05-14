using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clawleash.Server.Security;

/// <summary>
/// E2EE検証ミドルウェア
/// HMAC-SHA256による鍵所有証明を検証
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
        if (context.Request.Headers.TryGetValue("X-E2EE-Session", out var sessionId))
        {
            var session = sessionId.ToString();
            var sharedSecret = _keyManager.GetSharedSecret(session);

            if (sharedSecret == null)
            {
                _logger.LogWarning("E2EE session not found: {SessionId}", session);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-E2EE-HMAC", out var providedHmac))
            {
                _logger.LogWarning("E2EE HMAC missing for session: {SessionId}", session);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var computedHmac = ComputeSessionHmac(sharedSecret, session);
            var providedHmacBytes = TryDecodeHex(providedHmac.ToString());

            if (providedHmacBytes == null || !CryptographicOperations.FixedTimeEquals(providedHmacBytes, computedHmac))
            {
                _logger.LogWarning("E2EE HMAC verification failed for session: {SessionId}", session);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items["E2eeSessionId"] = session;
            context.Items["E2eeEnabled"] = true;
            _logger.LogDebug("E2EE session verified: {SessionId}", session);
        }

        await _next(context);
    }

    internal static byte[] ComputeSessionHmac(byte[] sharedSecret, string sessionId)
    {
        using var hmac = new HMACSHA256(sharedSecret);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
    }

    private static byte[]? TryDecodeHex(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch
        {
            return null;
        }
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
