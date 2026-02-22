using Clowleash.Configuration;

namespace Clowleash.Security;

/// <summary>
/// PowerShellコマンドの検証を行うクラス
/// ホワイトリスト/ブラックリストモードでコマンドを検証する
/// </summary>
public class CommandValidator
{
    private readonly PowerShellSettings _settings;
    private readonly HashSet<string> _allowedCommands;
    private readonly HashSet<string> _deniedCommands;

    public CommandValidator(PowerShellSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // コマンドを正規化して保持（大文字小文字を無視）
        _allowedCommands = new HashSet<string>(
            settings.AllowedCommands.Select(NormalizeCommand),
            StringComparer.OrdinalIgnoreCase);

        _deniedCommands = new HashSet<string>(
            settings.DeniedCommands.Select(NormalizeCommand),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 現在のフィルターモードを取得
    /// </summary>
    public CommandFilterMode Mode => _settings.Mode;

    /// <summary>
    /// 指定されたコマンドが実行可能か検証する
    /// </summary>
    /// <param name="command">検証するコマンド（コマンドライン全体または単一コマンド）</param>
    /// <returns>許可されている場合はtrue</returns>
    public bool IsCommandAllowed(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        // コマンドラインから主要なコマンドを抽出
        var commands = ExtractCommands(command);

        foreach (var cmd in commands)
        {
            if (!IsSingleCommandAllowed(cmd))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 単一のコマンドが許可されているか検証
    /// </summary>
    private bool IsSingleCommandAllowed(string command)
    {
        var normalizedCmd = NormalizeCommand(command);

        // ブラックリストモード: 拒否リストにあれば不許可
        if (_settings.Mode == CommandFilterMode.Blacklist)
        {
            if (_deniedCommands.Contains(normalizedCmd))
            {
                return false;
            }

            // 拒否リストにない場合は許可
            return true;
        }

        // ホワイトリストモード: 許可リストにある場合のみ許可
        return _allowedCommands.Contains(normalizedCmd);
    }

    /// <summary>
    /// コマンドラインから主要なコマンドを抽出する
    /// </summary>
    private static List<string> ExtractCommands(string commandLine)
    {
        var commands = new List<string>();

        // パイプで区切られた複数コマンドを分割
        var parts = commandLine.Split('|', ';');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // 最初の単語をコマンドとして抽出
            var firstWord = GetFirstWord(trimmed);
            if (!string.IsNullOrEmpty(firstWord))
            {
                commands.Add(firstWord);
            }
        }

        return commands;
    }

    /// <summary>
    /// 文字列の最初の単語を取得
    /// </summary>
    private static string GetFirstWord(string text)
    {
        var span = text.AsSpan().TrimStart();
        var end = 0;

        while (end < span.Length && !char.IsWhiteSpace(span[end]))
        {
            end++;
        }

        return span[..end].ToString();
    }

    /// <summary>
    /// コマンド名を正規化する（エイリアス解決、小文字化など）
    /// </summary>
    private static string NormalizeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        // 先頭の特殊文字を除去（&記号など）
        var normalized = command.TrimStart('&', ' ', '\t');

        // 拡張子を除去（.exe, .ps1など）
        var dotIndex = normalized.IndexOf('.');
        if (dotIndex > 0)
        {
            normalized = normalized[..dotIndex];
        }

        return normalized.Trim();
    }

    /// <summary>
    /// 危険なパターンが含まれていないか追加検証
    /// </summary>
    public bool HasDangerousPatterns(string command)
    {
        var dangerousPatterns = new[]
        {
            // コマンドインジェクション
            "..\\",
            "../",
            // 環境変数の悪用
            "$env:",
            "${env:",
            // スクリプトブロックの悪用
            "[Scriptblock]",
            "Invoke-Expression",
            "IEX",
            "Invoke-Command",
            // リモート実行
            "Enter-PSSession",
            "Invoke-WebRequest",
            "curl",
            "wget",
            // レジストリ操作
            "HKLM:",
            "HKCU:",
            "Registry::",
            // プロセス操作
            "Stop-Process",
            "Kill-Process",
            "taskkill"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 検証結果の詳細情報を取得
    /// </summary>
    public CommandValidationResult Validate(string command)
    {
        var result = new CommandValidationResult
        {
            OriginalCommand = command,
            Mode = _settings.Mode,
            IsAllowed = IsCommandAllowed(command),
            HasDangerousPatterns = HasDangerousPatterns(command)
        };

        var extractedCommands = ExtractCommands(command);
        result.ExtractedCommands = extractedCommands;

        if (result.HasDangerousPatterns)
        {
            result.IsAllowed = false;
            result.ErrorMessage = "コマンドに危険なパターンが含まれています";
        }
        else if (!result.IsAllowed)
        {
            var modeDesc = _settings.Mode == CommandFilterMode.Whitelist ? "ホワイトリスト" : "ブラックリスト";
            result.ErrorMessage = $"コマンド '{string.Join(", ", extractedCommands)}' は{modeDesc}モードで許可されていません";
        }

        return result;
    }
}

/// <summary>
/// コマンド検証の結果
/// </summary>
public class CommandValidationResult
{
    public string OriginalCommand { get; set; } = string.Empty;
    public List<string> ExtractedCommands { get; set; } = new();
    public CommandFilterMode Mode { get; set; }
    public bool IsAllowed { get; set; }
    public bool HasDangerousPatterns { get; set; }
    public string? ErrorMessage { get; set; }

    public override string ToString()
    {
        return IsAllowed
            ? $"OK: {string.Join(", ", ExtractedCommands)}"
            : $"NG: {ErrorMessage}";
    }
}
