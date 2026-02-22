using System.ComponentModel;
using System.Text;
using Clawleash.Security;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// ファイル操作プラグイン
/// ファイルの作成・読み込み・編集・移動、フォルダ操作、ツリー表示などの機能を提供
/// </summary>
public class FileOperationsPlugin
{
    private readonly PathValidator _pathValidator;

    public FileOperationsPlugin(PathValidator pathValidator)
    {
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    #region ファイル作成

    [KernelFunction, Description("新しいファイルを作成し、内容を書き込みます")]
    public string CreateFile(
        [Description("作成するファイルのパス")] string filePath,
        [Description("ファイルに書き込む内容")] string content = "")
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への作成は許可されていません";
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content);
            var size = Encoding.UTF8.GetByteCount(content);
            return $"成功: ファイル '{filePath}' を作成しました ({size} バイト)";
        }
        catch (Exception ex)
        {
            return $"エラー: ファイルの作成に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("複数のファイルを一括で作成します")]
    public string CreateMultipleFiles(
        [Description("ファイルパスと内容のJSON配列。形式: [{\"path\": \"path1\", \"content\": \"content1\"}]")] string filesJson)
    {
        try
        {
            var files = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(filesJson);
            if (files == null || files.Count == 0)
            {
                return "エラー: ファイルリストが空です";
            }

            var results = new List<string>();
            foreach (var file in files)
            {
                if (!file.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                {
                    results.Add("スキップ: パスが指定されていません");
                    continue;
                }

                var content = file.TryGetValue("content", out var c) ? c : "";
                var result = CreateFile(path, content);
                results.Add($"  {path}: {(result.StartsWith("成功") ? "✅" : "❌")}");
            }

            return $"複数ファイル作成結果:\n{string.Join("\n", results)}";
        }
        catch (System.Text.Json.JsonException)
        {
            return "エラー: JSON形式が無効です";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region ファイル読み込み

    [KernelFunction, Description("ファイルの内容を読み込みます")]
    public string ReadFile(
        [Description("読み込むファイルのパス")] string filePath,
        [Description("開始行番号（1から開始、省略時は最初から）")] int? startLine = null,
        [Description("終了行番号（省略時は最後まで）")] int? endLine = null)
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
            var start = Math.Max(0, (startLine ?? 1) - 1);
            var end = Math.Min(lines.Length, endLine ?? lines.Length);

            var result = new StringBuilder();
            result.AppendLine($"ファイル: {filePath}");
            result.AppendLine($"全行数: {lines.Length}");
            result.AppendLine($"表示: {start + 1}行目 〜 {end}行目");
            result.AppendLine("---");

            for (int i = start; i < end; i++)
            {
                result.AppendLine($"{i + 1,4}: {lines[i]}");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: ファイルの読み込みに失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの最初のN行を取得します")]
    public string ReadFileHead(
        [Description("読み込むファイルのパス")] string filePath,
        [Description("読み込む行数（デフォルト: 10）")] int lines = 10)
    {
        return ReadFile(filePath, 1, lines);
    }

    [KernelFunction, Description("ファイルの最後のN行を取得します")]
    public string ReadFileTail(
        [Description("読み込むファイルのパス")] string filePath,
        [Description("読み込む行数（デフォルト: 10）")] int lines = 10)
    {
        if (!_pathValidator.IsPathAllowed(filePath))
        {
            return $"エラー: ファイル '{filePath}' へのアクセスは許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var allLines = File.ReadAllLines(filePath);
            var startLine = Math.Max(1, allLines.Length - lines + 1);
            return ReadFile(filePath, startLine, allLines.Length);
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイル内でテキストを検索します")]
    public string SearchInFile(
        [Description("検索するファイルのパス")] string filePath,
        [Description("検索パターン")] string pattern,
        [Description("大文字小文字を区別しない")] bool ignoreCase = true)
    {
        if (!_pathValidator.IsPathAllowed(filePath))
        {
            return $"エラー: ファイル '{filePath}' へのアクセスは許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var lines = File.ReadAllLines(filePath);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var matches = new List<(int LineNumber, string Line)>();

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(pattern, comparison))
                {
                    matches.Add((i + 1, lines[i]));
                }
            }

            if (matches.Count == 0)
            {
                return $"'{pattern}' は見つかりませんでした";
            }

            var result = new StringBuilder();
            result.AppendLine($"検索結果: '{pattern}' ({matches.Count}件)");
            result.AppendLine("---");

            foreach (var (lineNum, line) in matches.Take(50))
            {
                var truncated = line.Length > 100 ? line[..100] + "..." : line;
                result.AppendLine($"{lineNum,4}: {truncated}");
            }

            if (matches.Count > 50)
            {
                result.AppendLine($"... 他 {matches.Count - 50} 件");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region ファイル編集

    [KernelFunction, Description("ファイルの特定の行を置換します")]
    public string ReplaceLine(
        [Description("編集するファイルのパス")] string filePath,
        [Description("置換する行番号（1から開始）")] int lineNumber,
        [Description("新しい行の内容")] string newContent)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への編集は許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var lines = File.ReadAllLines(filePath);

            if (lineNumber < 1 || lineNumber > lines.Length)
            {
                return $"エラー: 行番号 {lineNumber} は無効です（1〜{lines.Length}の範囲で指定してください）";
            }

            var oldLine = lines[lineNumber - 1];
            lines[lineNumber - 1] = newContent;
            File.WriteAllLines(filePath, lines);

            return $"成功: {lineNumber}行目を置換しました\n" +
                   $"  旧: {oldLine}\n" +
                   $"  新: {newContent}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイル内のテキストを一括置換します")]
    public string ReplaceText(
        [Description("編集するファイルのパス")] string filePath,
        [Description("検索するテキスト")] string oldText,
        [Description("置換後のテキスト")] string newText,
        [Description("すべて置換するかどうか")] bool replaceAll = true)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への編集は許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var content = File.ReadAllText(filePath);
            var count = 0;

            if (replaceAll)
            {
                var oldCount = content.Split(oldText).Length - 1;
                content = content.Replace(oldText, newText);
                count = oldCount;
            }
            else
            {
                var index = content.IndexOf(oldText);
                if (index >= 0)
                {
                    content = content.Remove(index, oldText.Length).Insert(index, newText);
                    count = 1;
                }
            }

            if (count == 0)
            {
                return $"情報: '{oldText}' は見つかりませんでした";
            }

            File.WriteAllText(filePath, content);
            return $"成功: {count} 箇所を置換しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルに新しい行を挿入します")]
    public string InsertLine(
        [Description("編集するファイルのパス")] string filePath,
        [Description("挿入する行番号（1から開始、この行の前に挿入）")] int lineNumber,
        [Description("挿入する内容")] string content)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への編集は許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var lines = File.ReadAllLines(filePath).ToList();

            if (lineNumber < 1)
            {
                return $"エラー: 行番号は1以上で指定してください";
            }

            var insertIndex = Math.Min(lineNumber - 1, lines.Count);
            lines.Insert(insertIndex, content);
            File.WriteAllLines(filePath, lines);

            return $"成功: {insertIndex + 1}行目に新しい行を挿入しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの特定の行を削除します")]
    public string DeleteLine(
        [Description("編集するファイルのパス")] string filePath,
        [Description("削除する行番号（1から開始）")] int lineNumber)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への編集は許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var lines = File.ReadAllLines(filePath);

            if (lineNumber < 1 || lineNumber > lines.Length)
            {
                return $"エラー: 行番号 {lineNumber} は無効です（1〜{lines.Length}の範囲で指定してください）";
            }

            var deletedLine = lines[lineNumber - 1];
            var newLines = lines.Where((_, i) => i != lineNumber - 1).ToArray();
            File.WriteAllLines(filePath, newLines);

            return $"成功: {lineNumber}行目を削除しました\n" +
                   $"  削除内容: {deletedLine}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの末尾にテキストを追加します")]
    public string AppendToFile(
        [Description("編集するファイルのパス")] string filePath,
        [Description("追加する内容")] string content)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' への追加は許可されていません";
        }

        try
        {
            File.AppendAllText(filePath, content);
            return $"成功: ファイル '{filePath}' に内容を追加しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region ファイル移動・コピー

    [KernelFunction, Description("ファイルを移動します")]
    public string MoveFile(
        [Description("移動元のファイルパス")] string sourcePath,
        [Description("移動先のファイルパス")] string destinationPath,
        [Description("上書きするかどうか")] bool overwrite = false)
    {
        if (!_pathValidator.IsPathAllowed(sourcePath))
        {
            return $"エラー: ソース '{sourcePath}' へのアクセスは許可されていません";
        }

        if (!_pathValidator.IsWritablePath(destinationPath))
        {
            return $"エラー: 移動先 '{destinationPath}' への書き込みは許可されていません";
        }

        try
        {
            if (!File.Exists(sourcePath))
            {
                return $"エラー: ファイル '{sourcePath}' が見つかりません";
            }

            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Move(sourcePath, destinationPath, overwrite);
            return $"成功: ファイルを移動しました\n" +
                   $"  {sourcePath}\n" +
                   $"  → {destinationPath}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルをコピーします")]
    public string CopyFile(
        [Description("コピー元のファイルパス")] string sourcePath,
        [Description("コピー先のファイルパス")] string destinationPath,
        [Description("上書きするかどうか")] bool overwrite = false)
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
            if (!File.Exists(sourcePath))
            {
                return $"エラー: ファイル '{sourcePath}' が見つかりません";
            }

            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destinationPath, overwrite);
            return $"成功: ファイルをコピーしました\n" +
                   $"  {sourcePath}\n" +
                   $"  → {destinationPath}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの名前を変更します")]
    public string RenameFile(
        [Description("現在のファイルパス")] string currentPath,
        [Description("新しいファイル名（パスではなく名前のみ）")] string newName)
    {
        var directory = Path.GetDirectoryName(currentPath);
        var newPath = string.IsNullOrEmpty(directory) ? newName : Path.Combine(directory, newName);
        return MoveFile(currentPath, newPath);
    }

    #endregion

    #region ファイル削除

    [KernelFunction, Description("ファイルを削除します")]
    public string DeleteFile(
        [Description("削除するファイルのパス")] string filePath)
    {
        if (!_pathValidator.IsWritablePath(filePath))
        {
            return $"エラー: ファイル '{filePath}' の削除は許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            File.Delete(filePath);
            return $"成功: ファイル '{filePath}' を削除しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region フォルダ操作

    [KernelFunction, Description("新しいフォルダを作成します")]
    public string CreateFolder(
        [Description("作成するフォルダのパス")] string folderPath)
    {
        if (!_pathValidator.IsWritablePath(folderPath))
        {
            return $"エラー: フォルダ '{folderPath}' への作成は許可されていません";
        }

        try
        {
            if (Directory.Exists(folderPath))
            {
                return $"情報: フォルダ '{folderPath}' は既に存在します";
            }

            Directory.CreateDirectory(folderPath);
            return $"成功: フォルダ '{folderPath}' を作成しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("フォルダを移動します")]
    public string MoveFolder(
        [Description("移動元のフォルダパス")] string sourcePath,
        [Description("移動先のフォルダパス")] string destinationPath)
    {
        if (!_pathValidator.IsPathAllowed(sourcePath))
        {
            return $"エラー: ソース '{sourcePath}' へのアクセスは許可されていません";
        }

        if (!_pathValidator.IsWritablePath(destinationPath))
        {
            return $"エラー: 移動先 '{destinationPath}' への書き込みは許可されていません";
        }

        try
        {
            if (!Directory.Exists(sourcePath))
            {
                return $"エラー: フォルダ '{sourcePath}' が見つかりません";
            }

            if (Directory.Exists(destinationPath))
            {
                return $"エラー: 移動先 '{destinationPath}' は既に存在します";
            }

            Directory.Move(sourcePath, destinationPath);
            return $"成功: フォルダを移動しました\n" +
                   $"  {sourcePath}\n" +
                   $"  → {destinationPath}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("フォルダをコピーします")]
    public string CopyFolder(
        [Description("コピー元のフォルダパス")] string sourcePath,
        [Description("コピー先のフォルダパス")] string destinationPath)
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
            if (!Directory.Exists(sourcePath))
            {
                return $"エラー: フォルダ '{sourcePath}' が見つかりません";
            }

            CopyDirectoryRecursive(sourcePath, destinationPath);

            var fileCount = Directory.GetFiles(destinationPath, "*", SearchOption.AllDirectories).Length;
            return $"成功: フォルダをコピーしました ({fileCount} ファイル)\n" +
                   $"  {sourcePath}\n" +
                   $"  → {destinationPath}";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("フォルダの名前を変更します")]
    public string RenameFolder(
        [Description("現在のフォルダパス")] string currentPath,
        [Description("新しいフォルダ名（パスではなく名前のみ）")] string newName)
    {
        var parentDir = Directory.GetParent(currentPath)?.FullName;
        var newPath = string.IsNullOrEmpty(parentDir) ? newName : Path.Combine(parentDir, newName);
        return MoveFolder(currentPath, newPath);
    }

    [KernelFunction, Description("フォルダを削除します")]
    public string DeleteFolder(
        [Description("削除するフォルダのパス")] string folderPath,
        [Description("中身ごと削除するかどうか")] bool recursive = false)
    {
        if (!_pathValidator.IsWritablePath(folderPath))
        {
            return $"エラー: フォルダ '{folderPath}' の削除は許可されていません";
        }

        try
        {
            if (!Directory.Exists(folderPath))
            {
                return $"エラー: フォルダ '{folderPath}' が見つかりません";
            }

            Directory.Delete(folderPath, recursive);
            return $"成功: フォルダ '{folderPath}' を削除しました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region ツリー表示

    [KernelFunction, Description("ディレクトリ構造をツリー形式で表示します")]
    public string ShowTree(
        [Description("表示するディレクトリパス")] string directoryPath,
        [Description("最大深度（デフォルト: 3）")] int maxDepth = 3,
        [Description("表示する最大ファイル数")] int maxFiles = 100)
    {
        if (!_pathValidator.IsPathAllowed(directoryPath))
        {
            return $"エラー: ディレクトリ '{directoryPath}' へのアクセスは許可されていません";
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return $"エラー: ディレクトリ '{directoryPath}' が見つかりません";
            }

            var result = new StringBuilder();
            var fileCount = 0;
            var dirCount = 0;

            result.AppendLine(directoryPath);

            BuildTree(result, directoryPath, "", maxDepth, ref fileCount, ref dirCount, maxFiles);

            result.AppendLine();
            result.AppendLine($"{dirCount} ディレクトリ, {fileCount} ファイル");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    private void BuildTree(
        StringBuilder sb,
        string path,
        string indent,
        int depth,
        ref int fileCount,
        ref int dirCount,
        int maxFiles)
    {
        if (depth <= 0 || fileCount >= maxFiles) return;

        try
        {
            var directories = Directory.GetDirectories(path).OrderBy(d => d).ToArray();
            var files = Directory.GetFiles(path).OrderBy(f => f).ToArray();

            for (int i = 0; i < directories.Length && fileCount < maxFiles; i++)
            {
                var dir = directories[i];
                var name = Path.GetFileName(dir);
                var isLast = (i == directories.Length - 1) && files.Length == 0;

                sb.AppendLine($"{indent}{(isLast ? "└── " : "├── ")}📁 {name}/");
                dirCount++;

                var newIndent = indent + (isLast ? "    " : "│   ");
                BuildTree(sb, dir, newIndent, depth - 1, ref fileCount, ref dirCount, maxFiles);
            }

            for (int i = 0; i < files.Length && fileCount < maxFiles; i++)
            {
                var file = files[i];
                var name = Path.GetFileName(file);
                var isLast = i == files.Length - 1;

                var info = new FileInfo(file);
                var size = FormatFileSize(info.Length);
                var icon = GetFileIcon(info.Extension);

                sb.AppendLine($"{indent}{(isLast ? "└── " : "├── ")}{icon} {name} ({size})");
                fileCount++;
            }

            if (fileCount >= maxFiles)
            {
                sb.AppendLine($"{indent}... (最大ファイル数に達しました)");
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}└── [アクセス拒否]");
        }
    }

    private static string GetFileIcon(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "🔷",
            ".js" or ".ts" => "💛",
            ".py" => "🐍",
            ".json" => "📋",
            ".xml" => "📄",
            ".html" or ".htm" => "🌐",
            ".css" => "🎨",
            ".md" => "📝",
            ".txt" => "📃",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼️",
            ".pdf" => "📕",
            ".zip" or ".rar" or ".7z" => "📦",
            ".exe" or ".dll" => "⚙️",
            ".sln" or ".csproj" => "🔧",
            _ => "📄"
        };
    }

    #endregion

    #region ファイル情報

    [KernelFunction, Description("ファイルまたはフォルダの詳細情報を取得します")]
    public string GetFileInfo(
        [Description("情報を取得するパス")] string path)
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
                    📄 ファイル情報
                    ─────────────────────
                    名前: {info.Name}
                    パス: {info.FullName}
                    サイズ: {FormatFileSize(info.Length)}
                    拡張子: {info.Extension}
                    作成日時: {info.CreationTime:yyyy-MM-dd HH:mm:ss}
                    更新日時: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                    アクセス日時: {info.LastAccessTime:yyyy-MM-dd HH:mm:ss}
                    読み取り専用: {(info.IsReadOnly ? "はい" : "いいえ")}
                    隠しファイル: {(info.Attributes.HasFlag(FileAttributes.Hidden) ? "はい" : "いいえ")}
                    """;
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var files = info.GetFiles();
                var dirs = info.GetDirectories();
                var totalSize = files.Sum(f => f.Length);

                return $"""
                    📁 フォルダ情報
                    ─────────────────────
                    名前: {info.Name}
                    パス: {info.FullName}
                    ファイル数: {files.Length}
                    サブフォルダ数: {dirs.Length}
                    合計サイズ: {FormatFileSize(totalSize)}
                    作成日時: {info.CreationTime:yyyy-MM-dd HH:mm:ss}
                    更新日時: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                    隠しフォルダ: {(info.Attributes.HasFlag(FileAttributes.Hidden) ? "はい" : "いいえ")}
                    """;
            }

            return $"エラー: '{path}' が見つかりません";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("ファイルの行数・文字数・単語数をカウントします")]
    public string CountFile(
        [Description("カウントするファイルのパス")] string filePath)
    {
        if (!_pathValidator.IsPathAllowed(filePath))
        {
            return $"エラー: ファイル '{filePath}' へのアクセスは許可されていません";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return $"エラー: ファイル '{filePath}' が見つかりません";
            }

            var content = File.ReadAllText(filePath);
            var lines = File.ReadAllLines(filePath);
            var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            return $"""
                📊 ファイル統計: {Path.GetFileName(filePath)}
                ─────────────────────
                行数: {lines.Length:N0}
                文字数: {content.Length:N0}
                単語数: {words.Length:N0}
                バイト数: {Encoding.UTF8.GetByteCount(content):N0}
                """;
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    #endregion

    #region ユーティリティ

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destDir);
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

    #endregion
}
