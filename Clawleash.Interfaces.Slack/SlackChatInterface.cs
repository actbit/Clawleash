using Clawleash.Abstractions.Services;
using Microsoft.Extensions.Logging;
using SlackNet;
using SlackNet.Events;
using SlackNet.SocketMode;

namespace Clawleash.Interfaces.Slack;

/// <summary>
/// Slack Bot チャットインターフェース
/// Socket Modeを使用したリアルタイム通信
/// </summary>
public class SlackChatInterface : IChatInterface
{
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackChatInterface>? _logger;
    private readonly SlackSocketModeClient _client;
    private readonly Dictionary<string, string> _channelTracking = new();
    private bool _disposed;
    private bool _isConnected;

    public string Name => "Slack";
    public bool IsConnected => _isConnected && _client.IsConnected;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public SlackChatInterface(
        SlackSettings settings,
        ILogger<SlackChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        var slackServices = new SlackServiceBuilder()
            .UseApiToken(settings.BotToken)
            .UseAppLevelToken(settings.AppToken)
            .RegisterEventHandler<MessageEvent>(OnMessageReceivedAsync);

        _client = slackServices.GetSocketModeClient();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BotToken))
        {
            _logger?.LogWarning("Slack bot token is not configured");
            return;
        }

        if (string.IsNullOrEmpty(_settings.AppToken))
        {
            _logger?.LogWarning("Slack app token is not configured");
            return;
        }

        try
        {
            await _client.Connect();
            _isConnected = true;

            _logger?.LogInformation("Slack bot connected via Socket Mode");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect Slack bot");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _isConnected = false;
            await _client.Close();
            _logger?.LogInformation("Slack bot disconnected");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disconnecting Slack bot");
        }
    }

    private async Task OnMessageReceivedAsync(MessageEvent message)
    {
        // 自分のメッセージやBotのメッセージは無視
        if (message.SubType == "bot_message" || string.IsNullOrEmpty(message.User))
            return;

        // 編集メッセージは無視
        if (message.SubType == "message_changed")
            return;

        var content = message.Text ?? string.Empty;

        // メンションを削除
        content = System.Text.RegularExpressions.Regex.Replace(content, "<@[A-Z0-9]+>", "").Trim();

        if (string.IsNullOrWhiteSpace(content))
            return;

        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = message.Ts ?? Guid.NewGuid().ToString(),
            SenderId = message.User,
            SenderName = "",  // API呼び出しで取得が必要
            Content = content,
            ChannelId = message.Channel,
            Timestamp = ParseSlackTimestamp(message.Ts),
            InterfaceName = Name,
            Metadata = new Dictionary<string, object>
            {
                ["ThreadTs"] = message.ThreadTs ?? "",
                ["ChannelType"] = message.ChannelType ?? "",
                ["IsDirectMessage"] = message.ChannelType == "im"
            }
        };

        // チャンネル追跡
        _channelTracking[args.MessageId] = args.ChannelId;

        _logger?.LogDebug("Received Slack message from {SenderId}: {Content}",
            args.SenderId, args.Content);

        MessageReceived?.Invoke(this, args);

        await Task.CompletedTask;
    }

    private static DateTime ParseSlackTimestamp(string? ts)
    {
        if (string.IsNullOrEmpty(ts))
            return DateTime.UtcNow;

        try
        {
            var parts = ts.Split('.');
            if (parts.Length > 0 && long.TryParse(parts[0], out var unixTime))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
            }
        }
        catch
        {
            // ignore
        }

        return DateTime.UtcNow;
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Slack client is not connected");
            return;
        }

        // 返信先のチャンネルを特定
        string? channelId = null;
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var trackedChannel))
        {
            channelId = trackedChannel;
        }

        if (string.IsNullOrEmpty(channelId))
        {
            _logger?.LogWarning("Cannot determine channel for reply");
            return;
        }

        try
        {
            var api = new SlackApiClient(_settings.BotToken);
            await api.Chat.PostMessage(channelId, message, threadTs: replyToMessageId);

            _logger?.LogDebug("Sent message to Slack channel {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Slack");
        }
    }

    /// <summary>
    /// 指定したチャンネルにメッセージを送信
    /// </summary>
    public async Task SendMessageToChannelAsync(string channelId, string message,
        string? threadTs = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Slack client is not connected");
            return;
        }

        try
        {
            var api = new SlackApiClient(_settings.BotToken);
            await api.Chat.PostMessage(channelId, message, threadTs: threadTs);

            _logger?.LogDebug("Sent message to Slack channel {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Slack channel {ChannelId}", channelId);
        }
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        // Slackはストリーミングメッセージに対応していないため、蓄積して一括送信
        return new SlackStreamingWriter(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync();
        _client.Dispose();
    }
}

/// <summary>
/// Slack用ストリーミングライター
/// </summary>
internal class SlackStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private bool _disposed;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public SlackStreamingWriter(SlackChatInterface slackInterface)
    {
        // Slack interface reference for future use
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        // 完了時にメッセージを送信する実装が必要
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
