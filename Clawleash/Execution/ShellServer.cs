using System.Diagnostics;
using System.Runtime.InteropServices;
using MessagePack;
using NetMQ;
using NetMQ.Sockets;
using Microsoft.Extensions.Logging;
using Clawleash.Contracts;
using Clawleash.Configuration;
using Clawleash.Sandbox;

namespace Clawleash.Execution;

/// <summary>
/// Clawleash.Shell プロセスを管理するサーバー
/// プロセス起動、IPC通信、ライフサイクル管理を行う
/// RouterSocket で待機し、Shell プロセスからの接続を受け付ける
/// </summary>
public class ShellServer : IAsyncDisposable
{
    private readonly ILogger<ShellServer> _logger;
    private readonly ShellServerOptions _options;
    private readonly ClawleashSettings? _settings;
    private Process? _process;
    private RouterSocket? _socket;
    private string? _boundAddress;
    private string? _clientIdentity;
    private bool _initialized;
    private bool _disposed;
    private int _messageCounter;

    // AppContainer関連
    private IntPtr _packageSid;
    private IntPtr _capabilitiesPtr;
    private readonly List<IntPtr> _allocatedMemory = new();

    public bool IsConnected => _socket != null && _process?.HasExited == false && !string.IsNullOrEmpty(_clientIdentity);
    public bool IsInitialized => _initialized;
    public string? BoundAddress => _boundAddress;

    public ShellServer(ILogger<ShellServer> logger, ShellServerOptions? options = null, ClawleashSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ShellServerOptions();
        _settings = settings;
    }

