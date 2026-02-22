using System.Text.Json.Serialization;

namespace Clawleash.Models;

/// <summary>
/// ブラウザの現在の状態（OpenClow風）
/// </summary>
public class BrowserState
{
    /// <summary>
    /// スクリーンショットのBase64エンコード画像データ
    /// </summary>
    [JsonPropertyName("screenshot")]
    public string ScreenshotBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 現在のページのHTMLコンテンツ
    /// </summary>
    [JsonPropertyName("html")]
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// アクセシビリティツリー（JSON形式）
    /// </summary>
    [JsonPropertyName("accessibility_tree")]
    public string? AccessibilityTree { get; set; }

    /// <summary>
    /// 現在のURL
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// ページタイトル
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// タイムスタンプ
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// AIが理解しやすい形式で状態を表現
    /// </summary>
    public override string ToString()
    {
        return $"""
            Browser State:
            URL: {Url}
            Title: {Title}
            Timestamp: {Timestamp:O}

            HTML Content (first 2000 chars):
            {(HtmlContent.Length > 2000 ? HtmlContent[..2000] + "..." : HtmlContent)}

            Accessibility Tree:
            {AccessibilityTree ?? "Not available"}

            Screenshot: {(string.IsNullOrEmpty(ScreenshotBase64) ? "Not available" : $"{ScreenshotBase64.Length} bytes (base64)")}
            """;
    }
}
