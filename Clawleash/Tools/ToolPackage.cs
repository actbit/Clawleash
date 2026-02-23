using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace Clawleash.Tools;

/// <summary>
/// 読み込まれたツールパッケージの情報
/// </summary>
public class ToolPackage : IDisposable
{
    private readonly ILogger<ToolPackage> _logger;
    private AssemblyLoadContext? _loadContext;
    private Assembly? _assembly;
    private bool _disposed;

    public string Name { get; }
    public string Version { get; }
    public string Path { get; }
    public Assembly? Assembly => _assembly;
    public bool IsLoaded => _assembly != null;

    public IReadOnlyList<ToolTypeInfo> ToolTypes { get; private set; } = Array.Empty<ToolTypeInfo>();

    public ToolPackage(
        string name,
        string version,
        string path,
        ILogger<ToolPackage> logger)
    {
        Name = name;
        Version = version;
        Path = path;
        _logger = logger;
    }

    /// <summary>
    /// アセンブリをロード
    /// </summary>
    public bool Load()
    {
        if (_assembly != null) return true;

        try
        {
            _logger.LogInformation("ツールをロード中: {Name} v{Version}", Name, Version);

            // 分離されたロードコンテキストでアセンブリを読み込む
            _loadContext = new AssemblyLoadContext($"Tool_{Name}_{Version}", isCollectible: true);
            _assembly = _loadContext.LoadFromAssemblyPath(Path);

            // [KernelFunction] 属性を持つ型を検出
            var toolTypes = new List<ToolTypeInfo>();
            foreach (var type in _assembly.GetExportedTypes())
            {
                var methods = GetKernelFunctionMethods(type);
                if (methods.Count > 0)
                {
                    toolTypes.Add(new ToolTypeInfo(type, methods));
                    _logger.LogDebug("ツールタイプを検出: {Type} ({Count} メソッド)", type.Name, methods.Count);
                }
            }

            ToolTypes = toolTypes;
            _logger.LogInformation("ツールロード完了: {Count} タイプ", toolTypes.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ツールロードエラー: {Name}", Name);
            return false;
        }
    }

    /// <summary>
    /// [KernelFunction] 属性を持つメソッドを取得
    /// </summary>
    private List<MethodInfo> GetKernelFunctionMethods(Type type)
    {
        var result = new List<MethodInfo>();

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var kernelFunctionAttr = method.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "KernelFunctionAttribute");

            if (kernelFunctionAttr != null)
            {
                result.Add(method);
            }
        }

        return result;
    }

    /// <summary>
    /// アセンブリをアンロード
    /// </summary>
    public void Unload()
    {
        if (_loadContext == null) return;

        _logger.LogInformation("ツールをアンロード中: {Name}", Name);

        ToolTypes = Array.Empty<ToolTypeInfo>();
        _assembly = null;
        _loadContext.Unload();
        _loadContext = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unload();
        _disposed = true;
    }
}

/// <summary>
/// ツールタイプの情報
/// </summary>
public class ToolTypeInfo
{
    public Type Type { get; }
    public string TypeName { get; }
    public string? Namespace { get; }
    public IReadOnlyList<MethodInfo> KernelFunctionMethods { get; }

    public ToolTypeInfo(Type type, List<MethodInfo> methods)
    {
        Type = type;
        TypeName = type.Name;
        Namespace = type.Namespace;
        KernelFunctionMethods = methods;
    }
}
