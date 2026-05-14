using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Clawleash.Abstractions.Security;

/// <summary>
/// AES-256-GCMを使用したE2EE暗号化プロバイダーの共通実装
/// ECDH-P256鍵交換 + AES-256-GCM暗号化
/// チャンネルごとの共通鍵をサポート
/// </summary>
public class AesGcmE2eeProvider : IE2eeProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly ILogger? _logger;
    private readonly ECDiffieHellman _ecdh;
    private byte[]? _sessionKey;
    private byte[]? _peerPublicKey;
    private string? _sessionId;
    private readonly Dictionary<string, byte[]> _channelKeys = new();
    private readonly object _lock = new();
    private long _nonceCounter;

    public bool IsEncrypted => _sessionKey != null;
    public string? SessionId => _sessionId;

    public AesGcmE2eeProvider(ILogger? logger = null)
    {
        _logger = logger;
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }

    public AesGcmE2eeProvider(byte[] ecPrivateKey, ILogger? logger = null)
    {
        _logger = logger;
        _ecdh = ECDiffieHellman.Create();
        _ecdh.ImportECPrivateKey(ecPrivateKey, out _);
    }

    public Task<byte[]> EncryptAsync(string plaintext, CancellationToken ct = default)
    {
        return EncryptAsync(plaintext, null, ct);
    }

    public Task<byte[]> EncryptAsync(string plaintext, string? channelId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var keyToUse = GetKeyForChannel(channelId);
            if (keyToUse == null)
                throw new InvalidOperationException("No key available for encryption.");

            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            var nonce = GenerateNonce();

            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            using var aesGcm = new AesGcm(keyToUse, TagSize);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

            _logger?.LogDebug("Encrypted {PlaintextLength} bytes to {ResultLength} bytes (channel: {ChannelId})",
                plaintextBytes.Length, result.Length, channelId ?? "session");

            return Task.FromResult(result);
        }
    }

    public Task<string> DecryptAsync(byte[] ciphertext, CancellationToken ct = default)
    {
        return DecryptAsync(ciphertext, null, ct);
    }

    public Task<string> DecryptAsync(byte[] ciphertext, string? channelId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var keyToUse = GetKeyForChannel(channelId);
            if (keyToUse == null)
                throw new InvalidOperationException("No key available for decryption.");

            if (ciphertext.Length < NonceSize + TagSize)
                throw new ArgumentException("Ciphertext too short", nameof(ciphertext));

            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSize);

            var ciphertextLength = ciphertext.Length - NonceSize - TagSize;

            var encryptedData = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(ciphertext, NonceSize, encryptedData, 0, ciphertextLength);
            Buffer.BlockCopy(ciphertext, NonceSize + ciphertextLength, tag, 0, TagSize);

            var plaintext = new byte[ciphertextLength];
            using var aesGcm = new AesGcm(keyToUse, TagSize);
            aesGcm.Decrypt(nonce, encryptedData, plaintext, tag);

            var result = Encoding.UTF8.GetString(plaintext);

            _logger?.LogDebug("Decrypted {CiphertextLength} bytes to {PlaintextLength} bytes (channel: {ChannelId})",
                ciphertext.Length, result.Length, channelId ?? "session");

            return Task.FromResult(result);
        }
    }

    private byte[]? GetKeyForChannel(string? channelId)
    {
        if (!string.IsNullOrEmpty(channelId) && _channelKeys.TryGetValue(channelId, out var channelKey))
        {
            return channelKey;
        }
        return _sessionKey;
    }

    public void SetChannelKey(string channelId, byte[] encryptedKey)
    {
        lock (_lock)
        {
            if (_sessionKey == null)
                throw new InvalidOperationException("Session key not established.");

            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(encryptedKey, 0, nonce, 0, NonceSize);

            var ciphertextLength = encryptedKey.Length - NonceSize - TagSize;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(encryptedKey, NonceSize, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(encryptedKey, NonceSize + ciphertextLength, tag, 0, TagSize);

            var channelKey = new byte[KeySize];
            using var aesGcm = new AesGcm(_sessionKey, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, channelKey, tag);

            _channelKeys[channelId] = channelKey;

            _logger?.LogInformation("Channel key set for {ChannelId}", channelId);
        }
    }

    public void SetPlainChannelKey(string channelId, byte[] plainKey)
    {
        lock (_lock)
        {
            _channelKeys[channelId] = plainKey;
            _logger?.LogWarning("Plain channel key set for {ChannelId} (E2EE disabled)", channelId);
        }
    }

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
            var publicKey = _ecdh.ExportSubjectPublicKeyInfo();

            _sessionId = Guid.NewGuid().ToString("N");

            _logger?.LogInformation("Starting key exchange. SessionId: {SessionId}", _sessionId);

            return Task.FromResult(new KeyExchangeResult
            {
                PublicKey = publicKey,
                SessionId = _sessionId,
                Metadata = new Dictionary<string, string>
                {
                    ["algorithm"] = "ECDH-P256-AES256-GCM",
                    ["version"] = "2.0"
                }
            });
        }
    }

    public Task CompleteKeyExchangeAsync(KeyExchangeResult result, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (result.PublicKey == null || result.PublicKey.Length == 0)
                throw new ArgumentException("Invalid public key", nameof(result));

            _peerPublicKey = result.PublicKey;
            _sessionId = result.SessionId;

            _sessionKey = DeriveSharedSecret(result.PublicKey);

            _logger?.LogInformation("Key exchange completed. SessionId: {SessionId}", _sessionId);

            return Task.CompletedTask;
        }
    }

    private byte[] DeriveSharedSecret(byte[] peerPublicKeyInfo)
    {
        using var peerEcdh = ECDiffieHellman.Create();
        peerEcdh.ImportSubjectPublicKeyInfo(peerPublicKeyInfo, out _);
        return _ecdh.DeriveKeyFromHash(peerEcdh.PublicKey, HashAlgorithmName.SHA256, null, null);
    }

    private byte[] GenerateNonce()
    {
        var counter = Interlocked.Increment(ref _nonceCounter);
        if (counter <= 0)
            throw new CryptographicException("Nonce counter overflow");

        var nonce = new byte[NonceSize];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(0, 8), counter);
        RandomNumberGenerator.Fill(nonce.AsSpan(8, 4));
        return nonce;
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (_sessionKey != null)
                CryptographicOperations.ZeroMemory(_sessionKey);
            _sessionKey = null;

            foreach (var key in _channelKeys.Values)
                CryptographicOperations.ZeroMemory(key);
            _channelKeys.Clear();

            _peerPublicKey = null;
            _sessionId = null;
            _nonceCounter = 0;

            _logger?.LogInformation("Session reset");
        }
    }

    public byte[] GetPublicKey() => _ecdh.ExportSubjectPublicKeyInfo();
}
