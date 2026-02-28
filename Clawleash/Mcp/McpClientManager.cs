using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Clawleash.Sandbox;

namespace Clawleash.Mcp;

/// <summary>
/// MCPサーバーから取得したツール情報
/// </summary>
public class McpToolInfo
{
    public string ServerName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// 接続済みMCPサーバー情報
/// </summary>
public class ConnectedServer
{
    public McpServerConfig Config { get; set; } = null!;

    // stdio用
    public Process? Process { get; set; }
    public StreamWriter? StdIn { get; set; }
    public StreamReader? StdOut { get; set; }
    public StreamReader? StdErr { get; set; }

    // SSE用
    public HttpClient? HttpClient { get; set; }
    public CancellationTokenSource? SseCts { get; set; }
    public Task? SseListenerTask { get; set; }
    public ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> PendingRequests { get; } = new();

    public List<McpToolInfo> Tools { get; set; } = new();
    public bool IsConnected { get; set; }
    public int RequestId { get; set; }
}

/// <summary>
/// MCPクライアント管理
/// 外部MCPサーバーへの接続・ツール実行を行う
/// </summary>
public class McpClientManager : IDisposable
{
    private readonly ILogger<McpClientManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISandboxProvider? _sandboxProvider;
    private readonly Dictionary<string, ConnectedServer> _servers = new();
    private bool _disposed;

    public IReadOnlyDictionary<string, ConnectedServer> Servers => _servers;
    public bool IsEnabled { get; private set; }

    public McpClientManager(
        ILoggerFactory loggerFactory,
        ISandboxProvider? sandboxProvider = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<McpClientManager>();
        _sandboxProvider = sandboxProvider;
    }

