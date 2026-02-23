using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Clawleash.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.Slack;

/// <summary>
/// Slack Bot チャットインターフェース
/// HTTP API + ポーリングによる完全実装
/// </summary>
public class SlackChatInterface : IChatInterface
{
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackChatInterface>? _logger;
    private readonly ConcurrentDictionary<string, ChannelInfo> _channelTracking = new();
    private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();
    private readonly ConcurrentDictionary<string, bool> _monitoredChannels = new();
    private readonly HashSet<string> _subscribedChannels = new();
    private HttpClient? _httpClient;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _disposed;
    private bool _isConnected;
    private string? _botUserId;
    private DateTime _lastPollTime;

    public string Name => "Slack";
    public bool IsConnected => _isConnected && _httpClient != null;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public SlackChatInterface(
        SlackSettings settings,
        ILogger<SlackChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _lastPollTime = DateTime.UtcNow;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BotToken))
        {
            _logger?.LogWarning("Slack bot token is not configured");
            return;
        }

        try
        {
            // HTTPクライアントを初期化
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.BotToken}");

            // Bot情報を取得
            var authResult = await CallApiAsync<AuthTestResponse>("auth.test");
            if (authResult == null || !authResult.Ok)
            {
                _logger?.LogError("Failed to authenticate with Slack");
                return;
            }

            _botUserId = authResult.UserId;
            _logger?.LogInformation("Slack bot authenticated as {User} ({UserId})",
                authResult.User, authResult.UserId);

            _isConnected = true;

            // ポーリングを開始
            _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = Task.Run(() => PollMessagesAsync(_pollingCts.Token), _pollingCts.Token);

            _logger?.LogInformation("Slack interface started with polling");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Slack bot");
            throw;
        }
    }

    /// <summary>
    /// チャンネルを監視対象に追加
    /// </summary>
    public async Task<bool> JoinChannelAsync(string channelId)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("Slack client is not connected");
            return false;
        }

        try
        {
            // チャンネル情報を取得
            var infoResult = await CallApiAsync<ChannelInfoResponse>("conversations.info",
                new Dictionary<string, string> { ["channel"] = channelId });

            if (infoResult == null || !infoResult.Ok)
            {
                _logger?.LogWarning("Failed to get channel info: {ChannelId}", channelId);
                return false;
            }

            _monitoredChannels[channelId] = true;
            _logger?.LogInformation("Joined channel: {ChannelName} ({ChannelId})",
                infoResult.Channel?.Name, channelId);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to join channel {ChannelId}", channelId);
            return false;
        }
    }

    /// <summary>
    /// チャンネルを監視対象から削除
    /// </summary>
    public void LeaveChannel(string channelId)
    {
        _monitoredChannels.TryRemove(channelId, out _);
        _logger?.LogInformation("Left channel: {ChannelId}", channelId);
    }

    /// <summary>
    /// メッセージをポーリング
    /// </summary>
    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                // 監視中のチャンネルのメッセージを取得
                foreach (var channelId in _monitoredChannels.Keys)
                {
                    await PollChannelMessagesAsync(channelId, cancellationToken);
                }

                // 5秒間隔でポーリング
                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during message polling");
                await Task.Delay(10000, cancellationToken);
            }
        }
    }

    private async Task PollChannelMessagesAsync(string channelId, CancellationToken cancellationToken)
    {
        try
        {
            var messages = await CallApiAsync<ConversationsHistoryResponse>("conversations.history",
                new Dictionary<string, string>
                {
                    ["channel"] = channelId,
                    ["oldest"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(),
                    ["limit"] = "100"
                });

            if (messages == null || !messages.Ok || messages.Messages == null)
                return;

            foreach (var msg in messages.Messages.OrderByDescending(m => m.Ts))
            {
                // 既に処理済みのメッセージはスキップ
                if (_processedMessages.ContainsKey(msg.Ts))
                    continue;

                // 自分のメッセージはスキップ
                if (msg.User == _botUserId)
                    continue;

                // Botのメッセージはスキップ
                if (!string.IsNullOrEmpty(msg.BotId))
                    continue;

                // サブタイプがあるメッセージはスキップ
                if (!string.IsNullOrEmpty(msg.Subtype))
                    continue;

                // メッセージを処理
                await ProcessMessageAsync(msg, channelId);

                // 処理済みとしてマーク（10分間保持）
                _processedMessages[msg.Ts] = DateTime.UtcNow;
            }

            // 古い処理済みメッセージをクリーンアップ
            CleanupProcessedMessages();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error polling messages from channel {ChannelId}", channelId);
        }
    }

    private async Task ProcessMessageAsync(SlackMessage msg, string channelId)
    {
        try
        {
            // チャンネル情報を追跡
            var channelInfo = new ChannelInfo
            {
                ChannelId = channelId,
                ThreadTs = msg.ThreadTs ?? msg.Ts,
                UserId = msg.User
            };
            _channelTracking[msg.Ts] = channelInfo;

            // ユーザー名を取得
            string senderName = msg.User;
            try
            {
                var userInfo = await CallApiAsync<UserInfoResponse>("users.info",
                    new Dictionary<string, string> { ["user"] = msg.User });

                if (userInfo?.Ok == true && userInfo.User != null)
                {
                    senderName = userInfo.User.Profile?.DisplayName
                                 ?? userInfo.User.Profile?.RealName
                                 ?? userInfo.User.Name
                                 ?? msg.User;
                }
            }
            catch
            {
                // ユーザー情報取得に失敗した場合はIDを使用
            }

            var args = new ChatMessageReceivedEventArgs
            {
                MessageId = msg.Ts,
                SenderId = msg.User,
                SenderName = senderName,
                Content = msg.Text,
                ChannelId = channelId,
                Timestamp = ParseSlackTimestamp(msg.Ts),
                InterfaceName = Name,
                RequiresReply = true,
                Metadata = new Dictionary<string, object>
                {
                    ["ThreadTs"] = channelInfo.ThreadTs,
                    ["IsThread"] = !string.IsNullOrEmpty(msg.ThreadTs)
                }
            };

            _logger?.LogDebug("Received Slack message from {Sender}: {Content}",
                args.SenderName,
                args.Content.Length > 50 ? args.Content[..50] + "..." : args.Content);

            MessageReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing Slack message");
        }
    }

    private void CleanupProcessedMessages()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var keysToRemove = _processedMessages
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _processedMessages.TryRemove(key, out _);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _pollingCts?.Cancel();

        if (_pollingTask != null)
        {
            try
            {
                await Task.WhenAny(_pollingTask, Task.Delay(5000, cancellationToken));
            }
            catch (OperationCanceledException) { }
        }

        _isConnected = false;
        _httpClient?.Dispose();
        _httpClient = null;
        _logger?.LogInformation("Slack bot disconnected");
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _httpClient == null)
        {
            _logger?.LogWarning("Slack client is not connected");
            return;
        }

        try
        {
            string? channelId = null;
            string? threadTs = null;

            // 返信先がある場合、チャンネル情報を取得
            if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var info))
            {
                channelId = info.ChannelId;
                threadTs = info.ThreadTs;
            }

            if (string.IsNullOrEmpty(channelId))
            {
                _logger?.LogWarning("No channel found for reply");
                return;
            }

            // スレッドで返信するかどうか
            var replyTargetTs = _settings.UseThreads ? (threadTs ?? replyToMessageId) : null;

            var result = await CallApiAsync<PostMessageResponse>("chat.postMessage",
                new Dictionary<string, string>
                {
                    ["channel"] = channelId,
                    ["text"] = message,
                    ["thread_ts"] = replyTargetTs ?? string.Empty
                });

            if (result?.Ok == true)
            {
                _logger?.LogDebug("Sent message to Slack channel {ChannelId}", channelId);
            }
            else
            {
                _logger?.LogWarning("Failed to send Slack message: {Error}", result?.Error);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Slack");
        }
    }

    /// <summary>
    /// 指定したチャンネルに直接メッセージを送信
    /// </summary>
    public async Task SendMessageToChannelAsync(string channelId, string message,
        string? threadTs = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _httpClient == null)
        {
            _logger?.LogWarning("Slack client is not connected");
            return;
        }

        try
        {
            var result = await CallApiAsync<PostMessageResponse>("chat.postMessage",
                new Dictionary<string, string>
                {
                    ["channel"] = channelId,
                    ["text"] = message,
                    ["thread_ts"] = threadTs ?? string.Empty
                });

            if (result?.Ok == true)
            {
                _logger?.LogDebug("Sent message to Slack channel {ChannelId}", channelId);
            }
            else
            {
                _logger?.LogWarning("Failed to send Slack message: {Error}", result?.Error);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message to Slack channel {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// DMを送信
    /// </summary>
    public async Task SendDirectMessageAsync(string userId, string message,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _httpClient == null)
        {
            _logger?.LogWarning("Slack client is not connected");
            return;
        }

        try
        {
            // DMチャンネルを開く
            var imResult = await CallApiAsync<ImOpenResponse>("im.open",
                new Dictionary<string, string> { ["user"] = userId });

            if (imResult?.Ok != true || imResult.Channel == null)
            {
                _logger?.LogWarning("Failed to open DM with user {UserId}: {Error}", userId, imResult?.Error);
                return;
            }

            await SendMessageToChannelAsync(imResult.Channel.Id, message, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send DM to user {UserId}", userId);
        }
    }

    /// <summary>
    /// チャンネル一覧を取得
    /// </summary>
    public async Task<List<SlackChannel>> GetChannelsAsync()
    {
        if (!IsConnected)
            return new List<SlackChannel>();

        try
        {
            var result = await CallApiAsync<ChannelsListResponse>("conversations.list",
                new Dictionary<string, string>
                {
                    ["types"] = "public_channel,private_channel",
                    ["limit"] = "1000"
                });

            if (result?.Ok == true && result.Channels != null)
            {
                return result.Channels.Select(c => new SlackChannel
                {
                    Id = c.Id,
                    Name = c.Name,
                    IsPrivate = c.IsPrivate
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get channels");
        }

        return new List<SlackChannel>();
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new SlackStreamingWriter(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync();
        _pollingCts?.Dispose();
    }

    private async Task<T?> CallApiAsync<T>(string method, Dictionary<string, string>? parameters = null)
        where T : class
    {
        if (_httpClient == null)
            return null;

        try
        {
            var url = $"https://slack.com/api/{method}";

            if (parameters != null && parameters.Count > 0)
            {
                var queryString = string.Join("&",
                    parameters.Where(p => !string.IsNullOrEmpty(p.Value))
                              .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                url += $"?{queryString}";
            }

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Slack API call failed: {Method}, Status: {Status}",
                    method, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error calling Slack API: {Method}", method);
            return null;
        }
    }

    private static DateTime ParseSlackTimestamp(string ts)
    {
        try
        {
            var parts = ts.Split('.');
            var seconds = long.Parse(parts[0]);
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private class ChannelInfo
    {
        public string ChannelId { get; set; } = string.Empty;
        public string ThreadTs { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }
}

#region Slack API Response Types

internal class AuthTestResponse
{
    public bool Ok { get; set; }
    public string? UserId { get; set; }
    public string? User { get; set; }
    public string? Team { get; set; }
    public string? Error { get; set; }
}

internal class ChannelInfoResponse
{
    public bool Ok { get; set; }
    public SlackChannelInfo? Channel { get; set; }
}

internal class SlackChannelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
}

internal class ConversationsHistoryResponse
{
    public bool Ok { get; set; }
    public List<SlackMessage>? Messages { get; set; }
}

internal class SlackMessage
{
    public string Ts { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ThreadTs { get; set; }
    public string? Subtype { get; set; }
    public string? BotId { get; set; }
}

internal class UserInfoResponse
{
    public bool Ok { get; set; }
    public SlackUser? User { get; set; }
}

internal class SlackUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SlackUserProfile? Profile { get; set; }
}

internal class SlackUserProfile
{
    public string? DisplayName { get; set; }
    public string? RealName { get; set; }
}

internal class PostMessageResponse
{
    public bool Ok { get; set; }
    public string? Ts { get; set; }
    public string? Error { get; set; }
}

internal class ImOpenResponse
{
    public bool Ok { get; set; }
    public SlackChannelInfo? Channel { get; set; }
    public string? Error { get; set; }
}

internal class ChannelsListResponse
{
    public bool Ok { get; set; }
    public List<SlackChannelInfo>? Channels { get; set; }
}

#endregion

/// <summary>
/// Slackチャンネル情報
/// </summary>
public class SlackChannel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
}

/// <summary>
/// Slack用ストリーミングライター
/// </summary>
internal class SlackStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly SlackChatInterface _slackInterface;
    private string? _channelId;
    private string? _threadTs;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public SlackStreamingWriter(SlackChatInterface slackInterface)
    {
        _slackInterface = slackInterface;
    }

    public void SetChannel(string channelId, string? threadTs = null)
    {
        _channelId = channelId;
        _threadTs = threadTs;
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var message = _content.ToString();
        if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(_channelId))
        {
            await _slackInterface.SendMessageToChannelAsync(_channelId, message, _threadTs, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
