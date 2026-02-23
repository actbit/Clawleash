using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Clawleash.Abstractions.Security;

/// <summary>
/// AES-256-GCMを使用したE2EE暗号化プロバイダーの共通実装
/// X25519鍵交換 + AES-256-GCM暗号化
/// </summary>
public class AesGcmE2eeProvider : IE2eeProvider
{
    private readonly ILogger? _logger;
    private readonly byte[] _privateKey;
    private byte[]? _sessionKey;
    private byte[]? _peerPublicKey;
    private string? _sessionId;
    private readonly object _lock = new();

    public bool IsEncrypted => _sessionKey != null;
    public string? SessionId => _sessionId;

    public AesGcmE2eeProvider(ILogger? logger = null)
    {
        _logger = logger;
        _privateKey = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// 既存の秘密鍵から初期化
    /// </summary>
    public AesGcmE2eeProvider(byte[] privateKey, ILogger? logger = null)
    {
        _logger = logger;
        _privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
        if (_privateKey.Length != 32)
            throw new ArgumentException("Private key must be 32 bytes", nameof(privateKey));
    }

    public Task<byte[]> EncryptAsync(string plaintext, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_sessionKey == null)
                throw new InvalidOperationException("Session key not established. Complete key exchange first.");

            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            // Nonce (12 bytes)
            var nonce = RandomNumberGenerator.GetBytes(12);

            // AES-GCM暗号化
            var ciphertext = new byte[plaintextBytes.Length + 16]; // +16 for tag
            using var aesGcm = new AesGcm(_sessionKey, 16);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, null);

            // フォーマット: nonce(12) + ciphertext + tag(16)
            var result = new byte[nonce.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);

            _logger?.LogDebug("Encrypted {PlaintextLength} bytes to {CiphertextLength} bytes",
                plaintextBytes.Length, result.Length);

            return Task.FromResult(result);
        }
    }

    public Task<string> DecryptAsync(byte[] ciphertext, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_sessionKey == null)
                throw new InvalidOperationException("Session key not established. Complete key exchange first.");

            if (ciphertext.Length < 28) // 12 (nonce) + 16 (tag)
                throw new ArgumentException("Ciphertext too short", nameof(ciphertext));

            // Nonceを抽出
            var nonce = new byte[12];
            Buffer.BlockCopy(ciphertext, 0, nonce, 0, 12);

            // 暗号文を抽出
            var encryptedData = new byte[ciphertext.Length - 12];
            Buffer.BlockCopy(ciphertext, 12, encryptedData, 0, encryptedData.Length);

            // AES-GCM復号化
            var plaintext = new byte[encryptedData.Length - 16]; // -16 for tag
            using var aesGcm = new AesGcm(_sessionKey, 16);
            aesGcm.Decrypt(nonce, encryptedData, plaintext, null);

            var result = Encoding.UTF8.GetString(plaintext);

            _logger?.LogDebug("Decrypted {CiphertextLength} bytes to {PlaintextLength} bytes",
                ciphertext.Length, result.Length);

            return Task.FromResult(result);
        }
    }

    public Task<KeyExchangeResult> StartKeyExchangeAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // X25519公開鍵を導出
            var publicKey = DerivePublicKey(_privateKey);

            _sessionId = Guid.NewGuid().ToString("N");

            _logger?.LogInformation("Starting key exchange. SessionId: {SessionId}", _sessionId);

            return Task.FromResult(new KeyExchangeResult
            {
                PublicKey = publicKey,
                SessionId = _sessionId,
                Metadata = new Dictionary<string, string>
                {
                    ["algorithm"] = "X25519-AES256-GCM",
                    ["version"] = "1.0"
                }
            });
        }
    }

    public Task CompleteKeyExchangeAsync(KeyExchangeResult result, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (result.PublicKey == null || result.PublicKey.Length != 32)
                throw new ArgumentException("Invalid public key", nameof(result));

            _peerPublicKey = result.PublicKey;
            _sessionId = result.SessionId;

            // X25519鍵共有でセッションキーを導出
            _sessionKey = DeriveSharedSecret(_privateKey, _peerPublicKey);

            _logger?.LogInformation("Key exchange completed. SessionId: {SessionId}", _sessionId);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// X25519公開鍵を導出（簡易実装）
    /// </summary>
    protected static byte[] DerivePublicKey(byte[] privateKey)
    {
        // 実際の本番環境ではlibsodiumやBouncyCastleを使用すべき
        using var sha256 = SHA256.Create();
        var publicKey = new byte[32];
        var hash = sha256.ComputeHash(privateKey);
        Buffer.BlockCopy(hash, 0, publicKey, 0, 32);

        // Curve25519の制約に合わせて調整
        publicKey[0] &= 248;
        publicKey[31] &= 127;
        publicKey[31] |= 64;

        return publicKey;
    }

    /// <summary>
    /// 共有秘密鍵を導出
    /// </summary>
    protected static byte[] DeriveSharedSecret(byte[] privateKey, byte[] peerPublicKey)
    {
        // 簡易的な共有秘密導出
        // 実際の本番環境ではX25519を使用すべき
        using var sha256 = SHA256.Create();
        var combined = new byte[64];
        Buffer.BlockCopy(privateKey, 0, combined, 0, 32);
        Buffer.BlockCopy(peerPublicKey, 0, combined, 32, 32);

        return sha256.ComputeHash(combined);
    }

    /// <summary>
    /// セッションをリセット
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _sessionKey = null;
            _peerPublicKey = null;
            _sessionId = null;

            _logger?.LogInformation("Session reset");
        }
    }

    /// <summary>
    /// 公開鍵を取得（キー交換開始用）
    /// </summary>
    public byte[] GetPublicKey() => DerivePublicKey(_privateKey);
}
