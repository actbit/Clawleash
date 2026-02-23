using System.Text;
using Clawleash.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.Slack;

/// <summary>
/// Slack Bot チャットインターフェース
/// TODO: 完全なSlackNet統合が必要
/// </summary>
public class SlackChatInterface : IChatInterface
{
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackChatInterface>? _logger;
    private readonly Dictionary<string, string> _channelTracking = new();
    private bool _disposed;
    private bool _isConnected;

    public string Name => "Slack";
    public bool IsConnected => _isConnected;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public SlackChatInterface(
        SlackSettings settings,
        ILogger<SlackChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BotToken))
        {
            _logger?.LogWarning("Slack bot token is not configured");
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(_settings.AppToken))
        {
            _logger?.LogWarning("Slack app token is not configured");
            return Task.CompletedTask;
        }

        // TODO: SlackNet Socket Mode接続の実装
        // 現在は基本構造のみ
        _logger?.LogWarning("Slack interface is not fully implemented yet");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        _logger?.LogInformation("Slack bot disconnected");
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("SendMessageAsync called (not implemented)");
        return Task.CompletedTask;
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new SlackStreamingWriter(this);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _isConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Slack用ストリーミングライター
/// </summary>
internal class SlackStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly SlackChatInterface _slackInterface;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public SlackStreamingWriter(SlackChatInterface slackInterface)
    {
        _slackInterface = slackInterface;
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
