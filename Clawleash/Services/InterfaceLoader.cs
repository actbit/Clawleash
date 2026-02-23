using System.Reflection;
using Clawleash.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services;

/// <summary>
/// 外部DLLからIChatInterfaceを動的に読み込むローダー
/// ディレクトリ監視によるホットリロード対応
/// </summary>
public class InterfaceLoader : IDisposable
{
    private readonly string _interfacesDirectory;
    private readonly ILogger<InterfaceLoader>? _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly FileSystemWatcher? _watcher;
    private readonly Dictionary<string, IChatInterface> _loadedInterfaces = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// インターフェースがロードされた時に発生するイベント
    /// </summary>
    public event EventHandler<InterfaceLoadedEventArgs>? InterfaceLoaded;

    /// <summary>
    /// インターフェースがアンロードされた時に発生するイベント
    /// </summary>
    public event EventHandler<InterfaceUnloadedEventArgs>? InterfaceUnloaded;

    /// <summary>
    /// 現在ロードされているインターフェースの数
    /// </summary>
    public int LoadedInterfaceCount
    {
        get
        {
            lock (_lock)
            {
                return _loadedInterfaces.Count;
            }
        }
    }

    /// <summary>
    /// インターフェースディレクトリのパス
    /// </summary>
    public string InterfacesDirectory => _interfacesDirectory;

    public InterfaceLoader(
        IServiceProvider serviceProvider,
        string? interfacesDirectory = null,
        ILogger<InterfaceLoader>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        _interfacesDirectory = interfacesDirectory ?? GetDefaultInterfacesDirectory();

        // ディレクトリ作成
        if (!Directory.Exists(_interfacesDirectory))
        {
            Directory.CreateDirectory(_interfacesDirectory);
            _logger?.LogInformation("Created interfaces directory: {Directory}", _interfacesDirectory);
        }

        // ファイル監視設定
        try
        {
            _watcher = new FileSystemWatcher(_interfacesDirectory)
            {
                Filter = "*.dll",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = true
            };

            _watcher.Created += OnDllCreated;
            _watcher.Deleted += OnDllDeleted;
            _watcher.Changed += OnDllChanged;
            _watcher.Renamed += OnDllRenamed;
            _watcher.EnableRaisingEvents = true;

            _logger?.LogInformation("FileSystemWatcher started for: {Directory}", _interfacesDirectory);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start FileSystemWatcher for: {Directory}", _interfacesDirectory);
        }
    }

