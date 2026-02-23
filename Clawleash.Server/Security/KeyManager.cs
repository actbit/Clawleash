using System.Security.Cryptography;
using System.Text.Json;

namespace Clawleash.Server.Security;

/// <summary>
/// E2EE鍵管理サービス
/// </summary>
public class KeyManager
{
    private readonly ILogger<KeyManager> _logger;
    private readonly Dictionary<string, SessionKeys> _sessions = new();
    private readonly object _lock = new();

    public KeyManager(ILogger<KeyManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// サーバー公開鍵を生成
    /// </summary>
    public byte[] GenerateServerPublicKey(out string sessionId)
    {
        sessionId = Guid.NewGuid().ToString("N");

        var privateKey = RandomNumberGenerator.GetBytes(32);
        var publicKey = DerivePublicKey(privateKey);

        lock (_lock)
        {
            _sessions[sessionId] = new SessionKeys
            {
                SessionId = sessionId,
                ServerPrivateKey = privateKey,
                CreatedAt = DateTime.UtcNow
            };
        }

        _logger.LogDebug("Generated server keypair for session {SessionId}", sessionId);

        return publicKey;
    }

    /// <summary>
    /// 鍵交換を完了
    /// </summary>
    public void CompleteKeyExchange(string sessionId, byte[] clientPublicKey)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return;
            }

            session.ClientPublicKey = clientPublicKey;
            session.SharedSecret = DeriveSharedSecret(session.ServerPrivateKey, clientPublicKey);
            session.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Key exchange completed for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// セッションの共有秘密鍵を取得
    /// </summary>
    public byte[]? GetSharedSecret(string sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session.SharedSecret : null;
        }
    }

    /// <summary>
    /// セッションを削除
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
            _logger.LogDebug("Session removed: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 期限切れセッションをクリーンアップ
    /// </summary>
    public void CleanupExpiredSessions(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var expiredCount = 0;

        lock (_lock)
        {
            var expiredSessions = _sessions
                .Where(kvp => kvp.Value.CreatedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in expiredSessions)
            {
                _sessions.Remove(sessionId);
                expiredCount++;
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredCount);
        }
    }

    private static byte[] DerivePublicKey(byte[] privateKey)
    {
        using var sha256 = SHA256.Create();
        var publicKey = sha256.ComputeHash(privateKey);

        // Curve25519 adjustments
        publicKey[0] &= 248;
        publicKey[31] &= 127;
        publicKey[31] |= 64;

        return publicKey;
    }

    private static byte[] DeriveSharedSecret(byte[] privateKey, byte[] peerPublicKey)
    {
        using var sha256 = SHA256.Create();
        var combined = new byte[64];
        Buffer.BlockCopy(privateKey, 0, combined, 0, 32);
        Buffer.BlockCopy(peerPublicKey, 0, combined, 32, 32);
        return sha256.ComputeHash(combined);
    }

    private class SessionKeys
    {
        public string SessionId { get; set; } = string.Empty;
        public byte[] ServerPrivateKey { get; set; } = Array.Empty<byte>();
        public byte[]? ClientPublicKey { get; set; }
        public byte[]? SharedSecret { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
