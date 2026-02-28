using Clawleash.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services;

/// <summary>
/// ChatInterfaceManagerの設定
/// </summary>
public class ChatInterfaceManagerSettings
{
    /// <summary>
    /// 返信を全インターフェースにブロードキャストするかどうか
    /// falseの場合は送信元のみに返信
    /// </summary>
    public bool BroadcastReplies { get; set; } = false;
}

/// <summary>
/// チャットインターフェースを管理するサービス
/// 複数のインターフェースを統合し、メッセージをエージェントに振り分ける
/// プラグインDLLからの動的追加/削除に対応
/// </summary>
public class ChatInterfaceManager : IAsyncDisposable
{
    private readonly List<IChatInterface> _interfaces = new();
    private readonly Func<ChatMessageReceivedEventArgs, Task<string>> _messageHandler;
    private readonly ILogger<ChatInterfaceManager>? _logger;
    private readonly ChatInterfaceManagerSettings _settings;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// アクティブなインターフェースの数
    /// </summary>
    public int ActiveInterfaceCount
    {
        get
        {
            lock (_lock)
            {
                return _interfaces.Count(i => i.IsConnected);
            }
        }
    }

    /// <summary>
    /// 登録されているインターフェースの総数
    /// </summary>
    public int TotalInterfaceCount
    {
        get
        {
            lock (_lock)
            {
                return _interfaces.Count;
            }
        }
    }

