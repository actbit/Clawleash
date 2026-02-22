using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawleash.Configuration;
using Clawleash.Models;
using Clawleash.Security;
using Microsoft.Playwright;

namespace Clawleash.Services;

/// <summary>
/// Playwrightを使用したブラウザ管理サービス
/// URL制限とセキュリティ検証を適用
/// </summary>
public partial class BrowserManager : IBrowserManager
{
    private readonly ClawleashSettings _settings;
    private readonly UrlValidator _urlValidator;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    public bool IsInitialized => _browser != null && _page != null;

    public BrowserManager(ClawleashSettings settings, UrlValidator urlValidator)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _urlValidator = urlValidator ?? throw new ArgumentNullException(nameof(urlValidator));
    }

    private async Task EnsureInitializedAsync()
    {
        if (_browser != null && _page != null)
        {
            return;
        }

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _settings.Browser.Headless
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        });

        _page = await _context.NewPageAsync();
    }

    #region 基本操作

    public async Task<BrowserState> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var validation = _urlValidator.Validate(url);
        if (!validation.IsAllowed)
        {
            throw new UnauthorizedAccessException($"URLアクセスが拒否されました: {validation.ErrorMessage}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _page!.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        return await GetStateAsync(cancellationToken);
    }

    public async Task<BrowserState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var url = _page!.Url;
        var title = await _page.TitleAsync();

        byte[]? screenshot = null;
        string screenshotBase64 = string.Empty;

        if (_settings.Browser.ScreenshotOnAction)
        {
            screenshot = await _page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = false
            });
            screenshotBase64 = Convert.ToBase64String(screenshot);
        }

        var htmlContent = await _page.ContentAsync();

        string? accessibilityTree = null;
        try
        {
            var accessibilityData = await _page.EvaluateAsync<object?>(@"() => {
                function getNodeInfo(node) {
                    if (!node) return null;
                    const result = {
                        tag: node.tagName?.toLowerCase() || node.nodeName?.toLowerCase(),
                    };
                    if (node.id) result.id = node.id;
                    if (node.className && typeof node.className === 'string') result.className = node.className;
                    if (node.getAttribute) {
                        const role = node.getAttribute('role');
                        const ariaLabel = node.getAttribute('aria-label');
                        if (role) result.role = role;
                        if (ariaLabel) result.ariaLabel = ariaLabel;
                    }
                    if (node.textContent && node.textContent.trim() && node.children?.length === 0) {
                        result.text = node.textContent.trim().substring(0, 100);
                    }
                    if (node.children && node.children.length > 0) {
                        result.children = Array.from(node.children).slice(0, 5).map(getNodeInfo).filter(x => x);
                    }
                    return result;
                }
                return getNodeInfo(document.body);
            }");

            if (accessibilityData != null)
            {
                accessibilityTree = JsonSerializer.Serialize(accessibilityData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
        }
        catch
        {
            // アクセシビリティツリーの取得に失敗した場合はスキップ
        }

        return new BrowserState
        {
            ScreenshotBase64 = screenshotBase64,
            HtmlContent = htmlContent,
            AccessibilityTree = accessibilityTree,
            Url = url,
            Title = title,
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task ClickAsync(string selector, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.ClickAsync(selector, new PageClickOptions
        {
            Timeout = 10000
        });
    }

    public async Task TypeAsync(string selector, string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.FillAsync(selector, text, new PageFillOptions
        {
            Timeout = 10000
        });
    }

    public async Task<string> GetTextContentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return await _page!.EvaluateAsync<string>("document.body.innerText");
    }

    public async Task<object?> EvaluateJavaScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return await _page!.EvaluateAsync(script);
    }

    public async Task<byte[]> TakeScreenshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return await _page!.ScreenshotAsync();
    }

    public async Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return _page!.Url;
    }

    #endregion

    #region 拡張ブラウザ操作

    public async Task ScrollAsync(int pixels, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.EvaluateAsync($"window.scrollBy(0, {pixels})");
        await Task.Delay(500, cancellationToken);
    }

    public async Task ScrollToBottomAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await Task.Delay(500, cancellationToken);
    }

    public async Task WaitForSelectorAsync(string selector, int timeoutMs = 10000, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
        {
            Timeout = timeoutMs
        });
    }

    public async Task WaitForTimeoutAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(milliseconds, cancellationToken);
    }

    public async Task WaitForLoadStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.Keyboard.PressAsync(key);
    }

    public async Task KeyboardTypeAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.Keyboard.TypeAsync(text);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
    }

    public async Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.GoBackAsync(new PageGoBackOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
    }

    public async Task GoForwardAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.GoForwardAsync(new PageGoForwardOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
    }

    public async Task HoverAsync(string selector, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await _page!.HoverAsync(selector, new PageHoverOptions
        {
            Timeout = 10000
        });
    }

    public async Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var fileChooserTask = _page!.WaitForFileChooserAsync();
        await _page.ClickAsync(selector);
        var fileChooser = await fileChooserTask;
        await fileChooser.SetFilesAsync(filePath);
    }

    public async Task<List<string>> GetLinksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var links = await _page!.EvaluateAsync<string[]>(@"() => {
            return Array.from(document.querySelectorAll('a[href]'))
                .map(a => a.href)
                .filter(href => href && !href.startsWith('javascript:'));
        }");

        return links?.Distinct().ToList() ?? [];
    }

    public async Task<PageMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = await _page!.EvaluateAsync<PageMetadata>(@"() => {
            const getMeta = (name) => {
                const el = document.querySelector(`meta[name='${name}'], meta[property='og:${name}']`);
                return el ? el.getAttribute('content') : null;
            };
            return {
                title: document.title,
                description: getMeta('description'),
                keywords: getMeta('keywords'),
                author: getMeta('author'),
                language: document.documentElement.lang || 'en',
                ogTitle: getMeta('title'),
                ogDescription: getMeta('description'),
                ogImage: getMeta('image'),
                statusCode: 200
            };
        }");

        return metadata ?? new PageMetadata();
    }

    public async Task<string> GetMarkdownAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var html = await _page!.ContentAsync();
        return HtmlToMarkdown(html);
    }

    #endregion

    #region クロール・スクレイプ操作

    public async Task<ScrapeResult> ScrapeAsync(string url, bool includeScreenshot = false, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var validation = _urlValidator.Validate(url);
        if (!validation.IsAllowed)
        {
            return new ScrapeResult
            {
                Url = url,
                Metadata = new PageMetadata { StatusCode = 403 }
            };
        }

        try
        {
            await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            cancellationToken.ThrowIfCancellationRequested();

            var html = await _page.ContentAsync();
            var text = await GetTextContentAsync(cancellationToken);
            var markdown = HtmlToMarkdown(html);
            var links = await GetLinksAsync(cancellationToken);
            var metadata = await GetMetadataAsync(cancellationToken);
            var title = await _page.TitleAsync();

            string? screenshotBase64 = null;
            if (includeScreenshot)
            {
                var screenshot = await TakeScreenshotAsync(cancellationToken);
                screenshotBase64 = Convert.ToBase64String(screenshot);
            }

            return new ScrapeResult
            {
                Url = url,
                Title = title,
                Html = html,
                Text = text,
                Markdown = markdown,
                Links = links,
                Metadata = metadata,
                ScreenshotBase64 = screenshotBase64
            };
        }
        catch (Exception)
        {
            return new ScrapeResult
            {
                Url = url,
                Metadata = new PageMetadata { StatusCode = 500 }
            };
        }
    }

    public async Task<CrawlResult> CrawlAsync(string startUrl, int maxPages = 10, int maxDepth = 2, CancellationToken cancellationToken = default)
    {
        var result = new CrawlResult
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = "running",
            Total = maxPages
        };

        try
        {
            var visited = new HashSet<string>();
            var queue = new Queue<(string Url, int Depth)>();
            var baseUri = new Uri(startUrl);

            queue.Enqueue((startUrl, 0));
            visited.Add(NormalizeUrl(startUrl));

            while (queue.Count > 0 && result.Data.Count < maxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (url, depth) = queue.Dequeue();

                if (depth > maxDepth) continue;

                var scrapeResult = await ScrapeAsync(url, false, cancellationToken);
                result.Data.Add(scrapeResult);
                result.Completed = result.Data.Count;

                if (depth < maxDepth)
                {
                    foreach (var link in scrapeResult.Links)
                    {
                        var normalizedLink = NormalizeUrl(link);
                        if (!visited.Contains(normalizedLink) && IsSameDomain(baseUri, normalizedLink))
                        {
                            visited.Add(normalizedLink);
                            queue.Enqueue((link, depth + 1));
                        }
                    }
                }

                // レート制限
                await Task.Delay(500, cancellationToken);
            }

            result.Status = "completed";
            result.Success = true;
            result.Total = result.Data.Count;
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<MapResult> MapAsync(string url, string? searchQuery = null, CancellationToken cancellationToken = default)
    {
        var result = new MapResult();

        try
        {
            await EnsureInitializedAsync();

            var validation = _urlValidator.Validate(url);
            if (!validation.IsAllowed)
            {
                result.Error = validation.ErrorMessage;
                return result;
            }

            await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            cancellationToken.ThrowIfCancellationRequested();

            var links = await GetLinksAsync(cancellationToken);
            var baseUri = new Uri(url);

            // 同一ドメインのリンクのみをフィルタリング
            var filteredLinks = links
                .Where(l => IsSameDomain(baseUri, l))
                .Distinct()
                .ToList();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                // 検索クエリでフィルタリング
                filteredLinks = filteredLinks
                    .Where(l => l.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            result.Links = filteredLinks;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, int limit = 5, bool scrapeContent = false, CancellationToken cancellationToken = default)
    {
        var result = new SearchResult();

        try
        {
            await EnsureInitializedAsync();

            // DuckDuckGoを使用して検索（APIキー不要）
            var searchUrl = $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}";

            var validation = _urlValidator.Validate(searchUrl);
            if (!validation.IsAllowed)
            {
                result.Error = validation.ErrorMessage;
                return result;
            }

            await _page!.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            cancellationToken.ThrowIfCancellationRequested();

            // 検索結果を取得
            var searchResults = await _page.EvaluateAsync<SearchResultItem[]>(@"() => {
                const results = [];
                const items = document.querySelectorAll('[data-testid=""result""]') ||
                              document.querySelectorAll('.result') ||
                              document.querySelectorAll('article');

                for (let i = 0; i < Math.min(items.length, 10); i++) {
                    const item = items[i];
                    const linkEl = item.querySelector('a[href]');
                    const titleEl = item.querySelector('h2, h3, [data-testid=""result-title-a""]');
                    const descEl = item.querySelector('[data-testid=""result-snippet""], p, .snippet');

                    if (linkEl) {
                        results.push({
                            url: linkEl.href,
                            title: titleEl ? titleEl.textContent.trim() : '',
                            description: descEl ? descEl.textContent.trim() : ''
                        });
                    }
                }
                return results;
            }");

            result.Results = (searchResults ?? []).Take(limit).ToList();

            if (scrapeContent)
            {
                foreach (var item in result.Results.Take(3))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var scrape = await ScrapeAsync(item.Url, false, cancellationToken);
                    item.Content = scrape.Markdown ?? scrape.Text;

                    await Task.Delay(500, cancellationToken);
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<List<ScrapeResult>> BatchScrapeAsync(List<string> urls, CancellationToken cancellationToken = default)
    {
        var results = new List<ScrapeResult>();

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ScrapeAsync(url, false, cancellationToken);
            results.Add(result);

            await Task.Delay(500, cancellationToken);
        }

        return results;
    }

    #endregion

    #region ユーティリティ

    private static string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
        catch
        {
            return url;
        }
    }

    private static bool IsSameDomain(Uri baseUri, string url)
    {
        try
        {
            var targetUri = new Uri(url);
            return baseUri.Host.Equals(targetUri.Host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string HtmlToMarkdown(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var markdown = html;

        // スクリプト、スタイル、コメントを削除
        markdown = ScriptRegex().Replace(markdown, "");
        markdown = StyleRegex().Replace(markdown, "");
        markdown = CommentRegex().Replace(markdown, "");

        // ヘッダー
        markdown = H1Regex().Replace(markdown, "\n# $1\n");
        markdown = H2Regex().Replace(markdown, "\n## $1\n");
        markdown = H3Regex().Replace(markdown, "\n### $1\n");
        markdown = H4Regex().Replace(markdown, "\n#### $1\n");
        markdown = H5Regex().Replace(markdown, "\n##### $1\n");
        markdown = H6Regex().Replace(markdown, "\n###### $1\n");

        // リンク
        markdown = LinkRegex().Replace(markdown, "[$2]($1)");

        // 画像
        markdown = ImageRegex().Replace(markdown, "![$2]($1)");

        // 太字・斜体
        markdown = BoldRegex().Replace(markdown, "**$1**");
        markdown = ItalicRegex().Replace(markdown, "*$1*");

        // リスト
        markdown = LiRegex().Replace(markdown, "\n- $1");

        // 段落
        markdown = DivRegex().Replace(markdown, "\n$1\n");
        markdown = PRegex().Replace(markdown, "\n$1\n");

        // 改行
        markdown = BrRegex().Replace(markdown, "\n");

        // 残りのタグを削除
        markdown = HtmlTagRegex().Replace(markdown, "");

        // 複数の改行をまとめる
        markdown = NewLineRegex().Replace(markdown, "\n\n");

        // デコード
        markdown = System.Net.WebUtility.HtmlDecode(markdown);

        return markdown.Trim();
    }

    [GeneratedRegex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"<h2[^>]*>(.*?)</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H2Regex();

    [GeneratedRegex(@"<h3[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H3Regex();

    [GeneratedRegex(@"<h4[^>]*>(.*?)</h4>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H4Regex();

    [GeneratedRegex(@"<h5[^>]*>(.*?)</h5>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H5Regex();

    [GeneratedRegex(@"<h6[^>]*>(.*?)</h6>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H6Regex();

    [GeneratedRegex(@"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<img[^>]*src=""([^""]*)""[^>]*alt=""([^""]*)""[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"<(strong|b)>(.*?)</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"<(em|i)>(.*?)</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LiRegex();

    [GeneratedRegex(@"<div[^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DivRegex();

    [GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex NewLineRegex();

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_page != null)
        {
            await _page.CloseAsync();
            _page = null;
        }

        if (_context != null)
        {
            await _context.CloseAsync();
            _context = null;
        }

        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        _disposed = true;
    }
}
