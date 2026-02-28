# Clawleash.Abstractions

Clawleash の共有インターフェースと抽象化を定義するライブラリ。カスタムチャットインターフェースプロバイダーを作成するための基盤を提供します。

## 概要

このライブラリは以下を提供します：

- **IChatInterface**: チャットインターフェースの抽象化
- **ChatMessageReceivedEventArgs**: メッセージ受信イベントの引数
- **IStreamingMessageWriter**: ストリーミングメッセージ送信用
- **ChatInterfaceSettingsBase**: 設定のベースクラス
- **IE2eeProvider**: E2EE 暗号化プロバイダー

---

## カスタムプロバイダーの作成

### 1. プロジェクト作成

新しいクラスライブラリプロジェクトを作成し、`Clawleash.Abstractions` を参照します：

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

### 2. 設定クラスの作成

`ChatInterfaceSettingsBase` を継承して設定クラスを作成します：

```csharp
using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.MyProvider;

public class MyProviderSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// 接続先のサーバーURL
    /// </summary>
    public string ServerUrl { get; set; } = "https://api.example.com";

    /// <summary>
    /// API キー
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 再接続間隔（ミリ秒）
    /// </summary>
    public int ReconnectIntervalMs { get; set; } = 5000;
}
```

### 3. IChatInterface の実装

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

    // 必須プロパティ
    public string Name => "MyProvider";
    public bool IsConnected => _isConnected;

    // 必須イベント
    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public MyProviderChatInterface(
        MyProviderSettings settings,
        ILogger<MyProviderChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    // 必須メソッド: インターフェース開始
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _logger?.LogWarning("API key is not configured");
            return;
        }

        try
        {
            // 接続処理を実装
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

    // 必須メソッド: インターフェース停止
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        // 切断処理を実装
        await Task.CompletedTask;
        _logger?.LogInformation("{Name} stopped", Name);
    }

    // 必須メソッド: メッセージ送信
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

        // メッセージ送信処理を実装
        // replyToMessageId がある場合は返信として送信
        await Task.CompletedTask;
    }

    // 必須メソッド: ストリーミング送信
    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new MyStreamingWriter(this);
    }

    // 必須メソッド: リソース解放
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
    }

    // 内部メソッド: メッセージ受信時に呼び出す
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
                // プロバイダー固有のメタデータ
                ["custom_field"] = "value"
            }
        };

        MessageReceived?.Invoke(this, args);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        // 実際の接続処理
        await Task.CompletedTask;
    }
}

// ストリーミングライターの実装
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

## プロバイダーのデプロイ

### ビルド

```bash
cd Clawleash.Interfaces.MyProvider
dotnet build -c Release
```

### 配置

DLL と依存ファイルをインターフェースディレクトリにコピー：

```
%LocalAppData%\Clawleash\Interfaces\MyProvider\
├── Clawleash.Interfaces.MyProvider.dll
├── Clawleash.Abstractions.dll
└── (その他の依存DLL)
```

### 自動認識

アプリケーション実行中にファイルを配置すると、自動的にロードされます（ホットリロード有効時）。

---

## 設定の統合

### appsettings.json への追加

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

### DI への登録

プロバイダーは `ActivatorUtilities.CreateInstance` でインスタンス化されるため、設定とロガーを DI に登録します：

```csharp
// Program.cs で設定を登録
services.Configure<MyProviderSettings>(
    configuration.GetSection("ChatInterface:MyProvider"));
```

---

## ChatMessageReceivedEventArgs プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `MessageId` | `string` | メッセージの一意識別子 |
| `SenderId` | `string` | 送信者の一意識別子 |
| `SenderName` | `string` | 送信者の表示名 |
| `Content` | `string` | メッセージの内容 |
| `ChannelId` | `string` | チャンネル/ルーム ID |
| `Timestamp` | `DateTime` | メッセージのタイムスタンプ (UTC) |
| `ReplyToMessageId` | `string?` | 返信先メッセージ ID |
| `RequiresReply` | `bool` | 返信が必要かどうか |
| `InterfaceName` | `string` | インターフェース名 |
| `Metadata` | `Dictionary<string, object>` | プロバイダー固有のメタデータ |

---

## IChatInterface メンバー一覧

### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `Name` | `string` | インターフェース名（一意） |
| `IsConnected` | `bool` | 接続状態 |

### イベント

| イベント | 説明 |
|----------|------|
| `MessageReceived` | メッセージ受信時に発生 |

### メソッド

| メソッド | 説明 |
|----------|------|
| `StartAsync(CancellationToken)` | インターフェース開始 |
| `StopAsync(CancellationToken)` | インターフェース停止 |
| `SendMessageAsync(message, replyToMessageId?, CancellationToken)` | メッセージ送信 |
| `StartStreamingMessage(CancellationToken)` | ストリーミング送信開始 |
| `DisposeAsync()` | リソース解放 |

---

## E2EE 対応

E2EE（エンドツーエンド暗号化）をサポートする場合：

### 1. IE2eeProvider の使用

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
            // 暗号化されたデータを送信
        }
        else
        {
            // 平文で送信
        }
    }
}
```

### 2. 設定での有効化

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

## ベストプラクティス

### エラーハンドリング

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
        // 接続状態を更新
        _isConnected = false;
        throw;
    }
}
```

### ログ出力

```csharp
// 構造化ログを使用
_logger?.LogInformation("{Name} starting with server: {ServerUrl}", Name, _settings.ServerUrl);
_logger?.LogDebug("Message received: {MessageId} from {SenderName}", args.MessageId, args.SenderName);
```

### リソース管理

```csharp
public async ValueTask DisposeAsync()
{
    if (_disposed) return;
    _disposed = true;

    // イベントの購読解除
    // _client.OnMessage -= OnMessage;

    // 接続の切断
    await StopAsync();

    // その他のリソース解放
    _httpClient?.Dispose();
}
```

---

## 既存プロバイダーの参照

実装の参考として、以下のプロバイダーを参照してください：

- [Clawleash.Interfaces.Discord](../Clawleash.Interfaces.Discord) - Discord Bot
- [Clawleash.Interfaces.Slack](../Clawleash.Interfaces.Slack) - Slack Bot
- [Clawleash.Interfaces.WebSocket](../Clawleash.Interfaces.WebSocket) - WebSocket (E2EE)
- [Clawleash.Interfaces.WebRTC](../Clawleash.Interfaces.WebRTC) - WebRTC (E2EE)

---

## ライセンス

MIT
