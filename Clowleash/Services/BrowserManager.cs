using Clowleash.Configuration;
using Clowleash.Models;
using Clowleash.Security;
using Microsoft.Playwright;

namespace Clowleash.Services;

/// <summary>
/// Playwrightを使用したブラウザ管理サービス
/// URL制限とセキュリティ検証を適用
/// </summary>
public class BrowserManager : IBrowserManager
{
    private readonly ClowleashSettings _settings;
    private readonly UrlValidator _urlValidator;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    public bool IsInitialized => _browser != null && _page != null;

    public BrowserManager(ClowleashSettings settings, UrlValidator urlValidator)
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

    public async Task<BrowserState> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        // URLを検証
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

        // スクリーンショット
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

        // HTML コンテンツ
        var htmlContent = await _page.ContentAsync();

        // アクセシビリティツリー（簡易版: DOM構造をJSONで取得）
        string? accessibilityTree = null;
        try
        {
            // アクセシビリティスナップショットの代替実装
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
                accessibilityTree = System.Text.Json.JsonSerializer.Serialize(accessibilityData, new System.Text.Json.JsonSerializerOptions
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
