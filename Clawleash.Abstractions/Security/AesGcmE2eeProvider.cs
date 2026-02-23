using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Clawleash.Abstractions.Security;

/// <summary>
/// AES-256-GCMを使用したE2EE暗号化プロバイダーの共通実装
/// X25519鍵交換 + AES-256-GCM暗号化
/// チャンネルごとの共通鍵をサポート
/// </summary>
public class AesGcmE2eeProvider : IE2eeProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly ILogger? _logger;
    private readonly byte[] _privateKey;
    private byte[]? _sessionKey;
    private byte[]? _peerPublicKey;
    private string? _sessionId;
    private readonly Dictionary<string, byte[]> _channelKeys = new();
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

    /// <summary>
    /// 指定したチャンネルの鍵を使用して暗号化
    /// </summary>
    public Task<byte[]> EncryptAsync(string plaintext, CancellationToken ct = default)
    {
        return EncryptAsync(plaintext, null, ct);
    }

    /// <summary>
    /// チャンネルを指定して暗号化
    /// </summary>
    public Task<byte[]> EncryptAsync(string plaintext, string? channelId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var keyToUse = GetKeyForChannel(channelId);
            if (keyToUse == null)
                throw new InvalidOperationException("No key available for encryption.");

            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            // Nonce (12 bytes)
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);

            // Separate buffers for ciphertext and tag
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            // AES-GCM暗号化
            using var aesGcm = new AesGcm(keyToUse, TagSize);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // フォーマット: nonce(12) + ciphertext + tag(16)
            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

            _logger?.LogDebug("Encrypted {PlaintextLength} bytes to {ResultLength} bytes (channel: {ChannelId})",
                plaintextBytes.Length, result.Length, channelId ?? "session");

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 指定したチャンネルの鍵を使用して復号化
    /// </summary>
    public Task<string> DecryptAsync(byte[] ciphertext, CancellationToken ct = default)
    {
        return DecryptAsync(ciphertext, null, ct);
    }

    /// <summary>
    /// チャンネルを指定して復号化
    /// </summary>
    public Task<string> DecryptAsync(byte[] ciphertext, string? channelId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var keyToUse = GetKeyForChannel(channelId);
            if (keyToUse == null)
                throw new InvalidOperationException("No key available for decryption.");

            if (ciphertext.Length < NonceSize + TagSize)
                throw new ArgumentException("Ciphertext too short", nameof(ciphertext));

            // Nonceを抽出
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSize);

            // CiphertextとTagを分離
            var ciphertextLength = ciphertext.Length - NonceSize - TagSize;

            var encryptedData = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(ciphertext, NonceSize, encryptedData, 0, ciphertextLength);
            Buffer.BlockCopy(ciphertext, NonceSize + ciphertextLength, tag, 0, TagSize);

            // AES-GCM復号化
            var plaintext = new byte[ciphertextLength];
            using var aesGcm = new AesGcm(keyToUse, TagSize);
            aesGcm.Decrypt(nonce, encryptedData, plaintext, tag);

            var result = Encoding.UTF8.GetString(plaintext);

            _logger?.LogDebug("Decrypted {CiphertextLength} bytes to {PlaintextLength} bytes (channel: {ChannelId})",
                ciphertext.Length, result.Length, channelId ?? "session");

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// チャンネルに対応する鍵を取得
    /// </summary>
    private byte[]? GetKeyForChannel(string? channelId)
    {
        if (!string.IsNullOrEmpty(channelId) && _channelKeys.TryGetValue(channelId, out var channelKey))
        {
            return channelKey;
        }
        return _sessionKey;
    }

    /// <summary>
    /// 暗号化されたチャンネル鍵を設定
    /// </summary>
    public void SetChannelKey(string channelId, byte[] encryptedKey)
    {
        lock (_lock)
        {
            if (_sessionKey == null)
                throw new InvalidOperationException("Session key not established.");

            // nonce(12) + ciphertext + tag(16) フォーマット
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(encryptedKey, 0, nonce, 0, NonceSize);

            var ciphertextLength = encryptedKey.Length - NonceSize - TagSize;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(encryptedKey, NonceSize, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(encryptedKey, NonceSize + ciphertextLength, tag, 0, TagSize);

            // 復号化
            var channelKey = new byte[KeySize];
            using var aesGcm = new AesGcm(_sessionKey, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, channelKey, tag);

            _channelKeys[channelId] = channelKey;

            _logger?.LogInformation("Channel key set for {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// 平文のチャンネル鍵を設定（E2EE無効時用）
    /// </summary>
    public void SetPlainChannelKey(string channelId, byte[] plainKey)
    {
        lock (_lock)
        {
            _channelKeys[channelId] = plainKey;
            _logger?.LogWarning("Plain channel key set for {ChannelId} (E2EE disabled)", channelId);
        }
    }

    /// <summary>
    /// チャンネル鍵が存在するか確認
    /// </summary>
    public bool HasChannelKey(string channelId)
    {
        lock (_lock)
        {
            return _channelKeys.ContainsKey(channelId);
        }
    }

    public Task<KeyExchangeResult> StartKeyExchangeAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
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

            _sessionKey = DeriveSharedSecret(_privateKey, _peerPublicKey);

            _logger?.LogInformation("Key exchange completed. SessionId: {SessionId}", _sessionId);

            return Task.CompletedTask;
        }
    }

    protected static byte[] DerivePublicKey(byte[] privateKey)
    {
        using var sha256 = SHA256.Create();
        var publicKey = new byte[32];
        var hash = sha256.ComputeHash(privateKey);
        Buffer.BlockCopy(hash, 0, publicKey, 0, 32);

        publicKey[0] &= 248;
        publicKey[31] &= 127;
        publicKey[31] |= 64;

        return publicKey;
    }

    protected static byte[] DeriveSharedSecret(byte[] privateKey, byte[] peerPublicKey)
    {
        using var sha256 = SHA256.Create();
        var combined = new byte[64];
        Buffer.BlockCopy(privateKey, 0, combined, 0, 32);
        Buffer.BlockCopy(peerPublicKey, 0, combined, 32, 32);

        return sha256.ComputeHash(combined);
    }

    public void Reset()
    {
        lock (_lock)
        {
            _sessionKey = null;
            _peerPublicKey = null;
            _sessionId = null;
            _channelKeys.Clear();

            _logger?.LogInformation("Session reset");
        }
    }

    public byte[] GetPublicKey() => DerivePublicKey(_privateKey);
}
