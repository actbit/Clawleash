using Clawleash.Models;

namespace Clawleash.Services;

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

    #region 基本操作

    /// <summary>
    /// 指定されたURLに移動する
    /// </summary>
    Task<BrowserState> NavigateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のブラウザ状態を取得する（OpenClow風）
    /// </summary>
    Task<BrowserState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 要素をクリックする
    /// </summary>
    Task ClickAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// テキストを入力する
    /// </summary>
    Task TypeAsync(string selector, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のページのテキストを取得する
    /// </summary>
    Task<string> GetTextContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// JavaScriptを実行する
    /// </summary>
    Task<object?> EvaluateJavaScriptAsync(string script, CancellationToken cancellationToken = default);

    /// <summary>
    /// スクリーンショットを撮影する
    /// </summary>
    Task<byte[]> TakeScreenshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のURLを取得する
    /// </summary>
    Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken = default);

    #endregion

    #region 拡張ブラウザ操作

    /// <summary>
    /// ページをスクロールする
    /// </summary>
    Task ScrollAsync(int pixels, CancellationToken cancellationToken = default);

    /// <summary>
    /// ページの最下部までスクロールする
    /// </summary>
    Task ScrollToBottomAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 要素が表示されるまで待機する
    /// </summary>
    Task WaitForSelectorAsync(string selector, int timeoutMs = 10000, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定時間待機する
    /// </summary>
    Task WaitForTimeoutAsync(int milliseconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// ページの読み込み完了まで待機する
    /// </summary>
    Task WaitForLoadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// キーボードのキーを押す
    /// </summary>
    Task PressKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーボードでテキストを入力する（フォーカスなし）
    /// </summary>
    Task KeyboardTypeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// ページをリロードする
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 前のページに戻る
    /// </summary>
    Task GoBackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 次のページに進む
    /// </summary>
    Task GoForwardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ホバー操作を行う
    /// </summary>
    Task HoverAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// ファイルをアップロードする
    /// </summary>
    Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// ページ内のすべてのリンクを取得する
    /// </summary>
    Task<List<string>> GetLinksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ページのメタデータを取得する
    /// </summary>
    Task<PageMetadata> GetMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ページのHTMLをMarkdownに変換して取得する
    /// </summary>
    Task<string> GetMarkdownAsync(CancellationToken cancellationToken = default);

    #endregion

    #region クロール・スクレイプ操作

    /// <summary>
    /// URLをスクレイプしてMarkdown/HTML/リンクを取得する
    /// </summary>
    Task<ScrapeResult> ScrapeAsync(string url, bool includeScreenshot = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Webサイトをクロールしてすべてのページを取得する
    /// </summary>
    Task<CrawlResult> CrawlAsync(string startUrl, int maxPages = 10, int maxDepth = 2, CancellationToken cancellationToken = default);

    /// <summary>
    /// Webサイトのサイトマップ（URL一覧）を取得する
    /// </summary>
    Task<MapResult> MapAsync(string url, string? searchQuery = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Web検索を実行する
    /// </summary>
    Task<SearchResult> SearchAsync(string query, int limit = 5, bool scrapeContent = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のURLを一括スクレイプする
    /// </summary>
    Task<List<ScrapeResult>> BatchScrapeAsync(List<string> urls, CancellationToken cancellationToken = default);

    #endregion
}
