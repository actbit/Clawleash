namespace Clawleash.Services.Handlers;

/// <summary>
/// GUI承認ハンドラーを識別するマーカーインターフェース
/// 将来的な拡張用：WPF、WinForms、Blazorなどで実装可能
/// </summary>
public interface IGuiApprovalHandler : IApprovalHandler
{
    /// <summary>
    /// 承認ダイアログのタイトル
    /// </summary>
    string? DialogTitle { get; set; }

    /// <summary>
    /// 非同期ダイアログ表示をサポートするかどうか
    /// </summary>
    bool SupportsAsyncDialog { get; }

    /// <summary>
    /// ダイアログを表示中かどうか
    /// </summary>
    bool IsDialogOpen { get; }

    /// <summary>
    /// 現在のダイアログを閉じる
    /// </summary>
    void CloseDialog();
}

/// <summary>
/// Web承認ハンドラーを識別するマーカーインターフェース
/// 将来的な拡張用：SignalR、WebSocket、REST APIなどで実装可能
/// </summary>
public interface IWebApprovalHandler : IApprovalHandler
{
    /// <summary>
    /// 保留中の承認リクエストの数
    /// </summary>
    int PendingRequestCount { get; }

    /// <summary>
    /// 承認レスポンスを受信したときのイベント
    /// </summary>
    event EventHandler<WebApprovalResponseEventArgs>? ResponseReceived;

    /// <summary>
    /// 保留中のリクエスト一覧を取得
    /// </summary>
    IReadOnlyList<ApprovalRequest> GetPendingRequests();

    /// <summary>
    /// 外部から承認レスポンスを送信（Web APIから呼び出し用）
    /// </summary>
    /// <param name="requestId">リクエストID</param>
    /// <param name="result">承認結果</param>
    /// <returns>処理に成功したかどうか</returns>
    bool SubmitResponse(string requestId, ApprovalResult result);
}

/// <summary>
/// Web承認レスポンスのイベント引数
/// </summary>
public class WebApprovalResponseEventArgs : EventArgs
{
    public string RequestId { get; set; } = string.Empty;
    public ApprovalResult Result { get; set; } = new();
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
