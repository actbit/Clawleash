using System.Collections.Concurrent;
using System.Text;
using Clawleash.Abstractions.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.Discord;

/// <summary>
/// Discord Bot チャットインターフェース
/// 完全実装：チャンネル追跡、スレッド返信、ストリーミング送信対応
/// </summary>
public class DiscordChatInterface : IChatInterface
{
    private readonly DiscordSettings _settings;
    private readonly ILogger<DiscordChatInterface>? _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConcurrentDictionary<string, DiscordChannelInfo> _channelTracking = new();
    private bool _disposed;
    private CancellationTokenSource? _cts;

    public string Name => "Discord";
    public bool IsConnected => _client.ConnectionState == ConnectionState.Connected;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public DiscordChatInterface(
        DiscordSettings settings,
        ILogger<DiscordChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.MessageContent
                             | GatewayIntents.AllUnprivileged
                             | GatewayIntents.DirectMessages
                             | GatewayIntents.GuildMessages,
            AlwaysDownloadUsers = false,
            LogLevel = LogSeverity.Info
        };

        _client = new DiscordSocketClient(config);
        _client.Log += OnLogAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.Ready += OnReadyAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Token))
        {
            _logger?.LogWarning("Discord token is not configured");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await _client.LoginAsync(TokenType.Bot, _settings.Token);
            await _client.StartAsync();

            _logger?.LogInformation("Discord bot starting...");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Discord bot");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts?.Cancel();
            await _client.StopAsync();
            await _client.LogoutAsync();

            _logger?.LogInformation("Discord bot stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error stopping Discord bot");
        }
    }

    private Task OnReadyAsync()
    {
        _logger?.LogInformation("Discord bot is ready! Logged in as {Username}#{Discriminator}",
            _client.CurrentUser?.Username, _client.CurrentUser?.Discriminator);
        return Task.CompletedTask;
    }

    private Task OnLogAsync(LogMessage logMessage)
    {
        var level = logMessage.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger?.Log(level, logMessage.Exception, "[Discord] {Message}", logMessage.Message);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        // 自分のメッセージは無視
        if (message.Author.Id == _client.CurrentUser?.Id)
            return;

        // Botのメッセージは無視
        if (message.Author.IsBot)
            return;

        // コマンドプレフィックスのチェック（DM以外）
        if (message.Channel is not IDMChannel && !string.IsNullOrEmpty(_settings.CommandPrefix))
        {
            if (!message.Content.StartsWith(_settings.CommandPrefix))
                return;
        }

        var content = message.Content;

        // プレフィックスを削除
        if (!string.IsNullOrEmpty(_settings.CommandPrefix) && content.StartsWith(_settings.CommandPrefix))
        {
            content = content[_settings.CommandPrefix.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(content))
            return;

        // チャンネル情報を追跡（返信用）
        var channelInfo = new DiscordChannelInfo
        {
            ChannelId = message.Channel.Id,
            GuildId = (message.Channel as SocketGuildChannel)?.Guild.Id,
            ChannelName = (message.Channel as SocketTextChannel)?.Name ?? "DM",
            ReferenceMessageId = message.Id,
            IsThread = message.Channel is SocketThreadChannel
        };
        _channelTracking[message.Id.ToString()] = channelInfo;

        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = message.Id.ToString(),
            SenderId = message.Author.Id.ToString(),
            SenderName = message.Author.GlobalName ?? message.Author.Username,
            Content = content,
            ChannelId = message.Channel.Id.ToString(),
            Timestamp = message.Timestamp.UtcDateTime,
            InterfaceName = Name,
            RequiresReply = true,
            Metadata = new Dictionary<string, object>
            {
                ["GuildId"] = channelInfo.GuildId?.ToString() ?? "",
                ["GuildName"] = (message.Channel as SocketGuildChannel)?.Guild.Name ?? "",
                ["ChannelName"] = channelInfo.ChannelName,
                ["IsDirectMessage"] = message.Channel is IDMChannel,
                ["IsThread"] = channelInfo.IsThread
            }
        };

        _logger?.LogDebug("Received Discord message from {Sender}: {Content}",
            args.SenderName, args.Content.Length > 50 ? args.Content[..50] + "..." : args.Content);

        MessageReceived?.Invoke(this, args);
        await Task.CompletedTask;
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Discord client is not connected");
            return;
        }

        ulong? channelId = null;
        ulong? replyMessageId = null;

        // 返信先がある場合、チャンネル情報を取得
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var info))
        {
            channelId = info.ChannelId;
            replyMessageId = info.ReferenceMessageId;
        }

        if (channelId == null)
        {
            _logger?.LogWarning("No channel found for message");
            return;
        }

        try
        {
            var channel = _client.GetChannel(channelId.Value) as IMessageChannel;
            if (channel == null)
            {
                _logger?.LogWarning("Channel not found: {ChannelId}", channelId);
                return;
            }

            // 返信として送信
            if (replyMessageId.HasValue && replyMessageId.Value != 0)
            {
                var reference = new MessageReference(replyMessageId.Value, channelId.Value);
                await channel.SendMessageAsync(message, messageReference: reference);
            }
            else
            {
                await channel.SendMessageAsync(message);
            }

            _logger?.LogDebug("Sent message to Discord channel {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Discord channel {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// 指定したチャンネルにメッセージを送信
    /// </summary>
    public async Task SendMessageToChannelAsync(ulong channelId, string message,
        ulong? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Discord client is not connected");
            return;
        }

        try
        {
            var channel = _client.GetChannel(channelId) as IMessageChannel;
            if (channel == null)
            {
                _logger?.LogWarning("Channel not found: {ChannelId}", channelId);
                return;
            }

            if (replyToMessageId.HasValue && replyToMessageId.Value != 0)
            {
                var reference = new MessageReference(replyToMessageId.Value, channelId);
                await channel.SendMessageAsync(message, messageReference: reference);
            }
            else
            {
                await channel.SendMessageAsync(message);
            }

            _logger?.LogDebug("Sent message to Discord channel {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Discord channel {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// チャンネル情報を取得
    /// </summary>
    public DiscordChannelInfo? GetChannelInfo(string messageId)
    {
        _channelTracking.TryGetValue(messageId, out var info);
        return info;
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new DiscordStreamingWriter(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await StopAsync();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing Discord client");
        }
    }
}

/// <summary>
/// Discordチャンネル情報
/// </summary>
public class DiscordChannelInfo
{
    public ulong ChannelId { get; set; }
    public ulong? GuildId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public ulong? ReferenceMessageId { get; set; }
    public bool IsThread { get; set; }
}

/// <summary>
/// Discord用ストリーミングライター
/// 蓄積して一括送信
/// </summary>
internal class DiscordStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly DiscordChatInterface _discordInterface;
    private ulong? _channelId;
    private ulong? _replyToMessageId;
    private bool _disposed;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public DiscordStreamingWriter(DiscordChatInterface discordInterface)
    {
        _discordInterface = discordInterface;
    }

    /// <summary>
    /// 送信先チャンネルを設定
    /// </summary>
    public void SetChannel(ulong channelId, ulong? replyToMessageId = null)
    {
        _channelId = channelId;
        _replyToMessageId = replyToMessageId;
    }

    /// <summary>
    /// 返信先メッセージIDから自動的にチャンネルを設定
    /// </summary>
    public bool TrySetChannelFromReply(string replyToMessageId)
    {
        var info = _discordInterface.GetChannelInfo(replyToMessageId);
        if (info != null)
        {
            _channelId = info.ChannelId;
            _replyToMessageId = info.ReferenceMessageId;
            return true;
        }
        return false;
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var message = _content.ToString();
        if (!string.IsNullOrEmpty(message) && _channelId.HasValue)
        {
            await _discordInterface.SendMessageToChannelAsync(
                _channelId.Value,
                message,
                _replyToMessageId,
                cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
