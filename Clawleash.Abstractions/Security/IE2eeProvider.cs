namespace Clawleash.Abstractions.Security;

/// <summary>
/// E2EE（エンドツーエンド暗号化）プロバイダーのインターフェース
/// チャンネルごとの共通鍵をサポート
/// </summary>
public interface IE2eeProvider
{
    /// <summary>
    /// セッション鍵を使用して暗号化
    /// </summary>
    /// <param name="plaintext">平文</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>暗号化されたデータ</returns>
    Task<byte[]> EncryptAsync(string plaintext, CancellationToken ct = default);

    /// <summary>
    /// チャンネルを指定して暗号化
    /// </summary>
    /// <param name="plaintext">平文</param>
    /// <param name="channelId">チャンネルID（nullの場合はセッション鍵を使用）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>暗号化されたデータ</returns>
    Task<byte[]> EncryptAsync(string plaintext, string? channelId, CancellationToken ct = default);

    /// <summary>
    /// セッション鍵を使用して復号化
    /// </summary>
    /// <param name="ciphertext">暗号化されたデータ</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>復号化された平文</returns>
    Task<string> DecryptAsync(byte[] ciphertext, CancellationToken ct = default);

    /// <summary>
    /// チャンネルを指定して復号化
    /// </summary>
    /// <param name="ciphertext">暗号化されたデータ</param>
    /// <param name="channelId">チャンネルID（nullの場合はセッション鍵を使用）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>復号化された平文</returns>
    Task<string> DecryptAsync(byte[] ciphertext, string? channelId, CancellationToken ct = default);

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
    /// 暗号化されたチャンネル鍵を設定
    /// </summary>
    /// <param name="channelId">チャンネルID</param>
    /// <param name="encryptedKey">セッション鍵で暗号化されたチャンネル鍵</param>
    void SetChannelKey(string channelId, byte[] encryptedKey);

    /// <summary>
    /// 平文のチャンネル鍵を設定（E2EE無効時用）
    /// </summary>
    /// <param name="channelId">チャンネルID</param>
    /// <param name="plainKey">平文のチャンネル鍵</param>
    void SetPlainChannelKey(string channelId, byte[] plainKey);

    /// <summary>
    /// チャンネル鍵が存在するか確認
    /// </summary>
    /// <param name="channelId">チャンネルID</param>
    /// <returns>チャンネル鍵が存在すればtrue</returns>
    bool HasChannelKey(string channelId);

    /// <summary>
    /// 現在のセッションが暗号化されているか
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// セッションID
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// セッションをリセット
    /// </summary>
    void Reset();

    /// <summary>
    /// 公開鍵を取得
    /// </summary>
    /// <returns>公開鍵</returns>
    byte[] GetPublicKey();
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
