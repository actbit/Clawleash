using Clowleash.Configuration;

namespace Clowleash.Security;

/// <summary>
/// URLの検証を行うクラス
/// 許可/拒否ドメインのリストに基づいてアクセスを制御する
/// </summary>
public class UrlValidator
{
    private readonly BrowserSettings _settings;
    private readonly HashSet<string> _allowedDomains;
    private readonly HashSet<string> _deniedDomains;
    private readonly bool _allowAllDomains;

    public UrlValidator(BrowserSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 許可ドメインを正規化して保持
        _allowedDomains = new HashSet<string>(
            settings.AllowedDomains.Select(NormalizeDomain),
            StringComparer.OrdinalIgnoreCase);

        _deniedDomains = new HashSet<string>(
            settings.DeniedDomains.Select(NormalizeDomain),
            StringComparer.OrdinalIgnoreCase);

        // ワイルドカード "*" が含まれているかチェック
        _allowAllDomains = _allowedDomains.Contains("*") || _allowedDomains.Count == 0;
    }

    /// <summary>
    /// 指定されたURLがアクセス可能か検証する
    /// </summary>
    /// <param name="url">検証するURL</param>
    /// <returns>許可されている場合はtrue</returns>
    public bool IsUrlAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var uri = new Uri(url);
            return IsDomainAllowed(uri.Host);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 指定されたドメインがアクセス可能か検証する
    /// </summary>
    /// <param name="domain">検証するドメイン</param>
    /// <returns>許可されている場合はtrue</returns>
    public bool IsDomainAllowed(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        var normalizedDomain = NormalizeDomain(domain);

        // 拒否リストにあれば不許可（優先）
        if (IsDomainInList(normalizedDomain, _deniedDomains))
        {
            return false;
        }

        // 全ドメイン許可の場合
        if (_allowAllDomains)
        {
            return true;
        }

        // 許可リストでチェック
        return IsDomainInList(normalizedDomain, _allowedDomains);
    }

    /// <summary>
    /// ドメインがリスト（ワイルドカード含む）に含まれるかチェック
    /// </summary>
    private static bool IsDomainInList(string domain, HashSet<string> list)
    {
        // 完全一致
        if (list.Contains(domain))
        {
            return true;
        }

        // ワイルドカードパターンマッチング (*.example.com など)
        foreach (var pattern in list)
        {
            if (pattern.StartsWith("*."))
            {
                var baseDomain = pattern[2..];
                if (domain.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase) ||
                    domain.Equals(baseDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (pattern.StartsWith("."))
            {
                // .example.com パターン
                if (domain.EndsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                    domain.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ドメインを正規化する
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        // ポート番号を除去
        var colonIndex = domain.IndexOf(':');
        if (colonIndex > 0)
        {
            domain = domain[..colonIndex];
        }

        // 小文字化、トリム
        return domain.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// URLを安全な形式にサニタイズする
    /// </summary>
    public string SanitizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        try
        {
            var uri = new Uri(url);

            // http/httpsのみ許可
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return string.Empty;
            }

            // クエリパラメータやフラグメントを保持したまま正規化
            return uri.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 危険なスキームやパターンが含まれていないかチェック
    /// </summary>
    public bool HasDangerousPatterns(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        var dangerousPatterns = new[]
        {
            "javascript:",
            "data:",
            "vbscript:",
            "file:",
            "ftp:",
            "..",
            "@", // URLインジェクション
            "\\", // Windowsパス
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // IPアドレスへの直接アクセスを制限（オプション）
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (IsIpAddress(uri.Host))
            {
                // ローカルIPアドレスをブロック
                if (IsPrivateIpAddress(uri.Host))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 文字列がIPアドレスかチェック
    /// </summary>
    private static bool IsIpAddress(string host)
    {
        return System.Net.IPAddress.TryParse(host, out _);
    }

    /// <summary>
    /// プライベートIPアドレスかチェック
    /// </summary>
    private static bool IsPrivateIpAddress(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();

        // IPv4 プライベートアドレス
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 127.0.0.0/8 (localhost)
            if (bytes[0] == 127)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 検証結果の詳細情報を取得
    /// </summary>
    public UrlValidationResult Validate(string url)
    {
        var result = new UrlValidationResult
        {
            OriginalUrl = url
        };

        try
        {
            var uri = new Uri(url);
            result.NormalizedUrl = uri.ToString();
            result.Domain = uri.Host;
            result.Scheme = uri.Scheme;

            result.HasDangerousPatterns = HasDangerousPatterns(url);
            result.IsAllowed = !result.HasDangerousPatterns && IsUrlAllowed(url);

            if (!result.IsAllowed)
            {
                if (result.HasDangerousPatterns)
                {
                    result.ErrorMessage = "URLに危険なパターンが含まれています";
                }
                else
                {
                    result.ErrorMessage = $"ドメイン '{result.Domain}' へのアクセスは許可されていません";
                }
            }
        }
        catch (UriFormatException ex)
        {
            result.IsAllowed = false;
            result.ErrorMessage = $"無効なURL形式: {ex.Message}";
        }

        return result;
    }
}

/// <summary>
/// URL検証の結果
/// </summary>
public class UrlValidationResult
{
    public string OriginalUrl { get; set; } = string.Empty;
    public string NormalizedUrl { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public bool HasDangerousPatterns { get; set; }
    public string? ErrorMessage { get; set; }

    public override string ToString()
    {
        return IsAllowed
            ? $"OK: {NormalizedUrl}"
            : $"NG: {ErrorMessage}";
    }
}
