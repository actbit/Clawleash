using System.Buffers.Binary;
using System.Text;
using Clawleash.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebRTC.Security;

/// <summary>
/// WebRTC DataChannel用E2EE暗号化プロバイダー
/// 共通実装を継承し、DataChannel用のエンベロープ形式を追加
/// </summary>
public class WebRtcE2eeProvider : Abstractions.Security.AesGcmE2eeProvider
{
    private readonly ILogger<WebRtcE2eeProvider>? _webRtcLogger;

    public WebRtcE2eeProvider(ILogger<WebRtcE2eeProvider>? logger = null)
        : base(logger)
    {
        _webRtcLogger = logger;
    }

    public WebRtcE2eeProvider(byte[] privateKey, ILogger<WebRtcE2eeProvider>? logger = null)
        : base(privateKey, logger)
    {
        _webRtcLogger = logger;
    }

    /// <summary>
    /// WebRTC DataChannelのエンベロープ形式でメッセージをラップ
    /// </summary>
    public async Task<byte[]> WrapForDataChannelAsync(string plaintext, CancellationToken ct = default)
    {
        if (!IsEncrypted)
            return Encoding.UTF8.GetBytes(plaintext);

        var encrypted = await EncryptAsync(plaintext, ct);

        // Add header: version(1) + flags(1) + length(4) + encrypted_data
        var wrapped = new byte[6 + encrypted.Length];
        wrapped[0] = 1; // version
        wrapped[1] = 0x01; // flag: encrypted
        BinaryPrimitives.WriteInt32BigEndian(wrapped.AsSpan(2, 4), encrypted.Length);
        Buffer.BlockCopy(encrypted, 0, wrapped, 6, encrypted.Length);

        return wrapped;
    }

    /// <summary>
    /// WebRTC DataChannelのエンベロープ形式からメッセージをアンラップ
    /// </summary>
    public async Task<string> UnwrapFromDataChannelAsync(byte[] data, CancellationToken ct = default)
    {
        if (data.Length < 6)
            return Encoding.UTF8.GetString(data);

        var version = data[0];
        var flags = data[1];
        var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(2, 4));

        if (version != 1)
            throw new NotSupportedException($"Unsupported envelope version: {version}");

        if ((flags & 0x01) == 0)
        {
            // Not encrypted
            return Encoding.UTF8.GetString(data, 6, data.Length - 6);
        }

        // Encrypted
        var encrypted = new byte[length];
        Buffer.BlockCopy(data, 6, encrypted, 0, length);

        return await DecryptAsync(encrypted, ct);
    }
}
