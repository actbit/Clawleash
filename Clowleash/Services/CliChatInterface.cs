using System.Text;

namespace Clowleash.Services;

/// <summary>
/// CLI（コンソール）チャットインターフェース
/// 標準入出力を使用したシンプルな対話
/// </summary>
public class CliChatInterface : IChatInterface
{
    private readonly StringBuilder _currentMessage = new();
    private bool _disposed;
    private bool _isStreaming;

    public string Name => "CLI";
    public bool IsConnected { get; private set; }

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // バックグラウンドで入力を監視
        _ = Task.Run(() => MonitorInputAsync(cancellationToken), cancellationToken);

        return Task.CompletedTask;
    }

    private async Task MonitorInputAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                // プロンプト表示
                Console.Write("\nYou: ");

                var line = await Task.Run(() => Console.ReadLine(), cancellationToken);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // 終了コマンドのチェック
                if (line.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    IsConnected = false;
                    break;
                }

                // メッセージ受信イベントを発火
                var args = new ChatMessageReceivedEventArgs
                {
                    MessageId = Guid.NewGuid().ToString(),
                    SenderId = "cli-user",
                    SenderName = "User",
                    Content = line,
                    ChannelId = "cli-channel",
                    Timestamp = DateTime.UtcNow
                };

                MessageReceived?.Invoke(this, args);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error reading input: {ex.Message}");
            }
        }
    }

    public Task SendMessageAsync(string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        if (_isStreaming)
        {
            // ストリーミング中は蓄積
            _currentMessage.AppendLine(message);
        }
        else
        {
            Console.WriteLine($"\nAssistant: {message}");
        }

        return Task.CompletedTask;
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        _isStreaming = true;
        _currentMessage.Clear();
        Console.WriteLine("\nAssistant: ");

        return new CliStreamingWriter(this);
    }

    internal void StopStreaming()
    {
        _isStreaming = false;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        IsConnected = false;
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private class CliStreamingWriter : IStreamingMessageWriter
    {
        private readonly CliChatInterface _interface;
        private bool _disposed;

        public string MessageId { get; } = Guid.NewGuid().ToString();

        public CliStreamingWriter(CliChatInterface iface)
        {
            _interface = iface;
        }

        public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Console.Write(text);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            Console.WriteLine(); // 改行
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _interface.StopStreaming();
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