    /// <summary>
    /// デフォルトのインターフェースディレクトリを取得
    /// </summary>
    public static string GetDefaultInterfacesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clawleash", "Interfaces");
    }

    /// <summary>
    /// 起動時に既存のDLLをロード
    /// </summary>
    public IEnumerable<IChatInterface> LoadExistingInterfaces()
    {
        var interfaces = new List<IChatInterface>();

        if (!Directory.Exists(_interfacesDirectory))
        {
            _logger?.LogWarning("Interfaces directory does not exist: {Directory}", _interfacesDirectory);
            return interfaces;
        }

        foreach (var dllFile in Directory.GetFiles(_interfacesDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var iface = LoadInterfaceFromDll(dllFile);
                if (iface != null)
                {
                    lock (_lock)
                    {
                        _loadedInterfaces[dllFile] = iface;
                    }
                    interfaces.Add(iface);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load interface from {File}", dllFile);
            }
        }

        _logger?.LogInformation("Loaded {Count} interfaces from existing DLLs", interfaces.Count);
        return interfaces;
    }

    /// <summary>
    /// 指定されたパスのDLLからインターフェースをロード
    /// </summary>
    public IChatInterface? LoadInterfaceFromDll(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger?.LogWarning("DLL file not found: {File}", dllPath);
            return null;
        }

        try
        {
            // アセンブリをロード
            var assembly = Assembly.LoadFrom(dllPath);

            // IChatInterfaceを実装した型を探す
            var interfaceType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IChatInterface).IsAssignableFrom(t)
                                     && !t.IsInterface
                                     && !t.IsAbstract);

            if (interfaceType == null)
            {
                _logger?.LogDebug("No IChatInterface implementation found in {File}", dllPath);
                return null;
            }

            // DIコンテナを使ってインスタンス作成
            var instance = ActivatorUtilities.CreateInstance(_serviceProvider, interfaceType);
            var chatInterface = (IChatInterface)instance;

            _logger?.LogInformation("Loaded interface: {Name} from {File}", chatInterface.Name, dllPath);
            return chatInterface;
        }
        catch (BadImageFormatException)
        {
            // DLLではない、またはアーキテクチャが異なる
            _logger?.LogDebug("Skipping non-compatible DLL: {File}", dllPath);
            return null;
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger?.LogWarning(ex, "Failed to load types from {File}. Loader exceptions: {Exceptions}",
                dllPath, string.Join(", ", ex.LoaderExceptions.Select(e => e?.Message)));
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load interface from {File}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// 新規DLL追加時の処理
    /// </summary>
    private void OnDllCreated(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;

        // ファイルが完全にコピーされるまで待機
        Task.Run(async () =>
        {
            await Task.Delay(500);

            try
            {
                // ファイルが書き込み可能になるまで待機
                await WaitForFileAccessAsync(e.FullPath);

                var iface = LoadInterfaceFromDll(e.FullPath);
                if (iface != null)
                {
                    lock (_lock)
                    {
                        _loadedInterfaces[e.FullPath] = iface;
                    }

                    InterfaceLoaded?.Invoke(this, new InterfaceLoadedEventArgs
                    {
                        Interface = iface,
                        FilePath = e.FullPath
                    });

                    _logger?.LogInformation("[Hot Reload] Interface loaded: {Name}", iface.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to hot-load interface from {File}", e.FullPath);
            }
        });
    }

    /// <summary>
    /// DLL削除時の処理
    /// </summary>
    private void OnDllDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_loadedInterfaces.TryGetValue(e.FullPath, out var iface))
            {
                _loadedInterfaces.Remove(e.FullPath);

                InterfaceUnloaded?.Invoke(this, new InterfaceUnloadedEventArgs
                {
                    Interface = iface,
                    FilePath = e.FullPath
                });

                _logger?.LogInformation("[Hot Unload] Interface unloaded: {Name}", iface.Name);

                // インターフェースを破棄
                if (iface is IAsyncDisposable asyncDisposable)
                {
                    _ = asyncDisposable.DisposeAsync().AsTask();
                }
                else if (iface is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// DLL変更時の処理
    /// </summary>
    private void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;

        // 変更は再ロードとして扱う
        Task.Run(async () =>
        {
            await Task.Delay(500);

            try
            {
                await WaitForFileAccessAsync(e.FullPath);

                lock (_lock)
                {
                    // 既存のインターフェースをアンロード
                    if (_loadedInterfaces.TryGetValue(e.FullPath, out var oldInterface))
                    {
                        InterfaceUnloaded?.Invoke(this, new InterfaceUnloadedEventArgs
                        {
                            Interface = oldInterface,
                            FilePath = e.FullPath
                        });

                        if (oldInterface is IAsyncDisposable asyncDisposable)
                        {
                            _ = asyncDisposable.DisposeAsync().AsTask();
                        }
                    }
                }

                // 新しいバージョンをロード
                var newInterface = LoadInterfaceFromDll(e.FullPath);
                if (newInterface != null)
                {
                    lock (_lock)
                    {
                        _loadedInterfaces[e.FullPath] = newInterface;
                    }

                    InterfaceLoaded?.Invoke(this, new InterfaceLoadedEventArgs
                    {
                        Interface = newInterface,
                        FilePath = e.FullPath
                    });

                    _logger?.LogInformation("[Hot Reload] Interface reloaded: {Name}", newInterface.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to reload interface from {File}", e.FullPath);
            }
        });
    }

    /// <summary>
    /// DLLリネーム時の処理
    /// </summary>
    private void OnDllRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed) return;

        // 旧パスを削除として扱う
        OnDllDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted,
            Path.GetDirectoryName(e.OldFullPath) ?? "",
            Path.GetFileName(e.OldFullPath)));

        // 新パスを追加として扱う
        OnDllCreated(sender, new FileSystemEventArgs(WatcherChangeTypes.Created,
            Path.GetDirectoryName(e.FullPath) ?? "",
            Path.GetFileName(e.FullPath)));
    }

    /// <summary>
    /// ファイルがアクセス可能になるまで待機
    /// </summary>
    private static async Task WaitForFileAccessAsync(string filePath, int maxAttempts = 10)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100 * (i + 1));
            }
        }
    }

    /// <summary>
    /// ロード済みのインターフェース一覧を取得
    /// </summary>
    public IEnumerable<IChatInterface> GetLoadedInterfaces()
    {
        lock (_lock)
        {
            return _loadedInterfaces.Values.ToList();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _watcher?.Dispose();

        lock (_lock)
        {
            foreach (var iface in _loadedInterfaces.Values)
            {
                try
                {
                    if (iface is IAsyncDisposable asyncDisposable)
                    {
                        _ = asyncDisposable.DisposeAsync().AsTask();
                    }
                    else if (iface is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to dispose interface: {Name}", iface.Name);
                }
            }

            _loadedInterfaces.Clear();
        }

        _logger?.LogInformation("InterfaceLoader disposed");
    }
}

/// <summary>
/// インターフェースロードイベントの引数
/// </summary>
public class InterfaceLoadedEventArgs : EventArgs
{
    /// <summary>
    /// ロードされたインターフェース
    /// </summary>
    public IChatInterface Interface { get; set; } = null!;

    /// <summary>
    /// DLLファイルパス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// インターフェースアンロードイベントの引数
/// </summary>
public class InterfaceUnloadedEventArgs : EventArgs
{
    /// <summary>
    /// アンロードされたインターフェース
    /// </summary>
    public IChatInterface Interface { get; set; } = null!;

    /// <summary>
    /// DLLファイルパス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
}
