# Clawleash.Abstractions

A library defining shared interfaces and abstractions for Clawleash. Provides the foundation for creating custom chat interface providers.

## Overview

This library provides:

- **IChatInterface**: Chat interface abstraction
- **ChatMessageReceivedEventArgs**: Message received event arguments
- **IStreamingMessageWriter**: For streaming message sending
- **ChatInterfaceSettingsBase**: Base class for settings
- **IE2eeProvider**: E2EE encryption provider

---

## Creating Custom Providers

### 1. Project Creation

Create a new class library project and reference `Clawleash.Abstractions`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Clawleash.Abstractions\Clawleash.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

### 2. Creating Settings Class

Create a settings class inheriting from `ChatInterfaceSettingsBase`:

```csharp
using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.MyProvider;

public class MyProviderSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// Server URL to connect to
    /// </summary>
    public string ServerUrl { get; set; } = "https://api.example.com";

    /// <summary>
    /// API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Reconnection interval (milliseconds)
    /// </summary>
    public int ReconnectIntervalMs { get; set; } = 5000;
}
```

### 3. Implementing IChatInterface

```csharp
using System.Text;
using Clawleash.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.MyProvider;

public class MyProviderChatInterface : IChatInterface
{
    private readonly MyProviderSettings _settings;
    private readonly ILogger<MyProviderChatInterface>? _logger;
    private bool _isConnected;
    private bool _disposed;

    // Required properties
    public string Name => "MyProvider";
    public bool IsConnected => _isConnected;

    // Required event
    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public MyProviderChatInterface(
        MyProviderSettings settings,
        ILogger<MyProviderChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    // Required method: Start interface
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _logger?.LogWarning("API key is not configured");
            return;
        }

        try
        {
            // Implement connection logic
            await ConnectAsync(cancellationToken);
            _isConnected = true;
            _logger?.LogInformation("{Name} connected", Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start {Name}", Name);
            throw;
        }
    }

    // Required method: Stop interface
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        // Implement disconnection logic
        await Task.CompletedTask;
        _logger?.LogInformation("{Name} stopped", Name);
    }

    // Required method: Send message
    public async Task SendMessageAsync(
        string message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("{Name} is not connected", Name);
            return;
        }

        // Implement message sending logic
        // If replyToMessageId is present, send as a reply
        await Task.CompletedTask;
    }

    // Required method: Start streaming message
    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new MyStreamingWriter(this);
    }

    // Required method: Dispose resources
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
    }

    // Internal method: Call when message is received
    protected virtual void OnMessageReceived(string content, string senderId, string senderName)
    {
        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            ChannelId = "default",
            Timestamp = DateTime.UtcNow,
            InterfaceName = Name,
            RequiresReply = true,
            Metadata = new Dictionary<string, object>
            {
                // Provider-specific metadata
                ["custom_field"] = "value"
            }
        };

        MessageReceived?.Invoke(this, args);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        // Actual connection logic
        await Task.CompletedTask;
    }
}

// Streaming writer implementation
internal class MyStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly MyProviderChatInterface _interface;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public MyStreamingWriter(MyProviderChatInterface iface)
    {
        _interface = iface;
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var message = _content.ToString();
        if (!string.IsNullOrEmpty(message))
        {
            await _interface.SendMessageAsync(message, null, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## Provider Deployment

### Build

```bash
cd Clawleash.Interfaces.MyProvider
dotnet build -c Release
```

### Installation

Copy DLL and dependencies to the interfaces directory:

```
%LocalAppData%\Clawleash\Interfaces\MyProvider\
├── Clawleash.Interfaces.MyProvider.dll
├── Clawleash.Abstractions.dll
└── (other dependency DLLs)
```

### Auto-Recognition

When files are placed while the application is running, they are automatically loaded (when hot-reload is enabled).

---

## Settings Integration

### Adding to appsettings.json

```json
{
  "ChatInterface": {
    "EnableHotReload": true,
    "MyProvider": {
      "Enabled": true,
      "ServerUrl": "https://api.example.com",
      "ApiKey": "${MY_PROVIDER_API_KEY}",
      "EnableE2ee": false
    }
  }
}
```

### DI Registration

Providers are instantiated using `ActivatorUtilities.CreateInstance`, so register settings and logger in DI:

```csharp
// Register settings in Program.cs
services.Configure<MyProviderSettings>(
    configuration.GetSection("ChatInterface:MyProvider"));
