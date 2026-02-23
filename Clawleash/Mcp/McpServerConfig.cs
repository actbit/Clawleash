using System.Text.Json.Serialization;

namespace Clawleash.Mcp;

/// <summary>
/// MCPサーバー設定
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// サーバー名（一意識別子）
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// トランスポート種別
    /// </summary>
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// 実行コマンド（stdio用）
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// コマンド引数
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    /// <summary>
    /// 環境変数
    /// </summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// サーバーURL（SSE用）
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// HTTPヘッダー（SSE用）
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// 有効/無効
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// タイムアウト（ミリ秒）
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// サンドボックスで実行するか
    /// </summary>
    [JsonPropertyName("useSandbox")]
    public bool UseSandbox { get; set; } = true;
}

/// <summary>
/// MCP全体設定
/// </summary>
public class McpSettings
{
    /// <summary>
    /// MCPサーバー一覧
    /// </summary>
    [JsonPropertyName("servers")]
    public List<McpServerConfig> Servers { get; set; } = new();

    /// <summary>
    /// デフォルトタイムアウト（ミリ秒）
    /// </summary>
    [JsonPropertyName("defaultTimeoutMs")]
    public int DefaultTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// MCP機能を有効にするか
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
