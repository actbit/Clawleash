using System.ComponentModel;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// ブラウザアクションプラグイン
/// スクロール、待機、キーボード操作などの高度なブラウザ操作を提供
/// </summary>
public class BrowserActionsPlugin
{
    private readonly IBrowserManager _browserManager;

    public BrowserActionsPlugin(IBrowserManager browserManager)
    {
        _browserManager = browserManager ?? throw new ArgumentNullException(nameof(browserManager));
    }

    #region スクロール操作

    [KernelFunction, Description("ページを指定したピクセル数だけスクロールします")]
    public async Task<string> ScrollPage(
        [Description("スクロール量（ピクセル・正の値で下、負の値で上）")] int pixels = 500)
    {
        try
        {
            await _browserManager.ScrollAsync(pixels);
            return $"成功: {pixels}ピクセルスクロールしました";
        }
        catch (Exception ex)
        {
            return $"エラー: スクロールに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページの最下部までスクロールします")]
    public async Task<string> ScrollToBottom()
    {
        try
        {
            await _browserManager.ScrollToBottomAsync();
            return "成功: ページの最下部までスクロールしました";
        }
        catch (Exception ex)
        {
            return $"エラー: スクロールに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region 待機操作

    [KernelFunction, Description("指定した時間待機します")]
    public async Task<string> WaitForTime(
        [Description("待機時間（ミリ秒）")] int milliseconds = 1000)
    {
        try
        {
            await _browserManager.WaitForTimeoutAsync(milliseconds);
            return $"成功: {milliseconds}ミリ秒待機しました";
        }
        catch (Exception ex)
        {
            return $"エラー: 待機に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("指定したセレクタの要素が表示されるまで待機します")]
    public async Task<string> WaitForElement(
        [Description("CSSセレクタ")] string selector,
        [Description("タイムアウト（ミリ秒・デフォルト: 10000）")] int timeoutMs = 10000)
    {
        try
        {
            await _browserManager.WaitForSelectorAsync(selector, timeoutMs);
            return $"成功: セレクタ '{selector}' の要素が表示されました";
        }
        catch (TimeoutException)
        {
            return $"エラー: タイムアウト - セレクタ '{selector}' の要素が見つかりませんでした";
        }
        catch (Exception ex)
        {
            return $"エラー: 要素の待機に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページの読み込みが完了するまで待機します")]
    public async Task<string> WaitForPageLoad()
    {
        try
        {
            await _browserManager.WaitForLoadStateAsync();
            return "成功: ページの読み込みが完了しました";
        }
        catch (Exception ex)
        {
            return $"エラー: ページ読み込み待機に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region キーボード操作

    [KernelFunction, Description("キーボードのキーを押します")]
    public async Task<string> PressKey(
        [Description("押すキー（Enter, Tab, Escape, ArrowUp, ArrowDown, ArrowLeft, ArrowRight, Backspace, Delete, Home, End, PageUp, PageDown など）")] string key)
    {
        try
        {
            await _browserManager.PressKeyAsync(key);
            return $"成功: '{key}' キーを押しました";
        }
        catch (Exception ex)
        {
            return $"エラー: キー入力に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("キーボードでテキストを入力します（現在フォーカスされている要素へ）")]
    public async Task<string> TypeWithKeyboard(
        [Description("入力するテキスト")] string text)
    {
        try
        {
            await _browserManager.KeyboardTypeAsync(text);
            return $"成功: テキストを入力しました: {text}";
        }
        catch (Exception ex)
        {
            return $"エラー: テキスト入力に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region ナビゲーション操作

    [KernelFunction, Description("ページをリロードします")]
    public async Task<string> ReloadPage()
    {
        try
        {
            await _browserManager.ReloadAsync();
            return "成功: ページをリロードしました";
        }
        catch (Exception ex)
        {
            return $"エラー: リロードに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ブラウザの履歴で前のページに戻ります")]
    public async Task<string> GoBack()
    {
        try
        {
            await _browserManager.GoBackAsync();
            return "成功: 前のページに戻りました";
        }
        catch (Exception ex)
        {
            return $"エラー: 戻る操作に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ブラウザの履歴で次のページに進みます")]
    public async Task<string> GoForward()
    {
        try
        {
            await _browserManager.GoForwardAsync();
            return "成功: 次のページに進みました";
        }
        catch (Exception ex)
        {
            return $"エラー: 進む操作に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region その他の操作

    [KernelFunction, Description("要素にホバー（マウスオーバー）します")]
    public async Task<string> HoverElement(
        [Description("CSSセレクタ")] string selector)
    {
        try
        {
            await _browserManager.HoverAsync(selector);
            return $"成功: セレクタ '{selector}' の要素にホバーしました";
        }
        catch (Exception ex)
        {
            return $"エラー: ホバー操作に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルをアップロードします")]
    public async Task<string> UploadFile(
        [Description("ファイル入力のCSSセレクタ")] string selector,
        [Description("アップロードするファイルのパス")] string filePath)
    {
        try
        {
            await _browserManager.UploadFileAsync(selector, filePath);
            return $"成功: ファイル '{filePath}' をアップロードしました";
        }
        catch (Exception ex)
        {
            return $"エラー: ファイルアップロードに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("複数のアクションを順番に実行します（Firecrawl風のActions機能）")]
    public async Task<string> ExecuteActions(
        [Description("アクションのJSON配列。各アクションは {\"type\": \"click|type|scroll|wait|press\", \"selector\": \"...\", \"text\": \"...\", \"pixels\": 500, \"milliseconds\": 1000, \"key\": \"Enter\"} の形式")] string actionsJson)
    {
        try
        {
            var actions = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(actionsJson);
            if (actions == null || actions.Count == 0)
            {
                return "エラー: アクションリストが空です";
            }

            var results = new List<string>();

            foreach (var action in actions)
            {
                var type = action.GetValueOrDefault("type")?.ToString()?.ToLowerInvariant();

                switch (type)
                {
                    case "click":
                        var clickSelector = action.GetValueOrDefault("selector")?.ToString();
                        if (!string.IsNullOrEmpty(clickSelector))
                        {
                            await _browserManager.ClickAsync(clickSelector);
                            results.Add($"クリック: {clickSelector}");
                        }
                        break;

                    case "type":
                        var typeSelector = action.GetValueOrDefault("selector")?.ToString();
                        var typeText = action.GetValueOrDefault("text")?.ToString();
                        if (!string.IsNullOrEmpty(typeSelector) && !string.IsNullOrEmpty(typeText))
                        {
                            await _browserManager.TypeAsync(typeSelector, typeText);
                            results.Add($"入力: {typeSelector} <- {typeText}");
                        }
                        break;

                    case "scroll":
                        var scrollPixels = action.GetValueOrDefault("pixels") as int? ?? 500;
                        await _browserManager.ScrollAsync(scrollPixels);
                        results.Add($"スクロール: {scrollPixels}px");
                        break;

                    case "scrolltobottom":
                        await _browserManager.ScrollToBottomAsync();
                        results.Add("スクロール: 最下部へ");
                        break;

                    case "wait":
                        var waitMs = action.GetValueOrDefault("milliseconds") as int? ?? 1000;
                        await _browserManager.WaitForTimeoutAsync(waitMs);
                        results.Add($"待機: {waitMs}ms");
                        break;

                    case "waitfor":
                        var waitSelector = action.GetValueOrDefault("selector")?.ToString();
                        var waitTimeout = action.GetValueOrDefault("timeout") as int? ?? 10000;
                        if (!string.IsNullOrEmpty(waitSelector))
                        {
                            await _browserManager.WaitForSelectorAsync(waitSelector, waitTimeout);
                            results.Add($"待機: {waitSelector} が表示されるまで");
                        }
                        break;

                    case "press":
                        var pressKey = action.GetValueOrDefault("key")?.ToString();
                        if (!string.IsNullOrEmpty(pressKey))
                        {
                            await _browserManager.PressKeyAsync(pressKey);
                            results.Add($"キー入力: {pressKey}");
                        }
                        break;

                    case "hover":
                        var hoverSelector = action.GetValueOrDefault("selector")?.ToString();
                        if (!string.IsNullOrEmpty(hoverSelector))
                        {
                            await _browserManager.HoverAsync(hoverSelector);
                            results.Add($"ホバー: {hoverSelector}");
                        }
                        break;

                    case "navigate":
                        var navUrl = action.GetValueOrDefault("url")?.ToString();
                        if (!string.IsNullOrEmpty(navUrl))
                        {
                            await _browserManager.NavigateAsync(navUrl);
                            results.Add($"移動: {navUrl}");
                        }
                        break;

                    case "reload":
                        await _browserManager.ReloadAsync();
                        results.Add("リロード");
                        break;

                    default:
                        results.Add($"不明なアクション: {type}");
                        break;
                }

                // アクション間の短い待機
                await Task.Delay(300);
            }

            return $"成功: {results.Count} 個のアクションを実行しました\n" + string.Join("\n", results);
        }
        catch (System.Text.Json.JsonException)
        {
            return "エラー: アクションリストのJSON形式が無効です";
        }
        catch (Exception ex)
        {
            return $"エラー: アクションの実行に失敗しました: {ex.Message}";
        }
    }

    #endregion
}
