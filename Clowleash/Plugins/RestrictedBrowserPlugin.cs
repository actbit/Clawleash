using System.ComponentModel;
using Clowleash.Models;
using Clowleash.Services;
using Microsoft.SemanticKernel;

namespace Clowleash.Plugins;

/// <summary>
/// 制限付きブラウザ操作プラグイン
/// URLフィルタリングを適用して安全にブラウザを操作
/// </summary>
public class RestrictedBrowserPlugin
{
    private readonly IBrowserManager _browserManager;

    public RestrictedBrowserPlugin(IBrowserManager browserManager)
    {
        _browserManager = browserManager ?? throw new ArgumentNullException(nameof(browserManager));
    }

    [KernelFunction, Description("指定されたURLに移動し、ページの状態を取得します")]
    public async Task<string> NavigateTo(
        [Description("移動先のURL")] string url)
    {
        try
        {
            var state = await _browserManager.NavigateAsync(url);

            return $"""
                ページに移動しました:
                URL: {state.Url}
                タイトル: {state.Title}

                ページテキスト（最初の1000文字）:
                {TruncateText(await GetPageText(), 1000)}
                """;
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"エラー: ページへの移動に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のブラウザ画面の状態（スクリーンショット+DOM構造）を取得します（OpenClow風）")]
    public async Task<string> GetBrowserState()
    {
        try
        {
            var state = await _browserManager.GetStateAsync();

            return $"""
                現在のブラウザ状態:
                URL: {state.Url}
                タイトル: {state.Title}
                タイムスタンプ: {state.Timestamp:O}

                HTML内容（最初の2000文字）:
                {TruncateText(state.HtmlContent, 2000)}

                アクセシビリティツリー:
                {TruncateText(state.AccessibilityTree ?? "利用不可", 1000)}

                スクリーンショット: {(string.IsNullOrEmpty(state.ScreenshotBase64) ? "なし" : $"{state.ScreenshotBase64.Length} バイト (base64)")}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: ブラウザ状態の取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページ上の要素をクリックします")]
    public async Task<string> ClickElement(
        [Description("クリックする要素のCSSセレクタ")] string selector)
    {
        try
        {
            await _browserManager.ClickAsync(selector);
            return $"成功: セレクタ '{selector}' の要素をクリックしました";
        }
        catch (Exception ex)
        {
            return $"エラー: 要素のクリックに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("入力フィールドにテキストを入力します")]
    public async Task<string> TypeText(
        [Description("入力する要素のCSSセレクタ")] string selector,
        [Description("入力するテキスト")] string text)
    {
        try
        {
            await _browserManager.TypeAsync(selector, text);
            return $"成功: セレクタ '{selector}' にテキストを入力しました";
        }
        catch (Exception ex)
        {
            return $"エラー: テキストの入力に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のページのテキストコンテンツを取得します")]
    public async Task<string> GetPageText()
    {
        try
        {
            var text = await _browserManager.GetTextContentAsync();
            return text;
        }
        catch (Exception ex)
        {
            return $"エラー: ページテキストの取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページ内でJavaScriptを実行します")]
    public async Task<string> ExecuteJavaScript(
        [Description("実行するJavaScriptコード")] string script)
    {
        // セキュリティチェック: 危険なパターンを制限
        var dangerousPatterns = new[]
        {
            "eval(", "Function(", "setTimeout(", "setInterval(",
            "XMLHttpRequest", "fetch(", "navigator.",
            "localStorage", "sessionStorage", "cookie"
        };

        if (dangerousPatterns.Any(p => script.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return "エラー: セキュリティ上の理由でこのJavaScriptの実行は許可されていません";
        }

        try
        {
            var result = await _browserManager.EvaluateJavaScriptAsync(script);
            return result?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"エラー: JavaScriptの実行に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("スクリーンショットを撮影します")]
    public async Task<string> TakeScreenshot()
    {
        try
        {
            var screenshot = await _browserManager.TakeScreenshotAsync();
            var base64 = Convert.ToBase64String(screenshot);
            return $"成功: スクリーンショットを撮影しました ({screenshot.Length} バイト, base64: {base64.Length} 文字)";
        }
        catch (Exception ex)
        {
            return $"エラー: スクリーンショットの撮影に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のURLを取得します")]
    public async Task<string> GetCurrentUrl()
    {
        try
        {
            var url = await _browserManager.GetCurrentUrlAsync();
            return url;
        }
        catch (Exception ex)
        {
            return $"エラー: URLの取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページ内の要素を検索します")]
    public async Task<string> FindElements(
        [Description("検索する要素のCSSセレクタ")] string selector)
    {
        try
        {
            var script = $$"""
                Array.from(document.querySelectorAll('{{selector}}')).map((el, i) => ({
                    index: i,
                    tagName: el.tagName,
                    id: el.id,
                    className: el.className,
                    text: el.textContent?.substring(0, 100)
                }))
                """;

            var result = await _browserManager.EvaluateJavaScriptAsync(script);
            return result?.ToString() ?? "要素が見つかりません";
        }
        catch (Exception ex)
        {
            return $"エラー: 要素の検索に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("フォームを送信します")]
    public async Task<string> SubmitForm(
        [Description("フォームのCSSセレクタ")] string selector)
    {
        try
        {
            var script = $"document.querySelector('{selector}').submit()";
            await _browserManager.EvaluateJavaScriptAsync(script);
            return $"成功: フォーム '{selector}' を送信しました";
        }
        catch (Exception ex)
        {
            return $"エラー: フォームの送信に失敗しました: {ex.Message}";
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