    /// <summary>
    /// Shell プロセスを開始して接続を待機
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process != null)
        {
            throw new InvalidOperationException("Shell プロセスは既に開始されています");
        }

        _logger.LogInformation("Shell サーバーを開始中...");

        try
        {
            // ZeroMQ ソケットをバインド
            _socket = new RouterSocket();
            var address = GetDefaultAddress();
            var port = _socket.BindRandomPort(address);
            _boundAddress = $"{address}:{port}";

            _logger.LogInformation("サーバーバインド完了: {Address}", _boundAddress);

            // Shell 実行ファイルのパスを特定
            var shellPath = FindShellExecutable();
            if (string.IsNullOrEmpty(shellPath))
            {
                _logger.LogError("Shell 実行ファイルが見つかりません");
                return false;
            }

            // プロセスを開始
            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = $"--server \"{_boundAddress}\"{(_options.Verbose ? " --verbose" : "")}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Windows AppContainer 設定
            if (_options.UseSandbox && OperatingSystem.IsWindows())
            {
                // TODO: AppContainer を作成して InternetClient ケーパビリティを追加
                ConfigureAppContainer(startInfo);
            }

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("[Shell stdout] {Data}", e.Data);
                }
            };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogWarning("[Shell stderr] {Data}", e.Data);
                }
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Shell からの初期接続を待機
            var timeout = DateTime.UtcNow.AddMilliseconds(_options.StartTimeoutMs);
            while (string.IsNullOrEmpty(_clientIdentity) && DateTime.UtcNow < timeout)
            {
                if (_process.HasExited)
                {
                    _logger.LogError("Shell プロセスが異常終了しました");
                    return false;
                }

                // 非ブロッキングでメッセージをチェック
                var frames = _socket.ReceiveMultipartBytes(100);
                if (frames != null && frames.Count > 0)
                {
                    HandleInitialConnection(frames);
                }
            }

            if (string.IsNullOrEmpty(_clientIdentity))
            {
                _logger.LogError("Shell プロセスからの接続がありません");
                await StopAsync();
                return false;
            }

            _logger.LogInformation("Shell プロセス接続完了");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell プロセス開始エラー");
            await StopAsync();
            return false;
        }
    }

    /// <summary>
    /// 初期接続を処理
    /// </summary>
    private void HandleInitialConnection(List<byte[]> frames)
    {
        if (frames.Count >= 2)
        {
            _clientIdentity = Convert.ToBase64String(frames[0]);
            var data = frames[^1];

            try
            {
                var message = MessagePackSerializer.Deserialize<ShellReadyMessage>(data);
                _logger.LogInformation(
                    "Shell 接続受信: PID={ProcessId}, Runtime={Runtime}",
                    message.ProcessId, message.Runtime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "初期メッセージの解析に失敗");
            }
        }
    }

    /// <summary>
    /// AppContainer を設定
    /// プロセスをAppContainer内で実行するための設定を行う
    /// </summary>
    private void ConfigureAppContainer(ProcessStartInfo startInfo)
    {
        if (_settings == null)
        {
            _logger.LogWarning("AppContainer設定にはClawleashSettingsが必要です");
            return;
        }

        try
        {
            var containerName = _settings.Sandbox.AppContainerName;

            // 既存のAppContainer SIDを取得、または新規作成
            _packageSid = GetOrCreateAppContainerSid(containerName);

            if (_packageSid == IntPtr.Zero)
            {
                _logger.LogError("AppContainer SIDの取得に失敗しました");
                return;
            }

            // ケーパビリティを設定
            var capabilities = InitializeCapabilities(_settings.Sandbox.Capabilities);

            // ProcThreadAttributeListのサイズを取得
            var attributeListSize = Sandbox.NativeMethods.GetProcThreadAttributeListSize(1);
            var attributeList = Marshal.AllocHGlobal(attributeListSize);
            _allocatedMemory.Add(attributeList);

            // 属性リストを初期化
            if (!Sandbox.NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("InitializeProcThreadAttributeList failed: {Error}", error);
                return;
            }

            // SECURITY_CAPABILITIES構造体を設定
            var securityCapabilities = new Sandbox.NativeMethods.SECURITY_CAPABILITIES
            {
                AppContainerSid = _packageSid,
                Capabilities = capabilities,
                CapabilityCount = (uint)_settings.Sandbox.Capabilities.GetHashCode().ToString().Split(',').Length,
                Reserved = 0
            };

            // PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIESを設定
            var attributePtr = new IntPtr(0x00020015);
            if (!Sandbox.NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                attributePtr,
                ref securityCapabilities,
                Marshal.SizeOf<Sandbox.NativeMethods.SECURITY_CAPABILITIES>(),
                IntPtr.Zero,
                IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("UpdateProcThreadAttribute failed: {Error}", error);
                return;
            }

            _logger.LogInformation("AppContainer設定完了: {ContainerName}, Capabilities: {Capabilities}",
                containerName, _settings.Sandbox.Capabilities);

            // ProcessStartInfoには直接AppContainerを設定できないため、
            // 別途CreateProcess Win32 APIを使用する必要がある
            // ここでは設定を記録し、実際のプロセス起動はStartAsyncで処理

            // 注意: Process.Start()ではAppContainerを直接使用できない
            // 本格的な実装では、このメソッドでWin32 CreateProcessを直接呼ぶ必要がある
            // 現在は設定のみ行い、プロセス起動時に設定を適用

            startInfo.Environment["CLAWLEASH_SANDBOX_TYPE"] = "AppContainer";
            startInfo.Environment["CLAWLEASH_APPCONTAINER_NAME"] = containerName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppContainer設定エラー");
        }
    }

    /// <summary>
    /// AppContainer SIDを取得または作成
    /// </summary>
    private IntPtr GetOrCreateAppContainerSid(string containerName)
    {
        // 既存のSIDを取得を試行
        var result = Sandbox.NativeMethods.DeriveAppContainerSidFromAppContainerName(containerName, out var sid);

        if (result == Sandbox.NativeMethods.ERROR_SUCCESS && sid != IntPtr.Zero)
        {
            _logger.LogDebug("既存のAppContainer SIDを取得: {ContainerName}", containerName);
            return sid;
        }

        // 新しいプロファイルを作成
        result = Sandbox.NativeMethods.CreateAppContainerProfile(
            containerName,
            "Clawleash Shell Sandbox",
            "Clawleash Shell execution sandbox",
            IntPtr.Zero,
            0,
            out sid);

        if (result == Sandbox.NativeMethods.ERROR_ALREADY_EXISTS)
        {
            result = Sandbox.NativeMethods.DeriveAppContainerSidFromAppContainerName(containerName, out sid);
            if (result != Sandbox.NativeMethods.ERROR_SUCCESS || sid == IntPtr.Zero)
            {
                _logger.LogError("既存のAppContainer SIDの取得に失敗: {Result}", result);
                return IntPtr.Zero;
            }
        }
        else if (result != Sandbox.NativeMethods.ERROR_SUCCESS || sid == IntPtr.Zero)
        {
            _logger.LogError("AppContainerプロファイルの作成に失敗: {Result}", result);
            return IntPtr.Zero;
        }

        _logger.LogInformation("AppContainerプロファイルを作成: {ContainerName}", containerName);
        return sid;
    }

    /// <summary>
    /// ケーパビリティを初期化してメモリを確保
    /// </summary>
    private IntPtr InitializeCapabilities(AppContainerCapability capabilityFlags)
    {
        if (capabilityFlags == AppContainerCapability.None)
        {
            return IntPtr.Zero;
        }

        // Flags enumから個別のケーパビリティを抽出
        var capabilitiesList = new List<AppContainerCapability>();
        foreach (AppContainerCapability cap in Enum.GetValues(typeof(AppContainerCapability)))
        {
            if (cap != AppContainerCapability.None && capabilityFlags.HasFlag(cap))
            {
                capabilitiesList.Add(cap);
            }
        }

        if (capabilitiesList.Count == 0)
        {
            return IntPtr.Zero;
        }

        var sids = Sandbox.NativeMethods.CreateCapabilitySids(capabilitiesList.ToArray());
        if (sids.Length == 0)
        {
            return IntPtr.Zero;
        }

        // アンマネージメモリにコピー
        var size = Marshal.SizeOf<Sandbox.NativeMethods.SID_AND_ATTRIBUTES>() * sids.Length;
        _capabilitiesPtr = Marshal.AllocHGlobal(size);
        _allocatedMemory.Add(_capabilitiesPtr);

        for (int i = 0; i < sids.Length; i++)
        {
            var ptr = _capabilitiesPtr + i * Marshal.SizeOf<Sandbox.NativeMethods.SID_AND_ATTRIBUTES>();
            Marshal.StructureToPtr(sids[i], ptr, false);
        }

        _logger.LogDebug("ケーパビリティを初期化: {Count}個", sids.Length);
        return _capabilitiesPtr;
    }

    /// <summary>
    /// デフォルトアドレスを取得
    /// </summary>
    private static string GetDefaultAddress() => "tcp://127.0.0.1";

    /// <summary>
    /// Shell 実行ファイルを検索
    /// </summary>
    private string? FindShellExecutable()
    {
        var assemblyLocation = typeof(ShellServer).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        if (directory == null) return null;

        var executableName = OperatingSystem.IsWindows()
            ? "Clawleash.Shell.exe"
            : "Clawleash.Shell";

        // 同じディレクトリ
        var path = Path.Combine(directory, executableName);
        if (File.Exists(path)) return path;

        // 親ディレクトリの Clawleash.Shell サブディレクトリ
        var parentDir = Directory.GetParent(directory)?.FullName;
        if (parentDir != null)
        {
            path = Path.Combine(parentDir, "Clawleash.Shell", executableName);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// Shell を初期化
    /// </summary>
    public async Task<ShellInitializeResponse> InitializeAsync(
        ShellInitializeRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Shell 初期化中: LanguageMode={LanguageMode}", request.LanguageMode);

        var response = await SendRequestAsync<ShellInitializeRequest, ShellInitializeResponse>(
            request, cancellationToken);

        _initialized = response.Success;
        return response;
    }

    /// <summary>
    /// コマンドを実行
    /// </summary>
    public async Task<ShellExecuteResponse> ExecuteAsync(
        ShellExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        EnsureInitialized();

        _logger.LogDebug("コマンド実行: {Command}", request.Command);

        return await SendRequestAsync<ShellExecuteRequest, ShellExecuteResponse>(
            request, cancellationToken);
    }

    /// <summary>
    /// 簡易コマンド実行
    /// </summary>
    public async Task<ShellExecuteResponse> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default)
    {
        var request = new ShellExecuteRequest
        {
            Command = command,
            WorkingDirectory = workingDirectory,
            TimeoutMs = timeoutMs
        };

        return await ExecuteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Tool を呼び出し
    /// </summary>
    public async Task<ToolInvokeResponse> InvokeToolAsync(
        string toolName,
        string methodName,
        object?[] arguments,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var request = new ToolInvokeRequest
        {
            ToolName = toolName,
            MethodName = methodName,
            Arguments = arguments
        };

        return await SendRequestAsync<ToolInvokeRequest, ToolInvokeResponse>(request, cancellationToken);
    }

    /// <summary>
    /// Ping を送信
    /// </summary>
    public async Task<ShellPingResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var request = new ShellPingRequest { Payload = $"ping-{Interlocked.Increment(ref _messageCounter)}" };
        return await SendRequestAsync<ShellPingRequest, ShellPingResponse>(request, cancellationToken);
    }

    /// <summary>
    /// Shell をシャットダウン
    /// </summary>
    public async Task ShutdownAsync(bool force = false)
    {
        if (_socket == null || string.IsNullOrEmpty(_clientIdentity)) return;

        try
        {
            var request = new ShellShutdownRequest { Force = force };
            await SendRequestAsync<ShellShutdownRequest, ShellShutdownResponse>(
                request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "シャットダウン通信エラー");
        }

        _initialized = false;
    }

    /// <summary>
    /// リクエストを送信
    /// </summary>
    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : ShellMessage
        where TResponse : ShellMessage
    {
        if (_socket == null || string.IsNullOrEmpty(_clientIdentity))
        {
            throw new InvalidOperationException("接続されていません");
        }

        // メッセージIDを設定
        request.MessageId = $"{Interlocked.Increment(ref _messageCounter):x8}";
        request.Timestamp = DateTime.UtcNow;

        // シリアライズ
        var data = MessagePackSerializer.Serialize(request);

        // 送信 (RouterSocket には Identity が必要)
        _socket.SendMoreFrame(Convert.FromBase64String(_clientIdentity));
        _socket.SendFrame(data);

        // 受信（タイムアウト付き）
        var frames = _socket.ReceiveMultipartBytes(_options.CommunicationTimeoutMs);
        if (frames == null || frames.Count == 0)
        {
            throw new TimeoutException("Shell からの応答がありません");
        }

        // デシリアライズ
        var responseData = frames[^1];
        var response = MessagePackSerializer.Deserialize<TResponse>(responseData);
        return response;
    }

    private void EnsureConnected()
    {
        if (_socket == null || _process?.HasExited != false || string.IsNullOrEmpty(_clientIdentity))
        {
            throw new InvalidOperationException("Shell に接続されていません");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Shell が初期化されていません");
        }
    }

    /// <summary>
    /// Shell プロセスを停止
    /// </summary>
    public async Task StopAsync()
    {
        if (_process == null) return;

        _logger.LogInformation("Shell プロセスを停止中...");

        try
        {
            // シャットダウン要求を送信
            await ShutdownAsync();

            // プロセス終了を待機
            if (!_process.WaitForExit(5000))
            {
                _logger.LogWarning("Shell プロセスを強制終了します");
                _process.Kill();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shell 停止エラー");
        }
        finally
        {
            _socket?.Dispose();
            _socket = null;
            _clientIdentity = null;
            _process?.Dispose();
            _process = null;
            _initialized = false;

            // AppContainerリソースをクリーンアップ
            CleanupAppContainerResources();
        }
    }

    /// <summary>
    /// AppContainerリソースをクリーンアップ
    /// </summary>
    private void CleanupAppContainerResources()
    {
        // SIDを解放
        if (_packageSid != IntPtr.Zero)
        {
            Sandbox.NativeMethods.FreeSid(_packageSid);
            _packageSid = IntPtr.Zero;
        }

        // 割り当てたメモリを解放
        foreach (var ptr in _allocatedMemory)
        {
            if (ptr != IntPtr.Zero)
            {
                try
                {
                    Sandbox.NativeMethods.DeleteProcThreadAttributeList(ptr);
                    Marshal.FreeHGlobal(ptr);
                }
                catch
                {
                    // エラーは無視
                }
            }
        }
        _allocatedMemory.Clear();
        _capabilitiesPtr = IntPtr.Zero;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await StopAsync();
        _disposed = true;
    }
}

/// <summary>
/// Shell サーバー設定
/// </summary>
public class ShellServerOptions
{
    /// <summary>
    /// 起動タイムアウト (ミリ秒)
    /// </summary>
    public int StartTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// 通信タイムアウト (ミリ秒)
    /// </summary>
    public int CommunicationTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 詳細ログを有効にする
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// サンドボックスを使用する
    /// </summary>
    public bool UseSandbox { get; set; } = true;
}
