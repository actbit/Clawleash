using Clowleash.Models;

namespace Clowleash.Services;

/// <summary>
/// ブラウザ管理のインターフェース
/// Playwrightを使用したブラウザ操作を抽象化
/// </summary>
public interface IBrowserManager : IAsyncDisposable
{
    /// <summary>
    /// ブラウザが初期化されているかどうか
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 指定されたURLに移動する
    /// </summary>
    /// <param name="url">移動先URL</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<BrowserState> NavigateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のブラウザ状態を取得する（OpenClow風）
    /// スクリーンショット、HTML、アクセシビリティツリーを含む
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<BrowserState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 要素をクリックする
    /// </summary>
    /// <param name="selector">CSSセレクタ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ClickAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// テキストを入力する
    /// </summary>
    /// <param name="selector">CSSセレクタ</param>
    /// <param name="text">入力テキスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task TypeAsync(string selector, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のページのテキストを取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<string> GetTextContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// JavaScriptを実行する
    /// </summary>
    /// <param name="script">JavaScriptコード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<object?> EvaluateJavaScriptAsync(string script, CancellationToken cancellationToken = default);

    /// <summary>
    /// スクリーンショットを撮影する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<byte[]> TakeScreenshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のURLを取得する
    /// </summary>
    Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken = default);
}
