using Clawleash.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebSocket.Security;

/// <summary>
/// WebSocket用E2EE暗号化プロバイダー
/// 共通実装を継承
/// </summary>
public class AesGcmE2eeProvider : Abstractions.Security.AesGcmE2eeProvider
{
    /// <summary>
    /// 新しいインスタンスを作成
    /// </summary>
    public AesGcmE2eeProvider(ILogger<AesGcmE2eeProvider>? logger = null)
        : base(logger)
    {
    }

    /// <summary>
    /// 既存の秘密鍵から初期化
    /// </summary>
    public AesGcmE2eeProvider(byte[] privateKey, ILogger<AesGcmE2eeProvider>? logger = null)
        : base(privateKey, logger)
    {
    }
}
