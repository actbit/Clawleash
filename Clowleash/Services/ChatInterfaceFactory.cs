using Clowleash.Configuration;

namespace Clowleash.Services;

/// <summary>
/// チャットインターフェースを管理するサービス
/// 複数のインターフェースを統合し、メッセージをエージェントに振り分ける
/// </summary>
public class ChatInterfaceManager : IAsyncDisposable
{
    private readonly List<IChatInterface> _interfaces = new();
    private readonly Func<ChatMessageReceivedEventArgs, Task<string>> _messageHandler;
    private bool _disposed;

    /// <summary>
    /// アクティブなインターフェースの数
    /// </summary>
    public int ActiveInterfaceCount => _interfaces.Count(i => i.IsConnected);

    public ChatInterfaceManager(Func<ChatMessageReceivedEventArgs, Task<string>> messageHandler)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
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

        chatInterface.MessageReceived += OnMessageReceived;
        _interfaces.Add(chatInterface);
    }

    /// <summary>
    /// すべてのインターフェースを開始する
    /// </summary>
    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var iface in _interfaces)
        {
            try
            {
                await iface.StartAsync(cancellationToken);
                Console.WriteLine($"Started interface: {iface.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start interface {iface.Name}: {ex.Message}");
            }
        }
    }

    private async void OnMessageReceived(object? sender, ChatMessageReceivedEventArgs e)
    {
        try
        {
            // エージェントにメッセージを処理させる
            var response = await _messageHandler(e);

            // 必要に応じて返信
            if (e.RequiresReply && sender is IChatInterface iface)
            {
                await iface.SendMessageAsync(response, e.MessageId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing message: {ex.Message}");

            if (sender is IChatInterface iface)
            {
                await iface.SendMessageAsync($"エラーが発生しました: {ex.Message}", e.MessageId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var iface in _interfaces)
        {
            iface.MessageReceived -= OnMessageReceived;
            await iface.DisposeAsync();
        }

        _interfaces.Clear();
        _disposed = true;
    }
}

/// <summary>
/// チャットインターフェースのファクトリー
/// </summary>
public static class ChatInterfaceFactory
{
    /// <summary>
    /// 設定に基づいてチャットインターフェースを作成する
    /// </summary>
    public static IChatInterface Create(ChatInterfaceType type)
    {
        return type switch
        {
            ChatInterfaceType.Cli => new CliChatInterface(),
            // 将来的にDiscordやSlackを追加
            // ChatInterfaceType.Discord => new DiscordChatInterface(),
            // ChatInterfaceType.Slack => new SlackChatInterface(),
            _ => throw new NotSupportedException($"Unsupported interface type: {type}")
        };
    }
}

/// <summary>
/// チャットインターフェースの種類
/// </summary>
public enum ChatInterfaceType
{
    Cli,
    Discord,
    Slack,
    Teams,
    WebRtc
}
