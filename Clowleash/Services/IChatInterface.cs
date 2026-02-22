namespace Clowleash.Services;

/// <summary>
/// チャットインターフェースの抽象化
/// CLI、Discord、Slackなど様々な入力ソースに対応するためのインターフェース
/// </summary>
public interface IChatInterface : IAsyncDisposable
{
    /// <summary>
    /// インターフェース名（CLI、Discord、Slackなど）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// インターフェースが接続されているかどうか
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// インターフェースを開始する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// メッセージを受信した時に発生するイベント
    /// </summary>
    event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// メッセージを送信する
    /// </summary>
    /// <param name="message">送信するメッセージ</param>
    /// <param name="replyToMessageId">返信先のメッセージID（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SendMessageAsync(string message, string? replyToMessageId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ストリーミングメッセージを開始する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ストリーミングライター</returns>
    IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default);
}

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
}

/// <summary>
/// ストリーミングメッセージライターのインターフェース
/// </summary>
public interface IStreamingMessageWriter : IAsyncDisposable
{
    /// <summary>
    /// メッセージID
    /// </summary>
    string MessageId { get; }

    /// <summary>
    /// テキストを追加する
    /// </summary>
    /// <param name="text">追加するテキスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task AppendTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// メッセージを完了する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
