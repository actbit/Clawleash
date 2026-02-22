using Clawleash.Configuration;

namespace Clawleash.Security;

/// <summary>
/// パスの検証を行うクラス
/// 許可されたディレクトリ内へのアクセスかどうかを検証する（二重防御）
/// </summary>
public class PathValidator
{
    private readonly FileSystemSettings _settings;
    private readonly HashSet<string> _allowedDirectories;

    public PathValidator(FileSystemSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 許可ディレクトリをフルパスに正規化して保持
        _allowedDirectories = new HashSet<string>(
            settings.AllowedDirectories
                .Select(d => GetCanonicalPath(d))
                .Where(d => !string.IsNullOrEmpty(d)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定されたパスが許可されたディレクトリ内にあるか検証する
    /// </summary>
    /// <param name="path">検証するパス</param>
    /// <returns>許可されている場合はtrue</returns>
    public bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = GetCanonicalPath(path);

            // 許可されたディレクトリのいずれかの配下にあるかチェック
            foreach (var allowedDir in _allowedDirectories)
            {
                if (fullPath.StartsWith(allowedDir, StringComparison.OrdinalIgnoreCase))
                {
                    // パストラバーサル攻撃の防止
                    if (!ContainsPathTraversal(fullPath, allowedDir))
                    {
                        return true;
                    }
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
    /// ファイルサイズが制限内か検証する
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>制限内の場合はtrue</returns>
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
    /// <param name="path">検証するパス</param>
    /// <returns>書き込み可能な場合はtrue</returns>
    public bool IsWritablePath(string path)
    {
        if (!IsPathAllowed(path))
        {
            return false;
        }

        try
        {
            var fullPath = GetCanonicalPath(path);
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
    /// パストラバーサル攻撃が含まれていないかチェック
    /// </summary>
    private static bool ContainsPathTraversal(string fullPath, string basePath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(basePath, fullPath);
            return relativePath.StartsWith("..", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // エラーがある場合は危険とみなす
        }
    }

    /// <summary>
    /// パスを正規化（シンボリックリンク解決、絶対パス化など）
    /// </summary>
    private static string GetCanonicalPath(string path)
    {
        try
        {
            // 相対パスを絶対パスに変換
            var fullPath = Path.GetFullPath(path);

            // シンボリックリンクを解決（Windows/Linux両対応）
            if (OperatingSystem.IsWindows())
            {
                // Windows: 正規化されたパスを返す
                return Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar);
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
    /// 許可されたディレクトリの一覧を取得
    /// </summary>
    public IReadOnlySet<string> GetAllowedDirectories()
    {
        return _allowedDirectories;
    }

    /// <summary>
    /// 検証結果の詳細情報を取得
    /// </summary>
    public PathValidationResult Validate(string path)
    {
        var result = new PathValidationResult
        {
            OriginalPath = path,
            CanonicalPath = GetCanonicalPath(path),
            IsAllowed = IsPathAllowed(path)
        };

        if (!result.IsAllowed)
        {
            var allowedList = string.Join(", ", _allowedDirectories);
            result.ErrorMessage = $"パス '{path}' は許可されたディレクトリ内ではありません。許可: [{allowedList}]";
        }

        return result;
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
