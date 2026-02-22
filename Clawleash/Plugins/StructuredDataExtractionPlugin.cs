using System.ComponentModel;
using System.Text.Json;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// 構造化データ抽出プラグイン
/// AIを使用してWebページから構造化データを抽出する（Firecrawl風のExtract機能）
/// </summary>
public class StructuredDataExtractionPlugin
{
    private readonly IBrowserManager _browserManager;
    private readonly Kernel _kernel;

    public StructuredDataExtractionPlugin(IBrowserManager browserManager, Kernel kernel)
    {
        _browserManager = browserManager ?? throw new ArgumentNullException(nameof(browserManager));
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    [KernelFunction, Description("現在のページからAIを使って構造化データを抽出します（Firecrawl風のLLM Extract）")]
    public async Task<string> ExtractStructuredData(
        [Description("抽出したいデータの説明（例: 記事のタイトル、著者、公開日を抽出）")] string extractionPrompt)
    {
        try
        {
            // ページの内容を取得
            var pageText = await _browserManager.GetTextContentAsync();
            var metadata = await _browserManager.GetMetadataAsync();

            // ページ内容が長すぎる場合は切り詰め
            var content = pageText.Length > 8000 ? pageText[..8000] : pageText;

            var prompt = $"""
                以下のWebページから構造化データを抽出してください。

                ## 抽出指示
                {extractionPrompt}

                ## ページ情報
                - タイトル: {metadata.Title}
                - URL: {await _browserManager.GetCurrentUrlAsync()}

                ## ページ内容
                {content}

                ## 出力形式
                JSON形式で出力してください。抽出できなかったフィールドは null としてください。
                """;

            // AIでデータを抽出
            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## 構造化データ抽出結果

                {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 構造化データの抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のページから特定のスキーマに基づいてデータを抽出します")]
    public async Task<string> ExtractWithSchema(
        [Description("抽出するフィールドのJSONスキーマ（例: {\"title\": \"string\", \"price\": \"number\"}）")] string schemaJson)
    {
        try
        {
            var schema = JsonDocument.Parse(schemaJson);
            var fields = new List<string>();

            if (schema.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    fields.Add($"- {prop.Name}: {prop.Value.GetProperty("type").GetString() ?? "string"}");
                }
            }

            var pageText = await _browserManager.GetTextContentAsync();
            var content = pageText.Length > 8000 ? pageText[..8000] : pageText;

            var prompt = $"""
                以下のWebページから、指定されたスキーマに従ってデータを抽出してください。

                ## 必要なフィールド
                {string.Join("\n", fields)}

                ## ページ内容
                {content}

                ## 出力形式
                以下のJSONスキーマに従って出力してください:
                {schemaJson}

                抽出できなかったフィールドは null としてください。
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## スキーマベース抽出結果

                {result.GetValue<string>()}
                """;
        }
        catch (JsonException)
        {
            return "エラー: スキーマのJSON形式が無効です";
        }
        catch (Exception ex)
        {
            return $"エラー: データ抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページから商品情報を抽出します")]
    public async Task<string> ExtractProductInfo()
    {
        try
        {
            var pageText = await _browserManager.GetTextContentAsync();
            var content = pageText.Length > 8000 ? pageText[..8000] : pageText;

            var jsonTemplate = """
                {
                    "name": "商品名",
                    "price": 価格（数値）,
                    "currency": "通貨",
                    "description": "商品説明",
                    "availability": "在庫状況",
                    "rating": 評価（数値）,
                    "reviewCount": レビュー数（数値）,
                    "imageUrl": "商品画像URL",
                    "category": "カテゴリ"
                }
                """;

            var prompt = $"""
                以下のWebページから商品情報を抽出してください。

                ## ページ内容
                {content}

                ## 出力形式（JSON）
                {jsonTemplate}
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## 商品情報抽出結果

                {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 商品情報の抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページから記事・ニュース情報を抽出します")]
    public async Task<string> ExtractArticleInfo()
    {
        try
        {
            var pageText = await _browserManager.GetTextContentAsync();
            var metadata = await _browserManager.GetMetadataAsync();
            var content = pageText.Length > 8000 ? pageText[..8000] : pageText;

            var jsonTemplate = """
                {
                    "title": "記事タイトル",
                    "author": "著者名",
                    "publishDate": "公開日",
                    "modifiedDate": "更新日",
                    "summary": "要約（3-5文）",
                    "category": "カテゴリ",
                    "tags": ["タグ1", "タグ2"],
                    "readTime": 推定読了時間（分）,
                    "mainImage": "メイン画像URL"
                }
                """;

            var prompt = $"""
                以下のWebページから記事・ニュース情報を抽出してください。

                ## ページ内容
                {content}

                ## 出力形式（JSON）
                {jsonTemplate}
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## 記事情報抽出結果

                {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 記事情報の抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページから連絡先情報を抽出します")]
    public async Task<string> ExtractContactInfo()
    {
        try
        {
            var pageText = await _browserManager.GetTextContentAsync();
            var content = pageText.Length > 8000 ? pageText[..8000] : pageText;

            var jsonTemplate = """
                {
                    "company": "会社名",
                    "address": "住所",
                    "phone": "電話番号",
                    "email": "メールアドレス",
                    "website": "ウェブサイト",
                    "socialMedia": {
                        "twitter": "Twitter URL",
                        "facebook": "Facebook URL",
                        "linkedin": "LinkedIn URL"
                    }
                }
                """;

            var prompt = $"""
                以下のWebページから連絡先情報を抽出してください。

                ## ページ内容
                {content}

                ## 出力形式（JSON）
                {jsonTemplate}

                情報が見つからない場合は null を設定してください。
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## 連絡先情報抽出結果

                {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 連絡先情報の抽出に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページの内容を要約します")]
    public async Task<string> SummarizePage(
        [Description("要約の最大文字数")] int maxLength = 500)
    {
        try
        {
            var pageText = await _browserManager.GetTextContentAsync();
            var content = pageText.Length > 10000 ? pageText[..10000] : pageText;

            var prompt = $"""
                以下のWebページの内容を{maxLength}文字以内で要約してください。
                重要なポイントを箇条書きで含めてください。

                ## ページ内容
                {content}

                ## 要約
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## ページ要約

                {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 要約の生成に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ページの内容を特定の質問に基づいて分析します")]
    public async Task<string> AnalyzePageContent(
        [Description("分析に関する質問")] string question)
    {
        try
        {
            var pageText = await _browserManager.GetTextContentAsync();
            var content = pageText.Length > 10000 ? pageText[..10000] : pageText;

            var prompt = $"""
                以下のWebページの内容に基づいて質問に答えてください。

                ## 質問
                {question}

                ## ページ内容
                {content}

                ## 回答
                回答は日本語で、ページの内容に基づいて正確に答えてください。
                ページに情報がない場合は「ページ内に情報が見つかりません」と回答してください。
                """;

            var result = await _kernel.InvokePromptAsync(prompt);

            return $"""
                ## 分析結果

                質問: {question}

                回答: {result.GetValue<string>()}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: 分析に失敗しました: {ex.Message}";
        }
    }
}
