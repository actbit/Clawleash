namespace Clawleash.Abstractions.Services;

/// <summary>
/// チャットインターフェースの抽象化
/// CLI、Discord、Slack、WebSocket、WebRTCなど様々な入力ソースに対応するためのインターフェース
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
    /// インターフェースを停止する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StopAsync(CancellationToken cancellationToken = default);

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
