using System.Text.Json.Serialization;

namespace Clawleash.Models;

/// <summary>
/// スキル定義
/// プロンプトテンプレートを再利用可能なコマンドとして定義
/// </summary>
public class Skill
{
    /// <summary>
    /// スキル名（一意識別子）
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// スキルの説明
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// プロンプトテンプレート
    /// {{parameterName}} 形式でパラメータを埋め込み
    /// </summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// スキルのパラメータ定義
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<SkillParameter> Parameters { get; set; } = new();

    /// <summary>
    /// スキルのバージョン
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// スキルの作成者
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// スキルのタグ
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// スキル呼び出し時の追加指示
    /// </summary>
    [JsonPropertyName("systemInstruction")]
    public string? SystemInstruction { get; set; }

    /// <summary>
    /// スキルのファイルパス（読み込み後に設定）
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>
    /// プロンプトテンプレートにパラメータを適用
    /// </summary>
    public string ApplyParameters(Dictionary<string, object> args)
    {
        var result = Prompt;

        foreach (var param in Parameters)
        {
            var value = args.TryGetValue(param.Name, out var v) ? v : param.Default;

            if (value == null && param.Required)
            {
                throw new ArgumentException($"必須パラメータ '{param.Name}' が指定されていません");
            }

            if (value != null)
            {
                result = result.Replace($"{{{{{param.Name}}}}}", value.ToString());
            }
        }

        return result;
    }
}

/// <summary>
/// スキルパラメータ定義
/// </summary>
public class SkillParameter
{
    /// <summary>
    /// パラメータ名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// パラメータの説明
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// パラメータの型 (string, number, boolean, array, object)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// 必須かどうか
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    /// <summary>
    /// デフォルト値
    /// </summary>
    [JsonPropertyName("default")]
    public object? Default { get; set; }

    /// <summary>
    /// 選択肢（enum的な制限）
    /// </summary>
    [JsonPropertyName("enum")]
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// 例
    /// </summary>
    [JsonPropertyName("example")]
    public string? Example { get; set; }
}