    /// <summary>
    /// 設定から全MCPサーバーに接続
    /// </summary>
    public async Task InitializeAsync(McpSettings settings)
    {
        if (!settings.Enabled)
        {
            _logger.LogInformation("MCP機能は無効です");
            IsEnabled = false;
            return;
        }

        IsEnabled = true;
        _logger.LogInformation("MCPサーバー {Count} 件を初期化中...", settings.Servers.Count);

        foreach (var config in settings.Servers.Where(s => s.Enabled))
        {
            try
            {
                await ConnectAsync(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCPサーバー接続エラー: {Name}", config.Name);
            }
        }

        _logger.LogInformation("MCPサーバー初期化完了: {Connected}/{Total} 件接続",
            _servers.Count(s => s.Value.IsConnected), settings.Servers.Count);
    }

    /// <summary>
    /// MCPサーバーに接続
    /// </summary>
    public async Task<ConnectedServer?> ConnectAsync(McpServerConfig config)
    {
        if (_servers.ContainsKey(config.Name))
        {
            _logger.LogWarning("MCPサーバーは既に接続済み: {Name}", config.Name);
            return _servers[config.Name];
        }

        _logger.LogInformation("MCPサーバーに接続中: {Name} ({Transport})", config.Name, config.Transport);

        var server = new ConnectedServer
        {
            Config = config,
            RequestId = 0
        };

        try
        {
            switch (config.Transport.ToLowerInvariant())
            {
                case "stdio":
                    await ConnectStdioAsync(server);
                    break;
                case "sse":
                    await ConnectSseAsync(server);
                    break;
                default:
                    throw new NotSupportedException($"サポートされていないトランスポート: {config.Transport}");
            }

            // 初期化ハンドシェイク
            await InitializeServerAsync(server);

            // ツール一覧を取得
            await LoadToolsAsync(server);

            _servers[config.Name] = server;
            _logger.LogInformation("MCPサーバー接続完了: {Name} ({ToolCount} ツール)",
                config.Name, server.Tools.Count);

            return server;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCPサーバー接続失敗: {Name}", config.Name);
            CleanupServer(server);
            return null;
        }
    }

    /// <summary>
    /// stdio トランスポートで接続
    /// </summary>
    private async Task ConnectStdioAsync(ConnectedServer server)
    {
        var config = server.Config;

        if (string.IsNullOrEmpty(config.Command))
        {
            throw new InvalidOperationException("stdio接続にはcommandが必要です");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // 引数を追加（個別に追加）
        if (config.Args?.Count > 0)
        {
            foreach (var arg in config.Args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        // 環境変数を設定
        if (config.Environment != null)
        {
            foreach (var (key, value) in config.Environment)
            {
                var expandedValue = ExpandEnvironmentVariables(value);
                startInfo.Environment[key] = expandedValue;
            }
        }

        // サンドボックスで実行する場合
        if (config.UseSandbox && _sandboxProvider != null)
        {
            _logger.LogInformation("サンドボックス内でMCPサーバーを実行: {Name}", config.Name);
            // TODO: サンドボックスプロバイダーでのプロセス実行
            // 現在は直接プロセス実行
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();

        server.Process = process;
        server.StdIn = process.StandardInput;
        server.StdOut = process.StandardOutput;
        server.StdErr = process.StandardError;

        // エラー出力をログに出力
        _ = Task.Run(async () =>
        {
            while (!process.HasExited && server.StdErr != null)
            {
                var line = await server.StdErr.ReadLineAsync();
                if (line != null)
                {
                    _logger.LogWarning("[{Name}] stderr: {Line}", config.Name, line);
                }
            }
        });

        _logger.LogDebug("MCPプロセス開始: PID={Pid}", process.Id);
    }

    /// <summary>
    /// SSE トランスポートで接続
    /// </summary>
    private async Task ConnectSseAsync(ConnectedServer server)
    {
        var config = server.Config;

        if (string.IsNullOrEmpty(config.Url))
        {
            throw new InvalidOperationException("SSE接続にはurlが必要です");
        }

        _logger.LogInformation("SSE接続: {Url}", config.Url);

        // HttpClientを作成
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        server.HttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
        };

        // カスタムヘッダーを設定
        if (config.Headers != null)
        {
            foreach (var (key, value) in config.Headers)
            {
                server.HttpClient.DefaultRequestHeaders.Add(key, value);
            }
        }

        // SSEストリームを開始
        server.SseCts = new CancellationTokenSource();
        server.SseListenerTask = Task.Run(() => ListenToSseStreamAsync(server), server.SseCts.Token);

        // SSE接続が確立されるまで少し待機
        await Task.Delay(100);

        _logger.LogInformation("SSE接続確立: {Url}", config.Url);
    }

    /// <summary>
    /// SSEストリームをリッスン
    /// </summary>
    private async Task ListenToSseStreamAsync(ConnectedServer server)
    {
        var baseUrl = server.Config.Url!.TrimEnd('/');
        var sseUrl = $"{baseUrl}/sse";

        _logger.LogDebug("SSEストリーム開始: {Url}", sseUrl);

        while (server.SseCts?.Token.IsCancellationRequested == false)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, sseUrl);
                request.Headers.Add("Accept", "text/event-stream");

                using var response = await server.HttpClient!.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    server.SseCts.Token);

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(server.SseCts.Token);
                using var reader = new StreamReader(stream);

                string? eventType = null;
                var dataBuilder = new StringBuilder();

                while (server.SseCts.Token.IsCancellationRequested == false)
                {
                    var line = await reader.ReadLineAsync(server.SseCts.Token);

                    if (line == null)
                        break;

                    if (line.StartsWith("event:"))
                    {
                        eventType = line[6..].Trim();
                    }
                    else if (line.StartsWith("data:"))
                    {
                        dataBuilder.AppendLine(line[5..].Trim());
                    }
                    else if (string.IsNullOrEmpty(line))
                    {
                        // 空行 = イベント終了
                        if (dataBuilder.Length > 0)
                        {
                            await ProcessSseEventAsync(server, eventType, dataBuilder.ToString());
                        }
                        eventType = null;
                        dataBuilder.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SSEストリームエラー、再接続中...");

                if (server.SseCts.Token.IsCancellationRequested)
                    break;

                await Task.Delay(1000, server.SseCts.Token);
            }
        }

        _logger.LogDebug("SSEストリーム終了: {Name}", server.Config.Name);
    }

    /// <summary>
    /// SSEイベントを処理
    /// </summary>
    private Task ProcessSseEventAsync(ConnectedServer server, string? eventType, string data)
    {
        _logger.LogDebug("SSEイベント受信: type={Type}, data={Data}", eventType, data[..Math.Min(100, data.Length)]);

        try
        {
            using var doc = JsonDocument.Parse(data.Trim());
            var root = doc.RootElement;

            // JSON-RPCレスポンスの場合
            if (root.TryGetProperty("id", out var idElement))
            {
                var id = idElement.GetInt32();

                if (server.PendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var msg)
                            ? msg.GetString()
                            : "Unknown error";
                        _logger.LogError("MCP SSEエラー: {Message}", message);
                        tcs.TrySetResult(null);
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        var resultJson = result.GetRawText();
                        tcs.TrySetResult(JsonDocument.Parse(resultJson).RootElement);
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSEイベント解析エラー");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// SSE経由でJSON-RPCリクエストを送信
    /// </summary>
    private async Task<JsonElement?> SendSseRequestAsync(ConnectedServer server, string method, object? parameters = null)
    {
        if (server.HttpClient == null)
        {
            return null;
        }

        var requestId = ++server.RequestId;
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(request);
        _logger.LogDebug("MCP SSE Request: {Json}", json);

        // TaskCompletionSourceを作成して待機
        var tcs = new TaskCompletionSource<JsonElement?>();
        server.PendingRequests[requestId] = tcs;

        try
        {
            var baseUrl = server.Config.Url!.TrimEnd('/');
            var messageUrl = $"{baseUrl}/message";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await server.HttpClient.PostAsync(messageUrl, content, server.SseCts?.Token ?? CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("MCP SSE POST失敗: {Status}", response.StatusCode);
                server.PendingRequests.TryRemove(requestId, out _);
                return null;
            }

            // タイムアウト付きでレスポンスを待機
            using var cts = new CancellationTokenSource(server.Config.TimeoutMs);
            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(server.Config.TimeoutMs, cts.Token));

            if (completedTask != tcs.Task)
            {
                _logger.LogError("MCP SSE リクエストがタイムアウト: {Method}", method);
                server.PendingRequests.TryRemove(requestId, out _);
                return null;
            }

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP SSE リクエストエラー: {Method}", method);
            server.PendingRequests.TryRemove(requestId, out _);
            return null;
        }
    }

    /// <summary>
    /// MCP初期化ハンドシェイク
    /// </summary>
    private async Task InitializeServerAsync(ConnectedServer server)
    {
        // initialize リクエストを送信
        var response = await SendRequestAsync(server, "initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new
            {
                name = "Clawleash",
                version = "1.0.0"
            }
        });

        if (response == null)
        {
            throw new Exception("初期化レスポンスが null です");
        }

        // 接続完了をマーク
        server.IsConnected = true;
        _logger.LogDebug("MCPサーバー初期化完了: {Name}", server.Config.Name);
    }

    /// <summary>
    /// ツール一覧を取得
    /// </summary>
    private async Task LoadToolsAsync(ConnectedServer server)
    {
        var response = await SendRequestAsync(server, "tools/list", new { });

        if (response == null)
        {
            _logger.LogWarning("ツール一覧の取得に失敗: {Name}", server.Config.Name);
            return;
        }

        try
        {
            if (response.Value.TryGetProperty("tools", out var toolsElement))
            {
                foreach (var tool in toolsElement.EnumerateArray())
                {
                    var toolInfo = new McpToolInfo
                    {
                        ServerName = server.Config.Name,
                        ToolName = tool.GetProperty("name").GetString() ?? "",
                        Description = tool.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? ""
                            : ""
                    };

                    if (tool.TryGetProperty("inputSchema", out var schema))
                    {
                        toolInfo.InputSchema = schema;
                    }

                    server.Tools.Add(toolInfo);
                    _logger.LogDebug("ツール検出: {Server}.{Tool}", toolInfo.ServerName, toolInfo.ToolName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ツール一覧の解析エラー: {Name}", server.Config.Name);
        }
    }

    /// <summary>
    /// MCPリクエストを送信（トランスポート自動選択）
    /// </summary>
    private async Task<JsonElement?> SendRequestAsync(ConnectedServer server, string method, object? parameters = null)
    {
        // トランスポートに応じて適切なメソッドを選択
        if (server.HttpClient != null)
        {
            return await SendSseRequestAsync(server, method, parameters);
        }

        return await SendStdioRequestAsync(server, method, parameters);
    }

    /// <summary>
    /// stdio経由でJSON-RPCリクエストを送信
    /// </summary>
    private async Task<JsonElement?> SendStdioRequestAsync(ConnectedServer server, string method, object? parameters = null)
    {
        if (server.StdIn == null || server.StdOut == null)
        {
            return null;
        }

        var requestId = ++server.RequestId;
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(request);
        _logger.LogDebug("MCP Request: {Json}", json);

        await server.StdIn.WriteLineAsync(json);
        await server.StdIn.FlushAsync();

        // タイムアウト付きでレスポンスを読み込み
        using var cts = new CancellationTokenSource(server.Config.TimeoutMs);
        string? responseLine;

        try
        {
            // ReadLineAsyncはValueTaskを返すため、Taskに変換してタイムアウト処理
            var readTask = server.StdOut.ReadLineAsync(cts.Token).AsTask();
            var completedTask = await Task.WhenAny(readTask, Task.Delay(server.Config.TimeoutMs, cts.Token));

            if (completedTask != readTask)
            {
                _logger.LogError("MCPリクエストがタイムアウト: {Method} ({Timeout}ms)", method, server.Config.TimeoutMs);
                return null;
            }

            responseLine = await readTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("MCPリクエストがキャンセルされました: {Method}", method);
            return null;
        }

        if (string.IsNullOrEmpty(responseLine))
        {
            return null;
        }

        _logger.LogDebug("MCP Response: {Response}", responseLine);

        // JsonDocumentをusingで適切に破棄
        using var doc = JsonDocument.Parse(responseLine);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : "Unknown error";
            _logger.LogError("MCPエラー: {Message}", message);
            return null;
        }

        if (root.TryGetProperty("result", out var result))
        {
            // Cloneではなく、JSON文字列として保存してから再パース
            // これによりメモリリークを防ぐ
            var resultJson = result.GetRawText();
            return JsonDocument.Parse(resultJson).RootElement;
        }

        return null;
    }

    /// <summary>
    /// MCPツールを実行
    /// </summary>
    public async Task<string> ExecuteToolAsync(string serverName, string toolName, object? arguments = null)
    {
        if (!_servers.TryGetValue(serverName, out var server) || !server.IsConnected)
        {
            return $"エラー: MCPサーバー '{serverName}' に接続されていません";
        }

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == toolName);
        if (tool == null)
        {
            return $"エラー: ツール '{toolName}' が見つかりません (サーバー: {serverName})";
        }

        try
        {
            var response = await SendRequestAsync(server, "tools/call", new
            {
                name = toolName,
                arguments = arguments ?? new { }
            });

            if (response == null)
            {
                return "エラー: ツール実行のレスポンスが null です";
            }

            // content配列からテキストを抽出
            if (response.Value.TryGetProperty("content", out var content))
            {
                var result = new System.Text.StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) && type.GetString() == "text")
                    {
                        if (item.TryGetProperty("text", out var text))
                        {
                            result.AppendLine(text.GetString());
                        }
                    }
                }
                return result.ToString().Trim();
            }

            return response.Value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ツール実行エラー: {Server}.{Tool}", serverName, toolName);
            return $"ツール実行エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 全サーバーのツール一覧を取得
    /// </summary>
    public IEnumerable<McpToolInfo> GetAllTools()
    {
        return _servers.Values.SelectMany(s => s.Tools);
    }

    /// <summary>
    /// 環境変数を展開
    /// </summary>
    private static string ExpandEnvironmentVariables(string value)
    {
        // ${VAR} 形式を展開
        var result = value;
        var matches = System.Text.RegularExpressions.Regex.Matches(value, @"\$\{(\w+)\}");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName) ?? "";
            result = result.Replace($"${{{varName}}}", envValue);
        }
        return Environment.ExpandEnvironmentVariables(result);
    }

    private void CleanupServer(ConnectedServer server)
    {
        try
        {
            // stdio クリーンアップ
            server.StdIn?.Close();
            server.StdOut?.Close();
            server.StdErr?.Close();
            server.Process?.Kill(entireProcessTree: true);
            server.Process?.Dispose();

            // SSE クリーンアップ
            server.SseCts?.Cancel();
            server.SseCts?.Dispose();
            server.HttpClient?.Dispose();

            // 待機中のリクエストをキャンセル
            foreach (var tcs in server.PendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            server.PendingRequests.Clear();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var server in _servers.Values)
        {
            CleanupServer(server);
        }
        _servers.Clear();
        _disposed = true;
    }
}
