using System.Text.Json.Serialization;

namespace Clawleash.Models;

/// <summary>
/// クロール結果
/// </summary>
public class CrawlResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("data")]
    public List<ScrapeResult> Data { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// スクレイプ結果
/// </summary>
public class ScrapeResult
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }

    [JsonPropertyName("html")]
    public string? Html { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("links")]
    public List<string> Links { get; set; } = [];

    [JsonPropertyName("metadata")]
    public PageMetadata? Metadata { get; set; }

    [JsonPropertyName("screenshot")]
    public string? ScreenshotBase64 { get; set; }
}

/// <summary>
/// ページメタデータ
/// </summary>
public class PageMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("keywords")]
    public string? Keywords { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("ogTitle")]
    public string? OgTitle { get; set; }

    [JsonPropertyName("ogDescription")]
    public string? OgDescription { get; set; }

    [JsonPropertyName("ogImage")]
    public string? OgImage { get; set; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}

/// <summary>
/// サイトマップ結果
/// </summary>
public class MapResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("links")]
    public List<string> Links { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Web検索結果
/// </summary>
public class SearchResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("results")]
    public List<SearchResultItem> Results { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 検索結果アイテム
/// </summary>
public class SearchResultItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// 構造化データ抽出結果
/// </summary>
public class ExtractResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