    public ChatInterfaceManager(
        Func<ChatMessageReceivedEventArgs, Task<string>> messageHandler,
        ChatInterfaceManagerSettings? settings = null,
        ILogger<ChatInterfaceManager>? logger = null)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _settings = settings ?? new ChatInterfaceManagerSettings();
        _logger = logger;
    }

    /// <summary>
    /// インターフェースを追加する
    /// </summary>
    public void AddInterface(IChatInterface chatInterface)
    {
        if (chatInterface == null)
        {
            throw new ArgumentNullException(nameof(chatInterface));
        }

        lock (_lock)
        {
            // 既に追加されているかチェック
            if (_interfaces.Any(i => i.Name == chatInterface.Name && i == chatInterface))
            {
                _logger?.LogWarning("Interface already added: {Name}", chatInterface.Name);
                return;
            }

            chatInterface.MessageReceived += OnMessageReceived;
            _interfaces.Add(chatInterface);

            _logger?.LogInformation("Interface added: {Name}", chatInterface.Name);
        }
    }

    /// <summary>
    /// インターフェースを削除する
    /// </summary>
    public async Task<bool> RemoveInterfaceAsync(IChatInterface chatInterface)
    {
        if (chatInterface == null)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_interfaces.Remove(chatInterface))
            {
                _logger?.LogWarning("Interface not found for removal: {Name}", chatInterface.Name);
                return false;
            }

            chatInterface.MessageReceived -= OnMessageReceived;
        }

        _logger?.LogInformation("Interface removed: {Name}", chatInterface.Name);

        // インターフェースを停止して破棄
        try
        {
            if (chatInterface.IsConnected)
            {
                // StopAsyncが実装されている場合は呼び出す
                var stopMethod = chatInterface.GetType().GetMethod("StopAsync");
                if (stopMethod != null)
                {
                    var task = stopMethod.Invoke(chatInterface, new object[] { CancellationToken.None }) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                }
            }

            await chatInterface.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing interface: {Name}", chatInterface.Name);
        }

        return true;
    }

    /// <summary>
    /// 名前でインターフェースを取得
    /// </summary>
    public IChatInterface? GetInterface(string name)
    {
        lock (_lock)
        {
            return _interfaces.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// すべてのインターフェースを取得
    /// </summary>
    public IEnumerable<IChatInterface> GetAllInterfaces()
    {
        lock (_lock)
        {
            return _interfaces.ToList();
        }
    }

    /// <summary>
    /// すべてのインターフェースを開始する
    /// </summary>
    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        List<IChatInterface> interfacesToStart;

        lock (_lock)
        {
            interfacesToStart = _interfaces.ToList();
        }

        _logger?.LogInformation("Starting {Count} interfaces...", interfacesToStart.Count);

        foreach (var iface in interfacesToStart)
        {
            try
            {
                await iface.StartAsync(cancellationToken);
                _logger?.LogInformation("Started interface: {Name}", iface.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start interface {Name}", iface.Name);
            }
        }
    }

    /// <summary>
    /// すべてのインターフェースを停止する
    /// </summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        List<IChatInterface> interfacesToStop;

        lock (_lock)
        {
            interfacesToStop = _interfaces.ToList();
        }

        _logger?.LogInformation("Stopping {Count} interfaces...", interfacesToStop.Count);

        foreach (var iface in interfacesToStop)
        {
            try
            {
                // StopAsyncが実装されている場合は呼び出す
                var stopMethod = iface.GetType().GetMethod("StopAsync");
                if (stopMethod != null)
                {
                    var task = stopMethod.Invoke(iface, new object[] { cancellationToken }) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                }

                _logger?.LogInformation("Stopped interface: {Name}", iface.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to stop interface {Name}", iface.Name);
            }
        }
    }

    private async void OnMessageReceived(object? sender, ChatMessageReceivedEventArgs e)
    {
        try
        {
            _logger?.LogDebug("Message received from {InterfaceName} by {SenderName}: {Content}",
                e.InterfaceName, e.SenderName,
                e.Content.Length > 50 ? e.Content[..50] + "..." : e.Content);

            // エージェントにメッセージを処理させる
            var response = await _messageHandler(e);

            // 返信の送信先を決定
            if (e.RequiresReply)
            {
                if (_settings.BroadcastReplies)
                {
                    // 全インターフェースにブロードキャスト
                    await BroadcastMessageAsync(response, e.MessageId);
                }
                else if (sender is IChatInterface iface)
                {
                    // 送信元のみに返信
                    await iface.SendMessageAsync(response, e.MessageId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing message from {InterfaceName}", e.InterfaceName);

            // エラー通知の送信先を決定
            if (_settings.BroadcastReplies)
            {
                await BroadcastMessageAsync($"エラーが発生しました: {ex.Message}", e.MessageId);
            }
            else if (sender is IChatInterface iface)
            {
                try
                {
                    await iface.SendMessageAsync($"エラーが発生しました: {ex.Message}", e.MessageId);
                }
                catch (Exception sendEx)
                {
                    _logger?.LogWarning(sendEx, "Failed to send error message");
                }
            }
        }
    }

    /// <summary>
    /// 全インターフェースにメッセージをブロードキャスト
    /// </summary>
    public async Task BroadcastMessageAsync(string message, string? replyToMessageId = null)
    {
        List<IChatInterface> interfacesToBroadcast;

        lock (_lock)
        {
            interfacesToBroadcast = _interfaces.Where(i => i.IsConnected).ToList();
        }

        if (interfacesToBroadcast.Count == 0)
        {
            _logger?.LogWarning("No connected interfaces to broadcast message");
            return;
        }

        _logger?.LogDebug("Broadcasting message to {Count} interfaces", interfacesToBroadcast.Count);

        var tasks = interfacesToBroadcast.Select(async iface =>
        {
            try
            {
                await iface.SendMessageAsync(message, replyToMessageId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send message to interface {Name}", iface.Name);
            }
        });

        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<IChatInterface> interfacesToDispose;

        lock (_lock)
        {
            interfacesToDispose = _interfaces.ToList();
            _interfaces.Clear();
        }

        _logger?.LogInformation("Disposing {Count} interfaces...", interfacesToDispose.Count);

        foreach (var iface in interfacesToDispose)
        {
            try
            {
                iface.MessageReceived -= OnMessageReceived;
                await iface.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing interface {Name}", iface.Name);
            }
        }
    }
}
