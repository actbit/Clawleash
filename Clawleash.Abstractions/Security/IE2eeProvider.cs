namespace Clawleash.Abstractions.Security;

/// <summary>
/// E2EE（エンドツーエンド暗号化）プロバイダーのインターフェース
/// </summary>
public interface IE2eeProvider
{
    /// <summary>
    /// 暗号化されたメッセージを送信
    /// </summary>
    /// <param name="plaintext">平文</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>暗号化されたデータ</returns>
    Task<byte[]> EncryptAsync(string plaintext, CancellationToken ct = default);

    /// <summary>
    /// 暗号化されたメッセージを受信して復号化
    /// </summary>
    /// <param name="ciphertext">暗号化されたデータ</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>復号化された平文</returns>
    Task<string> DecryptAsync(byte[] ciphertext, CancellationToken ct = default);

    /// <summary>
    /// 鍵交換を開始
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>鍵交換結果</returns>
    Task<KeyExchangeResult> StartKeyExchangeAsync(CancellationToken ct = default);

    /// <summary>
    /// 鍵交換を完了
    /// </summary>
    /// <param name="result">相手からの鍵交換結果</param>
    /// <param name="ct">キャンセルトークン</param>
    Task CompleteKeyExchangeAsync(KeyExchangeResult result, CancellationToken ct = default);

    /// <summary>
    /// 現在のセッションが暗号化されているか
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// セッションID
    /// </summary>
    string? SessionId { get; }
}

/// <summary>
/// 鍵交換の結果
/// </summary>
public class KeyExchangeResult
{
    /// <summary>
    /// 公開鍵
    /// </summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// セッションID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 追加のメタデータ
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
