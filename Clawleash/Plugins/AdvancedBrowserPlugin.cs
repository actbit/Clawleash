using System.ComponentModel;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// 高度なブラウザ操作プラグイン
/// タブ管理、Cookie、ローカルストレージ、iframe、ダイアログ処理など
/// </summary>
public class AdvancedBrowserPlugin
{
    private readonly IBrowserManager _browserManager;

    public AdvancedBrowserPlugin(IBrowserManager browserManager)
    {
        _browserManager = browserManager ?? throw new ArgumentNullException(nameof(browserManager));
    }

    #region Cookie・ストレージ操作

    [KernelFunction, Description("現在のページのCookieを取得します")]
    public async Task<string> GetCookies()
    {
        try
        {
            var cookies = await _browserManager.EvaluateJavaScriptAsync(@"() => {
                return document.cookie;
            }");
            return $"Cookie: {cookies ?? "なし"}";
        }
        catch (Exception ex)
        {
            return $"エラー: Cookieの取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ローカルストレージの値を取得します")]
    public async Task<string> GetLocalStorage(
        [Description("取得するキー（省略時は全て取得）")] string? key = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(key))
            {
                var value = await _browserManager.EvaluateJavaScriptAsync($"localStorage.getItem('{key}')");
                return $"localStorage['{key}']: {value ?? "null"}";
            }
            else
            {
                var allItems = await _browserManager.EvaluateJavaScriptAsync(@"() => {
                    const items = {};
                    for (let i = 0; i < localStorage.length; i++) {
                        const key = localStorage.key(i);
                        items[key] = localStorage.getItem(key);
                    }
                    return JSON.stringify(items);
                }");
                return $"LocalStorage:\n{allItems}";
            }
        }
        catch (Exception ex)
        {
            return $"エラー: LocalStorageの取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ローカルストレージに値を設定します")]
    public async Task<string> SetLocalStorage(
        [Description("キー")] string key,
        [Description("値")] string value)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($"localStorage.setItem('{key}', '{value.Replace("'", "\\'")}')");
            return $"成功: localStorage['{key}'] = '{value}'";
        }
        catch (Exception ex)
        {
            return $"エラー: LocalStorageの設定に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("セッションストレージの値を取得します")]
    public async Task<string> GetSessionStorage(
        [Description("取得するキー（省略時は全て取得）")] string? key = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(key))
            {
                var value = await _browserManager.EvaluateJavaScriptAsync($"sessionStorage.getItem('{key}')");
                return $"sessionStorage['{key}']: {value ?? "null"}";
            }
            else
            {
                var allItems = await _browserManager.EvaluateJavaScriptAsync(@"() => {
                    const items = {};
                    for (let i = 0; i < sessionStorage.length; i++) {
                        const key = sessionStorage.key(i);
                        items[key] = sessionStorage.getItem(key);
                    }
                    return JSON.stringify(items);
                }");
                return $"SessionStorage:\n{allItems}";
            }
        }
        catch (Exception ex)
        {
            return $"エラー: SessionStorageの取得に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region フォーム操作

    [KernelFunction, Description("セレクトボックス（ドロップダウン）のオプションを選択します")]
    public async Task<string> SelectOption(
        [Description("セレクトボックスのCSSセレクタ")] string selector,
        [Description("選択する値（value）")] string value)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($"document.querySelector('{selector}').value = '{value}'");
            return $"成功: セレクトボックス '{selector}' で '{value}' を選択しました";
        }
        catch (Exception ex)
        {
            return $"エラー: セレクトボックスの操作に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("チェックボックスまたはラジオボタンをクリックします")]
    public async Task<string> CheckElement(
        [Description("チェックボックス/ラジオボタンのCSSセレクタ")] string selector,
        [Description("チェックする場合はtrue、チェックを外す場合はfalse")] bool check = true)
    {
        try
        {
            var element = await _browserManager.EvaluateJavaScriptAsync($"document.querySelector('{selector}')");
            if (element == null)
            {
                return $"エラー: 要素 '{selector}' が見つかりません";
            }

            var script = $"document.querySelector('{selector}').checked = {check.ToString().ToLower()}";
            await _browserManager.EvaluateJavaScriptAsync(script);
            return $"成功: '{selector}' を{(check ? "チェック" : "チェック解除")}しました";
        }
        catch (Exception ex)
        {
            return $"エラー: チェックボックスの操作に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("複数行テキストエリアにテキストを入力します")]
    public async Task<string> FillTextArea(
        [Description("テキストエリアのCSSセレクタ")] string selector,
        [Description("入力するテキスト")] string text)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($"document.querySelector('{selector}').value = `{text.Replace("`", "\\`")}`");
            return $"成功: テキストエリア '{selector}' に入力しました";
        }
        catch (Exception ex)
        {
            return $"エラー: テキストエリアの入力に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region マウス操作

    [KernelFunction, Description("要素をダブルクリックします")]
    public async Task<string> DoubleClick(
        [Description("CSSセレクタ")] string selector)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const el = document.querySelector('{selector}');
                if (el) {{
                    const event = new MouseEvent('dblclick', {{
                        bubbles: true,
                        cancelable: true,
                        view: window
                    }});
                    el.dispatchEvent(event);
                }}
            }}");
            return $"成功: '{selector}' をダブルクリックしました";
        }
        catch (Exception ex)
        {
            return $"エラー: ダブルクリックに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("要素を右クリックします（コンテキストメニューを開く）")]
    public async Task<string> RightClick(
        [Description("CSSセレクタ")] string selector)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const el = document.querySelector('{selector}');
                if (el) {{
                    const event = new MouseEvent('contextmenu', {{
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        button: 2
                    }});
                    el.dispatchEvent(event);
                }}
            }}");
            return $"成功: '{selector}' を右クリックしました";
        }
        catch (Exception ex)
        {
            return $"エラー: 右クリックに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ドラッグアンドドロップ操作を実行します")]
    public async Task<string> DragAndDrop(
        [Description("ドラッグ元のCSSセレクタ")] string sourceSelector,
        [Description("ドロップ先のCSSセレクタ")] string targetSelector)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const source = document.querySelector('{sourceSelector}');
                const target = document.querySelector('{targetSelector}');
                if (!source || !target) return '要素が見つかりません';

                const dragStart = new DragEvent('dragstart', {{ bubbles: true, dataTransfer: new DataTransfer() }});
                const drop = new DragEvent('drop', {{ bubbles: true, dataTransfer: new DataTransfer() }});
                const dragEnd = new DragEvent('dragend', {{ bubbles: true }});

                source.dispatchEvent(dragStart);
                target.dispatchEvent(drop);
                source.dispatchEvent(dragEnd);
            }}");
            return $"成功: '{sourceSelector}' を '{targetSelector}' にドラッグ＆ドロップしました";
        }
        catch (Exception ex)
        {
            return $"エラー: ドラッグ＆ドロップに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region テキスト選択・クリップボード

    [KernelFunction, Description("ページ内のテキストを選択します")]
    public async Task<string> SelectText(
        [Description("選択を開始するCSSセレクタ")] string startSelector,
        [Description("選択を終了するCSSセレクタ（省略時は要素全体）")] string? endSelector = null)
    {
        try
        {
            if (string.IsNullOrEmpty(endSelector))
            {
                await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                    const el = document.querySelector('{startSelector}');
                    if (el) {{
                        const range = document.createRange();
                        range.selectNodeContents(el);
                        const selection = window.getSelection();
                        selection.removeAllRanges();
                        selection.addRange(range);
                    }}
                }}");
            }
            else
            {
                await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                    const start = document.querySelector('{startSelector}');
                    const end = document.querySelector('{endSelector}');
                    if (start && end) {{
                        const range = document.createRange();
                        range.setStartBefore(start);
                        range.setEndAfter(end);
                        const selection = window.getSelection();
                        selection.removeAllRanges();
                        selection.addRange(range);
                    }}
                }}");
            }
            return "成功: テキストを選択しました";
        }
        catch (Exception ex)
        {
            return $"エラー: テキスト選択に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("選択したテキストをコピーします（Ctrl+C）")]
    public async Task<string> CopySelection()
    {
        try
        {
            await _browserManager.PressKeyAsync("Control+c");
            return "成功: 選択範囲をコピーしました";
        }
        catch (Exception ex)
        {
            return $"エラー: コピーに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("クリップボードからペーストします（Ctrl+V）")]
    public async Task<string> Paste()
    {
        try
        {
            await _browserManager.PressKeyAsync("Control+v");
            return "成功: ペーストしました";
        }
        catch (Exception ex)
        {
            return $"エラー: ペーストに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region iframe操作

    [KernelFunction, Description("iframe内のコンテンツを取得します")]
    public async Task<string> GetIframeContent(
        [Description("iframeのCSSセレクタ")] string selector)
    {
        try
        {
            var content = await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const iframe = document.querySelector('{selector}');
                if (iframe && iframe.contentDocument) {{
                    return iframe.contentDocument.body.innerHTML;
                }}
                return null;
            }}");
            return content?.ToString() ?? "iframeコンテンツを取得できません";
        }
        catch (Exception ex)
        {
            return $"エラー: iframeコンテンツの取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("iframe内の要素をクリックします")]
    public async Task<string> ClickInIframe(
        [Description("iframeのCSSセレクタ")] string iframeSelector,
        [Description("iframe内の要素のCSSセレクタ")] string elementSelector)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const iframe = document.querySelector('{iframeSelector}');
                if (iframe && iframe.contentDocument) {{
                    const el = iframe.contentDocument.querySelector('{elementSelector}');
                    if (el) el.click();
                }}
            }}");
            return $"成功: iframe内の '{elementSelector}' をクリックしました";
        }
        catch (Exception ex)
        {
            return $"エラー: iframe内要素のクリックに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region ページ情報取得

    [KernelFunction, Description("ページのスクロール位置を取得します")]
    public async Task<string> GetScrollPosition()
    {
        try
        {
            var position = await _browserManager.EvaluateJavaScriptAsync(@"() => {
                return JSON.stringify({
                    scrollX: window.scrollX,
                    scrollY: window.scrollY,
                    scrollHeight: document.documentElement.scrollHeight,
                    clientHeight: document.documentElement.clientHeight
                });
            }");
            return $"スクロール位置: {position}";
        }
        catch (Exception ex)
        {
            return $"エラー: スクロール位置の取得に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("特定位置までスクロールします")]
    public async Task<string> ScrollToPosition(
        [Description("X座標")] int x = 0,
        [Description("Y座標")] int y = 0)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($"window.scrollTo({x}, {y})");
            return $"成功: ({x}, {y}) にスクロールしました";
        }
        catch (Exception ex)
        {
            return $"エラー: スクロールに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("特定の要素が見える位置までスクロールします")]
    public async Task<string> ScrollIntoView(
        [Description("CSSセレクタ")] string selector)
    {
        try
        {
            await _browserManager.EvaluateJavaScriptAsync($"document.querySelector('{selector}')?.scrollIntoView({{ behavior: 'smooth', block: 'center' }})");
            return $"成功: '{selector}' が見える位置までスクロールしました";
        }
        catch (Exception ex)
        {
            return $"エラー: スクロールに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページ内の特定のテキストを検索してハイライトします")]
    public async Task<string> FindAndHighlightText(
        [Description("検索するテキスト")] string text)
    {
        try
        {
            var count = await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const walker = document.createTreeWalker(
                    document.body,
                    NodeFilter.SHOW_TEXT,
                    null,
                    false
                );

                let count = 0;
                const searchTerm = '{text.Replace("'", "\\'")}';

                while (walker.nextNode()) {{
                    if (walker.nodeValue.toLowerCase().includes(searchTerm.toLowerCase())) {{
                        const range = document.createRange();
                        range.selectNode(walker.currentNode);
                        const mark = document.createElement('mark');
                        mark.style.backgroundColor = 'yellow';
                        range.surroundContents(mark);
                        count++;
                    }}
                }}
                return count;
            }}");
            return $"成功: '{text}' を {count} 箇所でハイライトしました";
        }
        catch (Exception ex)
        {
            return $"エラー: テキスト検索に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページ内のテーブルデータを抽出します")]
    public async Task<string> ExtractTableData(
        [Description("テーブルのCSSセレクタ（省略時は最初のテーブル）")] string? selector = null)
    {
        try
        {
            var tableSelector = selector ?? "table";
            var data = await _browserManager.EvaluateJavaScriptAsync($@"() => {{
                const table = document.querySelector('{tableSelector}');
                if (!table) return null;

                const rows = Array.from(table.querySelectorAll('tr'));
                return rows.map(row => {{
                    const cells = Array.from(row.querySelectorAll('th, td'));
                    return cells.map(cell => cell.textContent.trim());
                }});
            }}");

            if (data == null)
            {
                return "テーブルが見つかりません";
            }

            return $"テーブルデータ:\n{data}";
        }
        catch (Exception ex)
        {
            return $"エラー: テーブルデータの抽出に失敗しました: {ex.Message}";
        }
    }

    #endregion
}
