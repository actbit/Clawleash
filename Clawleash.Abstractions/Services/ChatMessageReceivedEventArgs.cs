namespace Clawleash.Abstractions.Services;

/// <summary>
/// メッセージ受信イベントの引数
/// </summary>
public class ChatMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// メッセージID
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// 送信者ID
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 送信者名
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// メッセージ内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// チャンネルID
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 返信先メッセージID
    /// </summary>
    public string? ReplyToMessageId { get; set; }

    /// <summary>
    /// 返信が必要かどうか
    /// </summary>
    public bool RequiresReply { get; set; } = true;

    /// <summary>
    /// インターフェース名（メッセージの送信元）
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// 追加のメタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
