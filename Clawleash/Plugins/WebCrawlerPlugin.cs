using System.ComponentModel;
using System.Text.Json;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// Webクローラー・スクレイパープラグイン
/// Firecrawl/OpenCraw風のWebスクレイピング機能を提供
/// </summary>
public class WebCrawlerPlugin
{
    private readonly IBrowserManager _browserManager;

    public WebCrawlerPlugin(IBrowserManager browserManager)
    {
        _browserManager = browserManager ?? throw new ArgumentNullException(nameof(browserManager));
    }

    #region スクレイプ機能

    [KernelFunction, Description("URLをスクレイプしてMarkdown形式でコンテンツを取得します（Firecrawl風）")]
    public async Task<string> ScrapeUrl(
        [Description("スクレイプするURL")] string url,
        [Description("スクリーンショットを含めるかどうか")] bool includeScreenshot = false)
    {
        try
        {
            var result = await _browserManager.ScrapeAsync(url, includeScreenshot);

            if (result.Metadata?.StatusCode == 403)
            {
                return "エラー: このURLへのアクセスは許可されていません";
            }

            if (result.Metadata?.StatusCode == 500)
            {
                return "エラー: ページの読み込みに失敗しました";
            }

            var response = $"""
                ## スクレイプ結果

                **URL**: {result.Url}
                **タイトル**: {result.Title}

                ### メタデータ
                - 説明: {result.Metadata?.Description ?? "なし"}
                - キーワード: {result.Metadata?.Keywords ?? "なし"}
                - 言語: {result.Metadata?.Language ?? "不明"}

                ### Markdown コンテンツ
                {TruncateText(result.Markdown ?? result.Text ?? "コンテンツなし", 5000)}

                ### リンク ({result.Links.Count}件)
                {string.Join("\n", result.Links.Take(20).Select(l => $"- {l}"))}
                {(result.Links.Count > 20 ? $"\n... 他 {result.Links.Count - 20} 件" : "")}
                """;

            if (includeScreenshot && !string.IsNullOrEmpty(result.ScreenshotBase64))
            {
                response += $"\n\n### スクリーンショット\nBase64: {result.ScreenshotBase64.Length} 文字";
            }

            return response;
        }
        catch (Exception ex)
        {
            return $"エラー: スクレイプに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region クロール機能

    [KernelFunction, Description("Webサイト全体をクロールして複数ページのコンテンツを取得します（Firecrawl風）")]
    public async Task<string> CrawlWebsite(
        [Description("クロールを開始するURL")] string startUrl,
        [Description("最大ページ数（デフォルト: 10）")] int maxPages = 10,
        [Description("最大深さ（デフォルト: 2）")] int maxDepth = 2)
    {
        try
        {
            var result = await _browserManager.CrawlAsync(startUrl, maxPages, maxDepth);

            if (!result.Success)
            {
                return $"エラー: クロールに失敗しました: {result.Error}";
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"## クロール結果 (ID: {result.Id})");
            response.AppendLine($"**ステータス**: {result.Status}");
            response.AppendLine($"**収集ページ数**: {result.Total}");
            response.AppendLine();

            foreach (var page in result.Data.Take(5))
            {
                response.AppendLine($"### {page.Title ?? "タイトルなし"}");
                response.AppendLine($"**URL**: {page.Url}");
                response.AppendLine($"**概要**: {TruncateText(page.Text ?? "", 300)}");
                response.AppendLine();
            }

            if (result.Data.Count > 5)
            {
                response.AppendLine($"\n... 他 {result.Data.Count - 5} ページ");
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: クロールに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region マップ機能

    [KernelFunction, Description("Webサイトのサイトマップ（全URL一覧）を高速に取得します（Firecrawl風）")]
    public async Task<string> MapWebsite(
        [Description("サイトマップを取得するURL")] string url,
        [Description("検索クエリ（オプション・URLフィルタリング用）")] string? searchQuery = null)
    {
        try
        {
            var result = await _browserManager.MapAsync(url, searchQuery);

            if (!result.Success)
            {
                return $"エラー: サイトマップの取得に失敗しました: {result.Error}";
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"## サイトマップ結果");
            response.AppendLine($"**ベースURL**: {url}");
            response.AppendLine($"**発見されたリンク数**: {result.Links.Count}");
            response.AppendLine();

            foreach (var link in result.Links.Take(50))
            {
                response.AppendLine($"- {link}");
            }

            if (result.Links.Count > 50)
            {
                response.AppendLine($"\n... 他 {result.Links.Count - 50} 件のリンク");
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: サイトマップの取得に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region 検索機能

    [KernelFunction, Description("Webを検索して結果を取得します（Firecrawl風）")]
    public async Task<string> SearchWeb(
        [Description("検索クエリ")] string query,
        [Description("結果の最大数（デフォルト: 5）")] int limit = 5,
        [Description("コンテンツもスクレイプするかどうか")] bool scrapeContent = false)
    {
        try
        {
            var result = await _browserManager.SearchAsync(query, limit, scrapeContent);

            if (!result.Success)
            {
                return $"エラー: 検索に失敗しました: {result.Error}";
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"## 検索結果: \"{query}\"");
            response.AppendLine($"**結果数**: {result.Results.Count}");
            response.AppendLine();

            for (int i = 0; i < result.Results.Count; i++)
            {
                var item = result.Results[i];
                response.AppendLine($"### {i + 1}. {item.Title ?? "タイトルなし"}");
                response.AppendLine($"**URL**: {item.Url}");
                response.AppendLine($"**説明**: {item.Description ?? "なし"}");

                if (!string.IsNullOrEmpty(item.Content))
                {
                    response.AppendLine($"**コンテンツ**: {TruncateText(item.Content, 500)}");
                }

                response.AppendLine();
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: 検索に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region バッチスクレイプ

    [KernelFunction, Description("複数のURLを一括でスクレイプします（Firecrawl風）")]
    public async Task<string> BatchScrape(
        [Description("スクレイプするURLのリスト（JSON配列形式）")] string urlsJson)
    {
        try
        {
            var urls = JsonSerializer.Deserialize<List<string>>(urlsJson);
            if (urls == null || urls.Count == 0)
            {
                return "エラー: URLリストが空です";
            }

            var results = await _browserManager.BatchScrapeAsync(urls);

            var response = new System.Text.StringBuilder();
            response.AppendLine($"## バッチスクレイプ結果");
            response.AppendLine($"**処理URL数**: {results.Count}");
            response.AppendLine();

            foreach (var result in results)
            {
                response.AppendLine($"### {result.Title ?? "タイトルなし"}");
                response.AppendLine($"**URL**: {result.Url}");
                response.AppendLine($"**Markdown**: {TruncateText(result.Markdown ?? "", 300)}");
                response.AppendLine();
            }

            return response.ToString();
        }
        catch (JsonException)
        {
            return "エラー: URLリストのJSON形式が無効です。[\"url1\", \"url2\"] の形式で指定してください";
        }
        catch (Exception ex)
        {
            return $"エラー: バッチスクレイプに失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region 構造化データ抽出

    [KernelFunction, Description("ページからリンクを抽出します")]
    public async Task<string> ExtractLinks()
    {
        try
        {
            var links = await _browserManager.GetLinksAsync();

            var response = new System.Text.StringBuilder();
            response.AppendLine($"## リンク一覧 ({links.Count}件)");

            foreach (var link in links.Take(50))
            {
                response.AppendLine($"- {link}");
            }

            if (links.Count > 50)
            {
                response.AppendLine($"\n... 他 {links.Count - 50} 件");
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: リンクの抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のページのメタデータを抽出します")]
    public async Task<string> ExtractMetadata()
    {
        try
        {
            var metadata = await _browserManager.GetMetadataAsync();

            return $"""
                ## ページメタデータ

                - **タイトル**: {metadata.Title ?? "なし"}
                - **説明**: {metadata.Description ?? "なし"}
                - **キーワード**: {metadata.Keywords ?? "なし"}
                - **作者**: {metadata.Author ?? "なし"}
                - **言語**: {metadata.Language ?? "不明"}
                - **OG タイトル**: {metadata.OgTitle ?? "なし"}
                - **OG 説明**: {metadata.OgDescription ?? "なし"}
                - **OG 画像**: {metadata.OgImage ?? "なし"}
                - **ステータスコード**: {metadata.StatusCode}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: メタデータの抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のページをMarkdown形式で取得します")]
    public async Task<string> GetPageMarkdown()
    {
        try
        {
            var markdown = await _browserManager.GetMarkdownAsync();
            return $"## Markdown コンテンツ\n\n{TruncateText(markdown, 10000)}";
        }
        catch (Exception ex)
        {
            return $"エラー: Markdownの取得に失敗しました: {ex.Message}";
        }
    }

    #endregion

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
