using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Clawleash.Tools;

/// <summary>
/// ツールパッケージのロード・管理を行う
/// ZIPファイルからDLLを展開し、プロキシを生成してKernelに登録
/// </summary>
public class ToolLoader : IDisposable
{
    private readonly ILogger<ToolLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IToolExecutor _executor;
    private readonly ToolProxyGenerator _proxyGenerator;
    private readonly string _toolsDirectory;
    private readonly Dictionary<string, LoadedTool> _loadedTools = new();
    private readonly Dictionary<string, ToolPackage> _packages = new();
    private bool _disposed;

    public IReadOnlyDictionary<string, LoadedTool> LoadedTools => _loadedTools;

    public ToolLoader(
        ILoggerFactory loggerFactory,
        IToolExecutor executor,
        string? toolsDirectory = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ToolLoader>();
        _executor = executor;
        _proxyGenerator = new ToolProxyGenerator(loggerFactory.CreateLogger<ToolProxyGenerator>());
        _toolsDirectory = toolsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clawleash", "Tools");
    }

    /// <summary>
    /// ZIPファイルからツールをロード
    /// </summary>
    public async Task<LoadedTool?> LoadFromZipAsync(string zipPath, Kernel kernel)
    {
        if (!File.Exists(zipPath))
        {
            _logger.LogError("ZIPファイルが見つかりません: {Path}", zipPath);
            return null;
        }

        var toolName = Path.GetFileNameWithoutExtension(zipPath);
        if (_loadedTools.ContainsKey(toolName))
        {
            _logger.LogWarning("ツールは既にロード済み: {Name}", toolName);
            return _loadedTools[toolName];
        }

        try
        {
            _logger.LogInformation("ツールをロード中: {Path}", zipPath);

            // 展開先ディレクトリ
            var extractPath = Path.Combine(_toolsDirectory, toolName);
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }
            Directory.CreateDirectory(extractPath);

            // ZIPを展開
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            // マニフェストを読み込み
            var manifest = await LoadManifestAsync(extractPath);

            // DLLを見つける
            var dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.TopDirectoryOnly);
            if (dllFiles.Length == 0)
            {
                _logger.LogError("DLLファイルが見つかりません: {Path}", extractPath);
                return null;
            }

            // メインDLLを特定（マニフェストまたは最初のDLL）
            var mainDll = !string.IsNullOrEmpty(manifest?.MainAssembly)
                ? Path.Combine(extractPath, manifest.MainAssembly)
                : dllFiles[0];

            if (!File.Exists(mainDll))
            {
                mainDll = dllFiles[0];
            }

            // パッケージを作成してロード
            var package = new ToolPackage(
                manifest?.Name ?? toolName,
                manifest?.Version ?? "1.0.0",
                mainDll,
                _loggerFactory.CreateLogger<ToolPackage>());

            if (!package.Load())
            {
                _logger.LogError("パッケージのロードに失敗: {Name}", toolName);
                return null;
            }

            _packages[toolName] = package;

            // プロキシを生成してインスタンスを作成
            var proxyInstances = new List<object>();
            foreach (var toolType in package.ToolTypes)
            {
                var proxyType = _proxyGenerator.GenerateProxyType(toolType, _executor);
                var instance = Activator.CreateInstance(proxyType, _executor);
                if (instance != null)
                {
                    proxyInstances.Add(instance);

                    // Kernel にプラグインとして登録 (実行時型を使用)
                    var pluginName = $"{package.Name}_{toolType.TypeName}";
                    var plugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromObject(
                        instance,
                        pluginName);
                    kernel.Plugins.Add(plugin);
                    _logger.LogInformation("Kernel に登録: {PluginName}", pluginName);
                }
            }

            var loadedTool = new LoadedTool
            {
                Name = package.Name,
                Version = package.Version,
                Path = extractPath,
                Package = package,
                ProxyInstances = proxyInstances
            };

            _loadedTools[toolName] = loadedTool;
            _logger.LogInformation("ツールロード完了: {Name} v{Version}", package.Name, package.Version);

            return loadedTool;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ツールロードエラー: {Path}", zipPath);
            return null;
        }
    }

    /// <summary>
    /// DLLファイルから直接ツールをロード
    /// </summary>
    public async Task<LoadedTool?> LoadFromDllAsync(string dllPath, Kernel kernel)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("DLLファイルが見つかりません: {Path}", dllPath);
            return null;
        }

        var toolName = Path.GetFileNameWithoutExtension(dllPath);
        if (_loadedTools.ContainsKey(toolName))
        {
            _logger.LogWarning("ツールは既にロード済み: {Name}", toolName);
            return _loadedTools[toolName];
        }

        try
        {
            var package = new ToolPackage(
                toolName,
                "1.0.0",
                dllPath,
                _loggerFactory.CreateLogger<ToolPackage>());

            if (!package.Load())
            {
                return null;
            }

            _packages[toolName] = package;

            var proxyInstances = new List<object>();
            foreach (var toolType in package.ToolTypes)
            {
                var proxyType = _proxyGenerator.GenerateProxyType(toolType, _executor);
                var instance = Activator.CreateInstance(proxyType, _executor);
                if (instance != null)
                {
                    proxyInstances.Add(instance);
                    var pluginName = $"{package.Name}_{toolType.TypeName}";
                    var plugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromObject(
                        instance,
                        pluginName);
                    kernel.Plugins.Add(plugin);
                }
            }

            var loadedTool = new LoadedTool
            {
                Name = package.Name,
                Version = package.Version,
                Path = dllPath,
                Package = package,
                ProxyInstances = proxyInstances
            };

            _loadedTools[toolName] = loadedTool;
            return loadedTool;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DLL ロードエラー: {Path}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// マニフェストを読み込み
    /// </summary>
    private static async Task<ToolManifest?> LoadManifestAsync(string directory)
    {
        var manifestPath = Path.Combine(directory, "tool-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            return System.Text.Json.JsonSerializer.Deserialize<ToolManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ツールをアンロード
    /// </summary>
    public bool Unload(string toolName)
    {
        if (!_loadedTools.TryGetValue(toolName, out var loadedTool))
        {
            return false;
        }

        try
        {
            loadedTool.Package.Unload();
            _loadedTools.Remove(toolName);
            _packages.Remove(toolName);

            // 展開ディレクトリを削除
            if (Directory.Exists(loadedTool.Path))
            {
                Directory.Delete(loadedTool.Path, true);
            }

            _logger.LogInformation("ツールをアンロード: {Name}", toolName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ツールアンロードエラー: {Name}", toolName);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var tool in _loadedTools.Keys.ToList())
        {
            Unload(tool);
        }

        _disposed = true;
    }
}

/// <summary>
/// ロード済みツール情報
/// </summary>
public class LoadedTool
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ToolPackage Package { get; set; } = null!;
    public IReadOnlyList<object> ProxyInstances { get; set; } = Array.Empty<object>();
}

/// <summary>
/// ツールマニフェスト
/// </summary>
public class ToolManifest
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string? MainAssembly { get; set; }
    public string? Description { get; set; }
    public string[]? Dependencies { get; set; }
}