```

---

## ChatMessageReceivedEventArgs Properties

| Property | Type | Description |
|-----------|-----|------|
| `MessageId` | `string` | Unique message identifier |
| `SenderId` | `string` | Unique sender identifier |
| `SenderName` | `string` | Sender display name |
| `Content` | `string` | Message content |
| `ChannelId` | `string` | Channel/Room ID |
| `Timestamp` | `DateTime` | Message timestamp (UTC) |
| `ReplyToMessageId` | `string?` | Reply target message ID |
| `RequiresReply` | `bool` | Whether reply is required |
| `InterfaceName` | `string` | Interface name |
| `Metadata` | `Dictionary<string, object>` | Provider-specific metadata |

---

## IChatInterface Members

### Properties

| Property | Type | Description |
|-----------|-----|------|
| `Name` | `string` | Interface name (unique) |
| `IsConnected` | `bool` | Connection state |

### Events

| Event | Description |
|----------|------|
| `MessageReceived` | Raised when message is received |

### Methods

| Method | Description |
|----------|------|
| `StartAsync(CancellationToken)` | Start interface |
| `StopAsync(CancellationToken)` | Stop interface |
| `SendMessageAsync(message, replyToMessageId?, CancellationToken)` | Send message |
| `StartStreamingMessage(CancellationToken)` | Start streaming message |
| `DisposeAsync()` | Release resources |

---

## E2EE Support

To support E2EE (End-to-End Encryption):

### 1. Using IE2eeProvider

```csharp
using Clawleash.Abstractions.Security;

public class MySecureProvider : IChatInterface
{
    private readonly IE2eeProvider _e2eeProvider;

    public MySecureProvider(MyProviderSettings settings, IE2eeProvider e2eeProvider)
    {
        _e2eeProvider = e2eeProvider;
    }

    public async Task SendMessageAsync(string message, ...)
    {
        if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
        {
            var ciphertext = await _e2eeProvider.EncryptAsync(message, channelId);
            // Send encrypted data
        }
        else
        {
            // Send plaintext
        }
    }
}
```

### 2. Enable in Settings

```json
{
  "ChatInterface": {
    "MyProvider": {
      "Enabled": true,
      "EnableE2ee": true
    }
  }
}
```

---

## Best Practices

### Error Handling

```csharp
public async Task StartAsync(CancellationToken cancellationToken = default)
{
    try
    {
        await ConnectAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Connection failed");
        // Update connection state
        _isConnected = false;
        throw;
    }
}
```

### Logging

```csharp
// Use structured logging
_logger?.LogInformation("{Name} starting with server: {ServerUrl}", Name, _settings.ServerUrl);
_logger?.LogDebug("Message received: {MessageId} from {SenderName}", args.MessageId, args.SenderName);
```

### Resource Management

```csharp
public async ValueTask DisposeAsync()
{
    if (_disposed) return;
    _disposed = true;

    // Unsubscribe from events
    // _client.OnMessage -= OnMessage;

    // Disconnect
    await StopAsync();

    // Release other resources
    _httpClient?.Dispose();
}
```

---

## Reference Implementations

For implementation reference, see these providers:

- [Clawleash.Interfaces.Discord](../Clawleash.Interfaces.Discord/README-en.md) - Discord Bot
- [Clawleash.Interfaces.Slack](../Clawleash.Interfaces.Slack/README-en.md) - Slack Bot
- [Clawleash.Interfaces.WebSocket](../Clawleash.Interfaces.WebSocket/README-en.md) - WebSocket (E2EE)
- [Clawleash.Interfaces.WebRTC](../Clawleash.Interfaces.WebRTC/README-en.md) - WebRTC (E2EE)

---

## License

MIT
