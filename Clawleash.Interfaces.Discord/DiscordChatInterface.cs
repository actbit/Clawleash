using System.Text;
using Clawleash.Abstractions.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.Discord;

/// <summary>
/// Discord Bot チャットインターフェース
/// </summary>
public class DiscordChatInterface : IChatInterface
{
    private readonly DiscordSettings _settings;
    private readonly ILogger<DiscordChatInterface>? _logger;
    private readonly DiscordSocketClient _client;
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

        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = message.Id.ToString(),
            SenderId = message.Author.Id.ToString(),
            SenderName = message.Author.GlobalName ?? message.Author.Username,
            Content = content,
            ChannelId = message.Channel.Id.ToString(),
            Timestamp = message.Timestamp.UtcDateTime,
            InterfaceName = Name,
            Metadata = new Dictionary<string, object>
            {
                ["GuildId"] = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "",
                ["GuildName"] = (message.Channel as SocketGuildChannel)?.Guild.Name ?? "",
                ["ChannelName"] = (message.Channel as SocketTextChannel)?.Name ?? "DM",
                ["IsDirectMessage"] = message.Channel is IDMChannel
            }
        };

        _logger?.LogDebug("Received Discord message from {Sender}: {Content}",
            args.SenderName, args.Content);

        MessageReceived?.Invoke(this, args);
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Discord client is not connected");
            return;
        }

        // 最後に受信したチャンネルに送信するため、MessageReceivedでチャンネルを追跡する必要がある
        // この実装では、返信先メッセージIDがある場合はそのチャンネルを使用
        // 実際の使用では、チャンネルIDを追跡する仕組みが必要

        _logger?.LogDebug("SendMessageAsync called but channel tracking not implemented");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 指定したチャンネルにメッセージを送信
    /// </summary>
    public async Task SendMessageToChannelAsync(ulong channelId, string message,
        string? replyToMessageId = null, CancellationToken cancellationToken = default)
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

            if (ulong.TryParse(replyToMessageId, out var replyId) && replyId != 0)
            {
                // 返信として送信
                var reference = new MessageReference(replyId, channelId);
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

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        // Discordはストリーミングメッセージに対応していないため、
        // 通常のメッセージ送信をラップしたwriterを返す
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
/// Discord用ストリーミングライター
/// Discordはストリーミングをサポートしていないため、蓄積して一括送信
/// </summary>
internal class DiscordStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private bool _disposed;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public DiscordStreamingWriter(DiscordChatInterface discordInterface)
    {
        // Discord interface reference for future use
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        // 完了時にメッセージを送信する実装が必要
        // 現在は何もしない
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
