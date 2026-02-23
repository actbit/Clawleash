using MessagePack;
using NetMQ;
using NetMQ.Sockets;
using Clawleash.Shell.Hosting;
using Clawleash.Contracts;
using Microsoft.Extensions.Logging;

namespace Clawleash.Shell.IPC;

/// <summary>
/// ZeroMQ + MessagePack によるIPCクライアント
/// Main アプリに接続してコマンドを受信・実行する
/// </summary>
public class IpcClient : IDisposable
{
    private readonly ILogger<IpcClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConstrainedRunspaceHost _runspaceHost;
    private DealerSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _clientTask;
    private bool _disposed;
    private bool _isConnected;

    public string? ServerAddress { get; private set; }
    public bool IsConnected => _isConnected;

    public IpcClient(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<IpcClient>();
        _runspaceHost = new ConstrainedRunspaceHost(loggerFactory.CreateLogger<ConstrainedRunspaceHost>());
    }

    /// <summary>
    /// サーバー（Main アプリ）に接続
    /// </summary>
    public async Task<bool> ConnectAsync(string serverAddress)
    {
        if (_isConnected)
        {
            throw new InvalidOperationException("既に接続済みです");
        }

        _logger.LogInformation("Main アプリに接続中: {Address}", serverAddress);

        try
        {
            _socket = new DealerSocket();
            _socket.Connect(serverAddress);
            ServerAddress = serverAddress;

            _cts = new CancellationTokenSource();
            _isConnected = true;

            // メッセージ受信ループを開始
            _clientTask = Task.Run(() => ClientLoop(_cts.Token));

            _logger.LogInformation("接続完了");

            // 準備完了を通知
            await SendReadyAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "接続エラー");
            return false;
        }
    }

    /// <summary>
    /// 準備完了を通知
    /// </summary>
    private async Task SendReadyAsync()
    {
        var readyMessage = new ShellReadyMessage
        {
            ProcessId = Environment.ProcessId,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        };

        await SendMessageAsync(readyMessage);
        _logger.LogDebug("準備完了通知を送信");
    }

    /// <summary>
    /// クライアントループ（コマンド受信）
    /// </summary>
    private async Task ClientLoop(CancellationToken cancellationToken)
    {
        _logger.LogInformation("クライアントループ開始");

        while (!cancellationToken.IsCancellationRequested && _socket != null)
        {
            try
            {
                var data = await Task.Run(() => _socket.ReceiveFrameBytes(), cancellationToken);
                var response = await ProcessMessageAsync(data);
                _socket.SendFrame(response);
            }
            catch (NetMQException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("クライアントループ終了 (キャンセル)");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "クライアントループエラー");
                await Task.Delay(100, cancellationToken);
            }
        }

        _logger.LogInformation("クライアントループ終了");
    }

    /// <summary>
    /// メッセージを送信
    /// </summary>
    private async Task SendMessageAsync<T>(T message) where T : class
    {
        if (_socket == null) return;

        try
        {
            var data = MessagePackSerializer.Serialize(message);
            _socket.SendFrame(data);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ送信エラー");
        }
    }

    /// <summary>
    /// メッセージを処理
    /// </summary>
    private async Task<byte[]> ProcessMessageAsync(byte[] data)
    {
        try
        {
            var header = MessagePackSerializer.Deserialize<MessageHeader>(data);

            return header.Type switch
            {
                nameof(ShellInitializeRequest) => await HandleMessageAsync<ShellInitializeRequest>(data),
                nameof(ShellExecuteRequest) => await HandleMessageAsync<ShellExecuteRequest>(data),
                nameof(ShellShutdownRequest) => await HandleMessageAsync<ShellShutdownRequest>(data),
                nameof(ShellPingRequest) => await HandleMessageAsync<ShellPingRequest>(data),
                nameof(ToolInvokeRequest) => await HandleMessageAsync<ToolInvokeRequest>(data),
                _ => CreateErrorResponse($"不明なメッセージタイプ: {header.Type}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ処理エラー");
            return CreateErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// 型付きメッセージを処理
    /// </summary>
    private async Task<byte[]> HandleMessageAsync<T>(byte[] data) where T : ShellMessage
    {
        var request = MessagePackSerializer.Deserialize<T>(data);
        ShellMessage response;

        switch (request)
        {
            case ShellInitializeRequest initReq:
                response = await _runspaceHost.InitializeAsync(initReq);
                break;

            case ShellExecuteRequest execReq:
                response = await _runspaceHost.ExecuteAsync(execReq);
                break;

            case ShellShutdownRequest shutdownReq:
                response = await HandleShutdownAsync(shutdownReq);
                break;

            case ShellPingRequest pingReq:
                response = HandlePing(pingReq);
                break;

            case ToolInvokeRequest toolReq:
                response = await HandleToolInvokeAsync(toolReq);
                break;

            default:
                response = new ShellExecuteResponse
                {
                    Success = false,
                    Error = $"未処理のメッセージタイプ: {typeof(T).Name}"
                };
                break;
        }

        return MessagePackSerializer.Serialize(response);
    }

    private async Task<ShellShutdownResponse> HandleShutdownAsync(ShellShutdownRequest request)
    {
        _logger.LogInformation("シャットダウン要求を受信");

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            _cts?.Cancel();
            _isConnected = false;
        });

        return new ShellShutdownResponse { Success = true };
    }

    private ShellPingResponse HandlePing(ShellPingRequest request)
    {
        return new ShellPingResponse
        {
            Payload = $"pong: {request.Payload}",
            ProcessingTimeMs = 0
        };
    }

    private async Task<ToolInvokeResponse> HandleToolInvokeAsync(ToolInvokeRequest request)
    {
        // TODO: 動的ロードしたTool DLLのメソッドを実行
        await Task.CompletedTask;

        return new ToolInvokeResponse
        {
            RequestId = request.MessageId,
            Success = false,
            Error = $"Tool '{request.ToolName}' はロードされていません"
        };
    }

    private static byte[] CreateErrorResponse(string error)
    {
        var response = new ShellExecuteResponse
        {
            Success = false,
            Error = error
        };
        return MessagePackSerializer.Serialize(response);
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;

        _logger.LogInformation("切断中...");

        _cts?.Cancel();

        if (_clientTask != null)
        {
            await _clientTask;
        }

        _socket?.Dispose();
        _socket = null;
        _isConnected = false;

        _logger.LogInformation("切断完了");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Cancel();
        _socket?.Dispose();
        _runspaceHost.Dispose();
        _cts?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// メッセージヘッダー（タイプ判別用）
/// </summary>
[MessagePackObject]
public class MessageHeader
{
    [Key(0)]
    public string Type { get; set; } = string.Empty;
}
