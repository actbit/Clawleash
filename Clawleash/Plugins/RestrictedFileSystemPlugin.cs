using System.ComponentModel;
using System.Text;
using Clawleash.Security;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// 制限付きファイルシステム操作プラグイン
/// 許可されたディレクトリ内でのみファイル操作を許可
/// </summary>
public class RestrictedFileSystemPlugin
{
    private readonly PathValidator _pathValidator;

    public RestrictedFileSystemPlugin(PathValidator pathValidator)
    {
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    [KernelFunction, Description("指定されたディレクトリ内のファイルとフォルダの一覧を取得します")]
    public string ListFiles(
        [Description("一覧を取得するディレクトリパス")] string directoryPath,
        [Description("検索パターン（オプション、例: *.txt）")] string? searchPattern = null)
    {
        if (!_pathValidator.IsPathAllowed(directoryPath))
        {
            return $"エラー: ディレクトリ '{directoryPath}' へのアクセスは許可されていません";
        }

        try
        {
            var dir = new DirectoryInfo(directoryPath);

            if (!dir.Exists)
            {
                return $"エラー: ディレクトリ '{directoryPath}' が見つかりません";
            }

            var result = new StringBuilder();
            result.AppendLine($"Directory: {directoryPath}");
            result.AppendLine("---");

            // サブディレクトリ
            var subDirs = dir.GetDirectories();
            if (subDirs.Length > 0)
            {
                result.AppendLine("Directories:");
                foreach (var subDir in subDirs)
                {
                    result.AppendLine($"  [DIR] {subDir.Name}");
                }
            }

            // ファイル
            var files = string.IsNullOrEmpty(searchPattern)
                ? dir.GetFiles()
                : dir.GetFiles(searchPattern);

            if (files.Length > 0)
            {
                result.AppendLine("Files:");
                foreach (var file in files)
                {
                    var size = FormatFileSize(file.Length);
                    result.AppendLine($"  {file.Name} ({size})");
                }
            }

            result.AppendLine($"\nTotal: {subDirs.Length} directories, {files.Length} files");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの内容を読み取ります")]
    public string ReadFile(
        [Description("読み取るファイルのパス")] string filePath,
        [Description("読み取る最大行数（オプション）")] int? maxLines = null)
    {
        if (!_pathValidator.IsPathAllowed(filePath))
        {
            return $"エラー: ファイル '{filePath}' へのアクセスは許可されていません";
        }

        if (!_pathValidator.IsFileSizeAllowed(filePath))
        {
            return $"エラー: ファイルサイズが制限を超えています";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var lines = File.ReadAllLines(filePath);

            var result = new StringBuilder();
            result.AppendLine($"File: {filePath}");
            result.AppendLine($"Lines: {lines.Length}");
            result.AppendLine("---");

            var displayLines = maxLines.HasValue && maxLines.Value < lines.Length
                ? lines.Take(maxLines.Value)
                : lines;

            foreach (var line in displayLines)
            {
                result.AppendLine(line);
            }

            if (maxLines.HasValue && lines.Length > maxLines.Value)
            {
                result.AppendLine($"... ({lines.Length - maxLines.Value} more lines)");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルに内容を書き込みます")]
    public string WriteFile(
        [Description("書き込むファイルのパス")] string filePath,
        [Description("書き込む内容")] string content,
        [Description("追記モード（true）または上書きモード（false）")] bool append = false)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への書き込みは許可されていません";
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (append)
            {
                File.AppendAllText(filePath, content);
            }
            else
            {
                File.WriteAllText(filePath, content);
            }

            var size = Encoding.UTF8.GetByteCount(content);
            return $"成功: '{filePath}' に {size} バイトを書き込みました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルまたはディレクトリを削除します")]
    public string DeleteFile(
        [Description("削除するファイルまたはディレクトリのパス")] string path,
        [Description("ディレクトリの場合、再帰的に削除するかどうか")] bool recursive = false)
    {
        if (!_pathValidator.IsPathAllowed(path))
        {
            return $"エラー: '{path}' へのアクセスは許可されていません";
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return $"成功: ファイル '{path}' を削除しました";
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
                return $"成功: ディレクトリ '{path}' を削除しました";
            }

            return $"エラー: '{path}' が見つかりません";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルまたはディレクトリの情報を取得します")]
    public string GetFileInfo([Description("情報を取得するパス")] string path)
    {
        if (!_pathValidator.IsPathAllowed(path))
        {
            return $"エラー: '{path}' へのアクセスは許可されていません";
        }

        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return $"""
                    File: {info.FullName}
                    Size: {FormatFileSize(info.Length)}
                    Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}
                    Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                    Extension: {info.Extension}
                    ReadOnly: {info.IsReadOnly}
                    """;
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var fileCount = info.GetFiles().Length;
                var dirCount = info.GetDirectories().Length;

                return $"""
                    Directory: {info.FullName}
                    Files: {fileCount}
                    Subdirectories: {dirCount}
                    Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}
                    Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                    """;
            }

            return $"エラー: '{path}' が見つかりません";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルまたはディレクトリをコピーまたは移動します")]
    public string CopyOrMove(
        [Description("元のパス")] string sourcePath,
        [Description("コピー先のパス")] string destinationPath,
        [Description("移動する場合はtrue、コピーする場合はfalse")] bool move = false)
    {
        if (!_pathValidator.IsPathAllowed(sourcePath))
        {
            return $"エラー: ソース '{sourcePath}' へのアクセスは許可されていません";
        }

        if (!_pathValidator.IsWritablePath(destinationPath))
        {
            return $"エラー: コピー先 '{destinationPath}' への書き込みは許可されていません";
        }

        try
        {
            if (File.Exists(sourcePath))
            {
                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (move)
                {
                    File.Move(sourcePath, destinationPath, true);
                    return $"成功: ファイルを '{sourcePath}' から '{destinationPath}' に移動しました";
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, true);
                    return $"成功: ファイルを '{sourcePath}' から '{destinationPath}' にコピーしました";
                }
            }

            if (Directory.Exists(sourcePath))
            {
                if (move)
                {
                    Directory.Move(sourcePath, destinationPath);
                    return $"成功: ディレクトリを '{sourcePath}' から '{destinationPath}' に移動しました";
                }
                else
                {
                    CopyDirectory(sourcePath, destinationPath);
                    return $"成功: ディレクトリを '{sourcePath}' から '{destinationPath}' にコピーしました";
                }
            }

            return $"エラー: '{sourcePath}' が見つかりません";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("許可されたディレクトリの一覧を取得します")]
    public string GetAllowedDirectories()
    {
        var allowedDirs = _pathValidator.GetAllowedDirectories();

        if (allowedDirs.Count == 0)
        {
            return "許可されたディレクトリはありません";
        }

        var result = new StringBuilder("許可されたディレクトリ:\n");
        foreach (var dir in allowedDirs)
        {
            var exists = Directory.Exists(dir);
            result.AppendLine($"  {dir} {(exists ? "[存在]" : "[未作成]")}");
        }

        return result.ToString();
    }

    private static void CopyDirectory(string source, string destination)
    {
        var dir = new DirectoryInfo(source);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {source}");
        }

        Directory.CreateDirectory(destination);

        foreach (var file in dir.GetFiles())
        {
            var targetPath = Path.Combine(destination, file.Name);
            file.CopyTo(targetPath, true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var targetPath = Path.Combine(destination, subDir.Name);
            CopyDirectory(subDir.FullName, targetPath);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        var i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:0.##} {suffixes[i]}";
    }
}
