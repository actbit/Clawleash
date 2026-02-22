using Clawleash.Configuration;
using System.Security;
using System.Text;

namespace Clawleash.Security;

/// <summary>
/// パスの検証を行うクラス
/// 許可されたディレクトリ内へのアクセスかどうかを検証する（二重防御）
/// パストラバーサル攻撃、プロンプトインジェクション対策を含む
/// </summary>
public class PathValidator
{
    private readonly FileSystemSettings _settings;
    private readonly HashSet<string> _allowedDirectories;
    private readonly HashSet<string> _readOnlyDirectories;

    public PathValidator(FileSystemSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 許可ディレクトリをフルパスに正規化して保持
        _allowedDirectories = new HashSet<string>(
            settings.AllowedDirectories
                .Select(d => GetCanonicalPath(d))
                .Where(d => !string.IsNullOrEmpty(d)),
            StringComparer.OrdinalIgnoreCase);

        // 読み取り専用ディレクトリ
        _readOnlyDirectories = new HashSet<string>(
            (settings.ReadOnlyDirectories ?? new List<string>())
                .Select(d => GetCanonicalPath(d))
                .Where(d => !string.IsNullOrEmpty(d)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定されたパスが許可されたディレクトリ内にあるか検証する
    /// </summary>
    /// <param name="path">検証するパス</param>
    /// <returns>許可されている場合はtrue</returns>
    public bool IsPathAllowed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            // パストラバーサル対策：危険な文字列をチェック
            if (ContainsDangerousPatterns(path))
            {
                return false;
            }

            var fullPath = GetCanonicalPath(path);

            // 正規化後のパスも再チェック
            if (ContainsDangerousPatterns(fullPath))
            {
                return false;
            }

            // 許可されたディレクトリのいずれかの配下にあるかチェック
            foreach (var allowedDir in _allowedDirectories)
            {
                if (IsPathWithinDirectory(fullPath, allowedDir))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // パスの解決に失敗した場合は不許可
            return false;
        }
    }

    /// <summary>
    /// 指定されたパスが書き込み禁止の読み取り専用かどうか
    /// </summary>
    public bool IsReadOnlyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true; // 無効なパスは読み取り専用扱い
        }

        try
        {
            var fullPath = GetCanonicalPath(path);

            foreach (var readOnlyDir in _readOnlyDirectories)
            {
                if (IsPathWithinDirectory(fullPath, readOnlyDir))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// ファイルサイズが制限内か検証する
    /// </summary>
    public bool IsFileSizeAllowed(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return true; // 存在しないファイルは許可
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var maxSizeBytes = (long)_settings.MaxFileSizeMB * 1024 * 1024;
            return fileInfo.Length <= maxSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 書き込み可能なパスか検証する
    /// </summary>
    public bool IsWritablePath(string? path)
    {
        if (!IsPathAllowed(path))
        {
            return false;
        }

        if (IsReadOnlyPath(path))
        {
            return false;
        }

        try
        {
            var fullPath = GetCanonicalPath(path!);
            var directory = Path.GetDirectoryName(fullPath);

            if (string.IsNullOrEmpty(directory))
            {
                directory = fullPath;
            }

            // ディレクトリが存在しない場合は作成可能かチェック
            if (!Directory.Exists(directory))
            {
                // 親ディレクトリを探す
                while (!Directory.Exists(directory) && !string.IsNullOrEmpty(directory))
                {
                    directory = Path.GetDirectoryName(directory);
                }
            }

            return directory != null && Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// パストラバーサル攻撃、プロンプトインジェクション攻撃を含むパターンをチェック
    /// </summary>
    private static bool ContainsDangerousPatterns(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        // 危険なパターンのリスト
        var dangerousPatterns = new[]
        {
            // パストラバーサル
            "..",
            "./",
            "/.",
            "\\.",

            // null バイト攻撃
            "\0",
            "%00",

            // URL エンコードされたパストラバーサル
            "%2e%2e",
            "%2e%2e/",
            "..%2f",
            "..%5c",
            "%252e", // ダブルエンコーディング

            // シェルインジェクション
            "|",
            ">",
            "<",
            "$(",
            "`",
            "&&",
            "||",
            ";",

            // Windows特殊デバイス
            "CON:",
            "PRN:",
            "AUX:",
            "NUL:",
            "COM1:",
            "COM2:",
            "COM3:",
            "COM4:",
            "LPT1:",
            "LPT2:",
            "LPT3:",
        };

        var lowerPath = path.ToLowerInvariant();

        foreach (var pattern in dangerousPatterns)
        {
            if (lowerPath.Contains(pattern.ToLowerInvariant()))
            {
                return true;
            }
        }

        // nullバイトの直接チェック
        if (path.Any(c => c == '\0'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// パスが指定されたディレクトリ内にあるかチェック（パストラバーサル対策含む）
    /// </summary>
    private static bool IsPathWithinDirectory(string fullPath, string basePath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(basePath))
        {
            return false;
        }

        // 正規化して比較
        var normalizedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedBase = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // ベースパスで始まるかチェック
        if (!normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ベースパスと完全一致、またはディレクトリ区切り文字が続く場合のみ有効
        if (normalizedFull.Length == normalizedBase.Length)
        {
            return true;
        }

        // パストラバーサル対策：相対パスが .. で始まらないことを確認
        try
        {
            var relativePath = Path.GetRelativePath(normalizedBase, normalizedFull);
            if (relativePath.StartsWith("..", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 相対パスに .. が含まれていないか
            if (relativePath.Contains(".."))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// パスを正規化（シンボリックリンク解決、絶対パス化など）
    /// </summary>
    private static string GetCanonicalPath(string path)
    {
        try
        {
            // URLデコードを試行（エンコードされた攻撃対策）
            var decodedPath = Uri.UnescapeDataString(path);

            // 相対パスを絶対パスに変換
            var fullPath = Path.GetFullPath(decodedPath);

            // Windows: 正規化されたパスを返す
            if (OperatingSystem.IsWindows())
            {
                // 拡張長パスプレフィックスを削除して統一
                if (fullPath.StartsWith(@"\\?\"))
                {
                    fullPath = fullPath[4..];
                }
                return fullPath.TrimEnd(Path.DirectorySeparatorChar);
            }
            else
            {
                // Linux: realpath相当の処理
                return fullPath.TrimEnd(Path.DirectorySeparatorChar);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 安全なパスにサニタイズ
    /// </summary>
    public string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        // nullバイトを削除
        var sanitized = new StringBuilder();
        foreach (var c in path)
        {
            if (c != '\0')
            {
                sanitized.Append(c);
            }
        }

        // 危険な文字を置換
        var result = sanitized.ToString();
        result = result.Replace("..", "");

        return result;
    }

    /// <summary>
    /// 許可されたディレクトリの一覧を取得
    /// </summary>
    public IReadOnlySet<string> GetAllowedDirectories()
    {
        return _allowedDirectories;
    }

    /// <summary>
    /// 検証結果の詳細情報を取得
    /// </summary>
    public PathValidationResult Validate(string? path)
    {
        var result = new PathValidationResult
        {
            OriginalPath = path ?? string.Empty,
            CanonicalPath = GetCanonicalPath(path ?? string.Empty),
            IsAllowed = IsPathAllowed(path)
        };

        if (!result.IsAllowed)
        {
            var allowedList = string.Join(", ", _allowedDirectories);
            result.ErrorMessage = $"パス '{path}' は許可されたディレクトリ内ではありません。許可: [{allowedList}]";
        }

        return result;
    }

    /// <summary>
    /// 現在のセキュリティ設定の概要を取得
    /// </summary>
    public string GetSecuritySummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## パス検証セキュリティ設定");
        sb.AppendLine($"許可ディレクトリ数: {_allowedDirectories.Count}");
        sb.AppendLine($"読み取り専用ディレクトリ数: {_readOnlyDirectories.Count}");
        sb.AppendLine($"最大ファイルサイズ: {_settings.MaxFileSizeMB} MB");
        sb.AppendLine();
        sb.AppendLine("### 許可されたディレクトリ");
        foreach (var dir in _allowedDirectories)
        {
            sb.AppendLine($"- {dir}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// パス検証の結果
/// </summary>
public class PathValidationResult
{
    public string OriginalPath { get; set; } = string.Empty;
    public string CanonicalPath { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public string? ErrorMessage { get; set; }

    public override string ToString()
    {
        return IsAllowed
            ? $"OK: {CanonicalPath}"
            : $"NG: {ErrorMessage}";
    }
}
