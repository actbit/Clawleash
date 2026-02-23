using Microsoft.Extensions.Logging;

namespace Clawleash.Sandbox;

/// <summary>
/// フォルダーポリシーを管理し、階層的な継承・上書きを処理する
/// より具体的なパスが優先される
/// </summary>
public class FolderPolicyManager
{
    private readonly ILogger<FolderPolicyManager>? _logger;
    private readonly List<FolderPolicy> _policies = new();
    private readonly Dictionary<string, EffectiveFolderPolicy> _cache = new();
    private readonly object _lock = new();

    /// <summary>
    /// デフォルトのグローバルポリシー
    /// </summary>
    public FolderPolicy DefaultPolicy { get; set; } = new()
    {
        Path = "*",
        Access = FolderAccessLevel.ReadWrite,
        Network = NetworkAccessLevel.Allow,
        Execute = ExecuteLevel.Allow
    };

    public FolderPolicyManager(ILogger<FolderPolicyManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// ポリシーを追加
    /// </summary>
    public void AddPolicy(FolderPolicy policy)
    {
        if (string.IsNullOrEmpty(policy.Path))
        {
            throw new ArgumentException("Policy path cannot be empty");
        }

        lock (_lock)
        {
            // 正規化されたパスに変換
            policy.Path = NormalizePath(policy.Path);

            // 既存の同じパスのポリシーを削除
            _policies.RemoveAll(p => p.Path.Equals(policy.Path, StringComparison.OrdinalIgnoreCase));

            _policies.Add(policy);

            // パスの長さで降順ソート（より具体的なパスが先）
            _policies.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length));

            // キャッシュをクリア
            _cache.Clear();

            _logger?.LogDebug("Policy added: {Path} -> Access={Access}, Network={Network}, Execute={Execute}",
                policy.Path, policy.Access, policy.Network, policy.Execute);
        }
    }

    /// <summary>
    /// 複数のポリシーを一括追加
    /// </summary>
    public void AddPolicies(IEnumerable<FolderPolicy> policies)
    {
        foreach (var policy in policies)
        {
            AddPolicy(policy);
        }
    }

    /// <summary>
    /// 指定パスに適用される有効なポリシーを取得
    /// </summary>
    public EffectiveFolderPolicy GetEffectivePolicy(string path)
    {
        var normalizedPath = NormalizePath(path);

        lock (_lock)
        {
            if (_cache.TryGetValue(normalizedPath, out var cached))
            {
                return cached;
            }

            var effective = CalculateEffectivePolicy(normalizedPath);
            _cache[normalizedPath] = effective;
            return effective;
        }
    }

    /// <summary>
    /// 有効なポリシーを計算（階層的継承を処理）
    /// </summary>
    private EffectiveFolderPolicy CalculateEffectivePolicy(string path)
    {
        // パスにマッチするポリシーを収集（親から子の順）
        var matchingPolicies = new List<FolderPolicy>();

        // 1. デフォルトポリシーを追加
        matchingPolicies.Add(DefaultPolicy);

        // 2. 親フォルダーから順にマッチするポリシーを収集
        foreach (var policy in _policies)
        {
            if (PathContainsOrEquals(policy.Path, path))
            {
                matchingPolicies.Add(policy);
            }
        }

        // 3. 継承を解決して有効なポリシーを構築
        var effective = new EffectiveFolderPolicy
        {
            Path = path
        };

        bool? networkAllowed = null;
        bool? executeAllowed = null;

        // 親から子へ順に適用（子が親を上書き）
        foreach (var policy in matchingPolicies)
        {
            effective.Access = policy.Access;
            effective.SourcePolicy = policy;

            // ネットワーク: Inheritでなければ上書き
            if (policy.Network != NetworkAccessLevel.Inherit)
            {
                networkAllowed = policy.Network == NetworkAccessLevel.Allow;
            }

            // 実行: Inheritでなければ上書き
            if (policy.Execute != ExecuteLevel.Inherit)
            {
                executeAllowed = policy.Execute == ExecuteLevel.Allow;
            }

            // 拡張子リストは上書き
            if (policy.AllowedExtensions.Count > 0)
            {
                effective.AllowedExtensions = new List<string>(policy.AllowedExtensions);
            }
            if (policy.DeniedExtensions.Count > 0)
            {
                effective.DeniedExtensions = new List<string>(policy.DeniedExtensions);
            }

            // ファイルサイズ制限はより厳しい方を採用
            if (policy.MaxFileSizeMB > 0)
            {
                if (effective.MaxFileSizeMB == 0 || policy.MaxFileSizeMB < effective.MaxFileSizeMB)
                {
                    effective.MaxFileSizeMB = policy.MaxFileSizeMB;
                }
            }

            // 監査は有効なら有効
            if (policy.EnableAudit)
            {
                effective.EnableAudit = true;
            }
        }

        effective.NetworkAllowed = networkAllowed ?? (DefaultPolicy.Network == NetworkAccessLevel.Allow);
        effective.ExecuteAllowed = executeAllowed ?? (DefaultPolicy.Execute == ExecuteLevel.Allow);

        _logger?.LogDebug("Effective policy for {Path}: Access={Access}, Network={Network}, Execute={Execute}",
            path, effective.Access, effective.NetworkAllowed, effective.ExecuteAllowed);

        return effective;
    }

    /// <summary>
    /// ファイルアクセスが許可されているかチェック
    /// </summary>
    public FileAccessCheckResult CheckFileAccess(string filePath, bool writeAccess = false)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = filePath;
        }

        var policy = GetEffectivePolicy(directory);
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

        // アクセスレベルチェック
        if (policy.Access == FolderAccessLevel.Deny)
        {
            return new FileAccessCheckResult
            {
                Allowed = false,
                Reason = "Access denied by folder policy",
                Policy = policy
            };
        }

        if (writeAccess && policy.Access == FolderAccessLevel.ReadOnly)
        {
            return new FileAccessCheckResult
            {
                Allowed = false,
                Reason = "Write access denied - folder is read-only",
                Policy = policy
            };
        }

        // 拡張子チェック
        if (policy.DeniedExtensions.Count > 0 &&
            policy.DeniedExtensions.Any(e => e.TrimStart('.').ToLowerInvariant() == extension))
        {
            return new FileAccessCheckResult
            {
                Allowed = false,
                Reason = $"File extension '.{extension}' is denied",
                Policy = policy
            };
        }

        if (policy.AllowedExtensions.Count > 0 &&
            !policy.AllowedExtensions.Any(e => e.TrimStart('.').ToLowerInvariant() == extension))
        {
            return new FileAccessCheckResult
            {
                Allowed = false,
                Reason = $"File extension '.{extension}' is not in allowed list",
                Policy = policy
            };
        }

        // ファイルサイズチェック（書き込み時のみ）
        if (writeAccess && policy.MaxFileSizeMB > 0)
        {
            // 実際のチェックは呼び出し側で行う
            return new FileAccessCheckResult
            {
                Allowed = true,
                MaxFileSizeMB = policy.MaxFileSizeMB,
                Policy = policy
            };
        }

        return new FileAccessCheckResult
        {
            Allowed = true,
            Policy = policy
        };
    }

    /// <summary>
    /// ネットワークアクセスが許可されているかチェック
    /// </summary>
    public bool IsNetworkAllowed(string path)
    {
        var policy = GetEffectivePolicy(path);
        return policy.NetworkAllowed;
    }

    /// <summary>
    /// プロセス実行が許可されているかチェック
    /// </summary>
    public bool IsExecuteAllowed(string path)
    {
        var policy = GetEffectivePolicy(path);
        return policy.ExecuteAllowed;
    }

    /// <summary>
    /// すべてのポリシーを取得
    /// </summary>
    public IReadOnlyList<FolderPolicy> GetAllPolicies()
    {
        lock (_lock)
        {
            return _policies.ToList();
        }
    }

    /// <summary>
    /// ポリシーをクリア
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _policies.Clear();
            _cache.Clear();
        }
    }

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// パスを正規化
    /// </summary>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>
    /// parentPathがpathを含むか等しいかチェック
    /// </summary>
    private static bool PathContainsOrEquals(string parentPath, string childPath)
    {
        if (parentPath.Equals(childPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // childPathがparentPathで始まるかチェック
        if (childPath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// ファイルアクセスチェック結果
/// </summary>
public class FileAccessCheckResult
{
    public bool Allowed { get; set; }
    public string? Reason { get; set; }
    public int MaxFileSizeMB { get; set; }
    public EffectiveFolderPolicy? Policy { get; set; }
}
