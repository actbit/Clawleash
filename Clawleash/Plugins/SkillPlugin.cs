using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Clawleash.Models;
using Clawleash.Skills;

namespace Clawleash.Plugins;

/// <summary>
/// スキルを呼び出すためのSemantic Kernelプラグイン
/// </summary>
public class SkillPlugin
{
    private readonly SkillLoader _skillLoader;
    private readonly Kernel _kernel;

    public SkillPlugin(SkillLoader skillLoader, Kernel kernel)
    {
        _skillLoader = skillLoader;
        _kernel = kernel;
    }

    /// <summary>
    /// 利用可能なスキル一覧を表示
    /// </summary>
    [KernelFunction("list_skills")]
    [Description("利用可能なスキルの一覧を表示します")]
    public string ListSkills(
        [Description("タグでフィルタリング（オプション）")] string? tag = null)
    {
        var skills = _skillLoader.ListSkills(tag).ToList();

        if (skills.Count == 0)
        {
            return tag != null
                ? $"タグ '{tag}' に一致するスキルが見つかりません"
                : "利用可能なスキルがありません";
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine($"## 利用可能なスキル ({skills.Count}件)");
        result.AppendLine();

        foreach (var skill in skills.OrderBy(s => s.Name))
        {
            result.AppendLine($"### {skill.Name}");
            result.AppendLine($"- 説明: {skill.Description}");
            result.AppendLine($"- バージョン: {skill.Version}");
            if (skill.Tags.Count > 0)
            {
                result.AppendLine($"- タグ: {string.Join(", ", skill.Tags)}");
            }
            if (skill.Parameters.Count > 0)
            {
                result.AppendLine($"- パラメータ:");
                foreach (var param in skill.Parameters)
                {
                    var required = param.Required ? "(必須)" : "(任意)";
                    var defaultVal = param.Default != null ? $" [既定値: {param.Default}]" : "";
                    result.AppendLine($"  - {param.Name} {required}: {param.Description}{defaultVal}");
                }
            }
            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// スキルを実行
    /// </summary>
    [KernelFunction("execute_skill")]
    [Description("指定したスキルを実行します。パラメータはJSON形式で指定してください。")]
    public async Task<string> ExecuteSkillAsync(
        [Description("スキル名")] string skillName,
        [Description("パラメータ(JSON形式)")]

        string parametersJson = "{}")
    {
        var skill = _skillLoader.GetSkill(skillName);
        if (skill == null)
        {
            return $"エラー: スキル '{skillName}' が見つかりません";
        }

        try
        {
            // パラメータを解析
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson)
                ?? new Dictionary<string, object>();

            // プロンプトテンプレートにパラメータを適用
            var prompt = skill.ApplyParameters(parameters);

            // AIにプロンプトを送信
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            var chatHistory = new ChatHistory();

            // システム指示があれば追加
            if (!string.IsNullOrEmpty(skill.SystemInstruction))
            {
                chatHistory.AddSystemMessage(skill.SystemInstruction);
            }

            chatHistory.AddUserMessage(prompt);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var response = await chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            return response.Content ?? "スキルの実行結果が空です";
        }
        catch (Exception ex)
        {
            return $"スキル実行エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// スキルの詳細を表示
    /// </summary>
    [KernelFunction("show_skill")]
    [Description("指定したスキルの詳細情報を表示します")]
    public string ShowSkill(
        [Description("スキル名")] string skillName)
    {
        var skill = _skillLoader.GetSkill(skillName);
        if (skill == null)
        {
            return $"エラー: スキル '{skillName}' が見つかりません";
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine($"# スキル: {skill.Name}");
        result.AppendLine();
        result.AppendLine($"**説明:** {skill.Description}");
        result.AppendLine($"**バージョン:** {skill.Version}");

        if (!string.IsNullOrEmpty(skill.Author))
        {
            result.AppendLine($"**作成者:** {skill.Author}");
        }

        if (skill.Tags.Count > 0)
        {
            result.AppendLine($"**タグ:** {string.Join(", ", skill.Tags)}");
        }

        result.AppendLine();
        result.AppendLine("## パラメータ");

        if (skill.Parameters.Count == 0)
        {
            result.AppendLine("このスキルにはパラメータがありません");
        }
        else
        {
            foreach (var param in skill.Parameters)
            {
                result.AppendLine();
                result.AppendLine($"### {param.Name}");
                result.AppendLine($"- 型: {param.Type}");
                result.AppendLine($"- 必須: {(param.Required ? "はい" : "いいえ")}");
                result.AppendLine($"- 説明: {param.Description}");

                if (param.Default != null)
                {
                    result.AppendLine($"- 既定値: {param.Default}");
                }

                if (param.EnumValues?.Count > 0)
                {
                    result.AppendLine($"- 選択肢: {string.Join(", ", param.EnumValues)}");
                }

                if (!string.IsNullOrEmpty(param.Example))
                {
                    result.AppendLine($"- 例: {param.Example}");
                }
            }
        }

        result.AppendLine();
        result.AppendLine("## プロンプトテンプレート");
        result.AppendLine("```");
        result.AppendLine(skill.Prompt);
        result.AppendLine("```");

        if (!string.IsNullOrEmpty(skill.SystemInstruction))
        {
            result.AppendLine();
            result.AppendLine("## システム指示");
            result.AppendLine("```");
            result.AppendLine(skill.SystemInstruction);
            result.AppendLine("```");
        }

        return result.ToString();
    }

    /// <summary>
    /// 新しいスキルを登録（インライン定義）
    /// </summary>
    [KernelFunction("register_skill")]
    [Description("新しいスキルを登録します。YAMLまたはJSON形式で指定できます。")]
    public string RegisterSkill(
        [Description("スキル定義(YAMLまたはJSON形式)")]

        string skillDefinition,
        [Description("フォーマット (yaml または json、省略時は自動判定)")]

        string? format = null)
    {
        try
        {
            // フォーマット自動判定
            var trimmed = skillDefinition.TrimStart();
            var isYaml = format?.ToLowerInvariant() == "yaml" ||
                         (!trimmed.StartsWith("{") && !trimmed.StartsWith("["));

            var skill = isYaml
                ? _skillLoader.LoadFromYaml(skillDefinition)
                : _skillLoader.LoadFromJson(skillDefinition);

            if (skill == null)
            {
                return $"エラー: スキルの登録に失敗しました。{(isYaml ? "YAML" : "JSON")}の形式を確認してください。";
            }

            return $"スキル '{skill.Name}' を正常に登録しました";
        }
        catch (Exception ex)
        {
            return $"スキル登録エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// スキルを削除
    /// </summary>
    [KernelFunction("remove_skill")]
    [Description("登録されているスキルを削除します")]
    public string RemoveSkill(
        [Description("削除するスキル名")] string skillName)
    {
        if (_skillLoader.RemoveSkill(skillName))
        {
            return $"スキル '{skillName}' を削除しました";
        }
        return $"エラー: スキル '{skillName}' が見つかりません";
    }
}
