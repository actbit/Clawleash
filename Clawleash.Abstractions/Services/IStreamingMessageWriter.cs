namespace Clawleash.Abstractions.Services;

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
