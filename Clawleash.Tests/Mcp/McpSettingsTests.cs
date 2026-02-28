using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Clawleash.Mcp;
using System.Text.Json;
using System.Threading.Tasks;

namespace Clawleash.Tests.Mcp;

public class McpSettingsTests
{
    [Fact]
    public void McpSettings_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new McpSettings();

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.DefaultTimeoutMs.Should().Be(30000);
        settings.Servers.Should().BeEmpty();
    }

    [Fact]
    public void McpServerConfig_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new McpServerConfig();

        // Assert
        config.Transport.Should().Be("stdio");
        config.Enabled.Should().BeTrue();
        config.TimeoutMs.Should().Be(30000);
        config.UseSandbox.Should().BeTrue();
    }

    [Fact]
    public void McpServerConfig_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = @"{
            ""name"": ""test-server"",
            ""transport"": ""stdio"",
            ""command"": ""npx"",
            ""args"": [""-y"", ""@test/server""],
            ""environment"": {
                ""API_KEY"": ""secret""
            },
            ""enabled"": true,
            ""timeoutMs"": 60000,
            ""useSandbox"": false
        }";

        // Act
        var config = JsonSerializer.Deserialize<McpServerConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("test-server");
        config.Transport.Should().Be("stdio");
        config.Command.Should().Be("npx");
        config.Args.Should().ContainInOrder("-y", "@test/server");
        config.Environment.Should().ContainKey("API_KEY");
        config.TimeoutMs.Should().Be(60000);
        config.UseSandbox.Should().BeFalse();
    }

    [Fact]
    public void McpSettings_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = @"{
            ""enabled"": true,
            ""defaultTimeoutMs"": 45000,
            ""servers"": [
                {
                    ""name"": ""github"",
                    ""transport"": ""stdio"",
                    ""command"": ""npx""
                },
                {
                    ""name"": ""filesystem"",
                    ""transport"": ""stdio"",
                    ""command"": ""docker""
                }
            ]
        }";

        // Act
        var settings = JsonSerializer.Deserialize<McpSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        settings.Should().NotBeNull();
        settings!.Enabled.Should().BeTrue();
        settings.DefaultTimeoutMs.Should().Be(45000);
        settings.Servers.Should().HaveCount(2);
        settings.Servers[0].Name.Should().Be("github");
        settings.Servers[1].Name.Should().Be("filesystem");
    }

    [Fact]
    public void McpServerConfig_Sse_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = @"{
            ""name"": ""sse-server"",
            ""transport"": ""sse"",
            ""url"": ""http://localhost:3000"",
            ""headers"": {
                ""Authorization"": ""Bearer token123""
            },
            ""enabled"": true,
            ""timeoutMs"": 45000
        }";

        // Act
        var config = JsonSerializer.Deserialize<McpServerConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("sse-server");
        config.Transport.Should().Be("sse");
        config.Url.Should().Be("http://localhost:3000");
        config.Headers.Should().ContainKey("Authorization");
        config.Headers!["Authorization"].Should().Be("Bearer token123");
        config.TimeoutMs.Should().Be(45000);
    }

    [Fact]
    public void McpSettings_WithMixedTransports_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = @"{
            ""enabled"": true,
            ""servers"": [
                {
                    ""name"": ""stdio-tool"",
                    ""transport"": ""stdio"",
                    ""command"": ""node"",
                    ""args"": [""server.js""]
                },
                {
                    ""name"": ""sse-tool"",
                    ""transport"": ""sse"",
                    ""url"": ""http://api.example.com/mcp""
                }
            ]
        }";

        // Act
        var settings = JsonSerializer.Deserialize<McpSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        settings.Should().NotBeNull();
        settings!.Servers.Should().HaveCount(2);
        settings.Servers[0].Transport.Should().Be("stdio");
        settings.Servers[0].Command.Should().Be("node");
        settings.Servers[1].Transport.Should().Be("sse");
        settings.Servers[1].Url.Should().Be("http://api.example.com/mcp");
    }
}

public class McpToolInfoTests
{
    [Fact]
    public void McpToolInfo_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var toolInfo = new McpToolInfo();

        // Assert
        toolInfo.ServerName.Should().BeEmpty();
        toolInfo.ToolName.Should().BeEmpty();
        toolInfo.Description.Should().BeEmpty();
        toolInfo.InputSchema.Should().BeNull();
    }
}

public class ConnectedServerTests
{
    [Fact]
    public void ConnectedServer_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var server = new ConnectedServer();

        // Assert - stdio fields
        server.Process.Should().BeNull();
        server.StdIn.Should().BeNull();
        server.StdOut.Should().BeNull();
        server.StdErr.Should().BeNull();

        // Assert - SSE fields
        server.HttpClient.Should().BeNull();
        server.SseCts.Should().BeNull();
        server.SseListenerTask.Should().BeNull();
        server.PendingRequests.Should().BeEmpty();

        // Assert - common fields
        server.Tools.Should().BeEmpty();
        server.IsConnected.Should().BeFalse();
        server.RequestId.Should().Be(0);
    }

    [Fact]
    public void ConnectedServer_PendingRequests_ShouldBeThreadSafe()
    {
        // Arrange
        var server = new ConnectedServer();
        var tcs = new TaskCompletionSource<JsonElement?>();

        // Act
        server.PendingRequests[1] = tcs;
        var exists = server.PendingRequests.TryGetValue(1, out var retrieved);
        var removed = server.PendingRequests.TryRemove(1, out _);

        // Assert
        exists.Should().BeTrue();
        removed.Should().BeTrue();
        // retrieved and tcs should be the same reference
        (retrieved == tcs).Should().BeTrue("retrieved should be the same instance as tcs");
        server.PendingRequests.Should().BeEmpty();
    }
}

public class McpClientManagerTests : IDisposable
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<McpClientManager>> _loggerMock;

    public McpClientManagerTests()
    {
        _loggerMock = new Mock<ILogger<McpClientManager>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);
    }

    public void Dispose()
    {
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Assert
        manager.Servers.Should().BeEmpty();
        manager.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_WhenDisabled_ShouldSetIsEnabledFalse()
    {
        // Arrange
        var settings = new McpSettings { Enabled = false };
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        await manager.InitializeAsync(settings);

        // Assert
        manager.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_WhenEnabled_ShouldSetIsEnabledTrue()
    {
        // Arrange
        var settings = new McpSettings { Enabled = true, Servers = new List<McpServerConfig>() };
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        await manager.InitializeAsync(settings);

        // Assert
        manager.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithDisabledServers_ShouldSkipThem()
    {
        // Arrange
        var settings = new McpSettings
        {
            Enabled = true,
            Servers = new List<McpServerConfig>
            {
                new McpServerConfig { Name = "disabled", Enabled = false }
            }
        };
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        await manager.InitializeAsync(settings);

        // Assert
        manager.Servers.Should().BeEmpty();
    }

    [Fact]
    public void GetAllTools_WhenNoServers_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        var tools = manager.GetAllTools();

        // Assert
        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteToolAsync_WhenServerNotConnected_ShouldReturnError()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        var result = await manager.ExecuteToolAsync("nonexistent", "tool");

        // Assert
        result.Should().Contain("エラー");
        result.Should().Contain("接続されていません");
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);

        // Act
        var act = () =>
        {
            manager.Dispose();
            manager.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }
}
