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

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdh.ExportSubjectPublicKeyInfo();

        lock (_lock)
        {
            _sessions[sessionId] = new SessionKeys
            {
                SessionId = sessionId,
                ServerEcdh = ecdh,
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

            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(clientPublicKey, out _);
            session.SharedSecret = session.ServerEcdh.DeriveKeyFromHash(
                peerEcdh.PublicKey, HashAlgorithmName.SHA256, null, null);
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
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                if (session.SharedSecret != null)
                    CryptographicOperations.ZeroMemory(session.SharedSecret);
                session.ServerEcdh.Dispose();
                _sessions.Remove(sessionId);
            }
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

            foreach (var sid in expiredSessions)
            {
                var session = _sessions[sid];
                if (session.SharedSecret != null)
                    CryptographicOperations.ZeroMemory(session.SharedSecret);
                session.ServerEcdh.Dispose();
                _sessions.Remove(sid);
                expiredCount++;
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredCount);
        }
    }

    private class SessionKeys
    {
        public string SessionId { get; set; } = string.Empty;
        public ECDiffieHellman ServerEcdh { get; set; } = null!;
        public byte[]? ClientPublicKey { get; set; }
        public byte[]? SharedSecret { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
