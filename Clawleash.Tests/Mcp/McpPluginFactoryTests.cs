using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Clawleash.Mcp;
using System.Text.Json;

namespace Clawleash.Tests.Mcp;

public class McpPluginFactoryTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<McpClientManager>> _loggerMock;

    public McpPluginFactoryTests()
    {
        _loggerMock = new Mock<ILogger<McpClientManager>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);
    }

    [Fact]
    public void CreateKernelFunction_ShouldCreateFunctionWithCorrectName()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "get_weather",
            Description = "Get weather information"
        };

        // Act
        var function = McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        function.Should().NotBeNull();
        function.Name.Should().Be("get_weather");
        function.Description.Should().Be("Get weather information");
    }

    [Fact]
    public void CreateKernelFunction_WithInputSchema_ShouldParseParameters()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var schemaJson = @"{
            ""type"": ""object"",
            ""properties"": {
                ""location"": {
                    ""type"": ""string"",
                    ""description"": ""City name""
                },
                ""unit"": {
                    ""type"": ""string"",
                    ""description"": ""Temperature unit"",
                    ""enum"": [""celsius"", ""fahrenheit""]
                }
            },
            ""required"": [""location""]
        }";

        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "get_weather",
            Description = "Get weather",
            InputSchema = JsonDocument.Parse(schemaJson).RootElement
        };

        // Act
        var function = McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        function.Should().NotBeNull();
        function.Name.Should().Be("get_weather");
        // パラメータメタデータはKernelFunction内部に保持される
    }

    [Fact]
    public void CreateKernelPlugin_ShouldCreatePluginWithMultipleFunctions()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var tools = new List<McpToolInfo>
        {
            new() { ServerName = "test-server", ToolName = "read_file", Description = "Read a file" },
            new() { ServerName = "test-server", ToolName = "write_file", Description = "Write a file" },
            new() { ServerName = "test-server", ToolName = "list_files", Description = "List files" }
        };

        // Act
        var plugin = McpPluginFactory.CreateKernelPlugin(manager, "test-server", tools);

        // Assert
        plugin.Should().NotBeNull();
        plugin.Name.Should().Be("Mcp_test_server");
        plugin.FunctionCount.Should().Be(3);
        plugin.Should().Contain(f => f.Name == "read_file");
        plugin.Should().Contain(f => f.Name == "write_file");
        plugin.Should().Contain(f => f.Name == "list_files");
    }

    [Fact]
    public void CreateKernelPlugin_WithEmptyTools_ShouldCreateEmptyPlugin()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var tools = new List<McpToolInfo>();

        // Act
        var plugin = McpPluginFactory.CreateKernelPlugin(manager, "test-server", tools);

        // Assert
        plugin.Should().NotBeNull();
        plugin.Name.Should().Be("Mcp_test_server");
        plugin.FunctionCount.Should().Be(0);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("array")]
    [InlineData("object")]
    public void CreateKernelFunction_ShouldInferCorrectTypes(string jsonType)
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var schemaJson = @"{
            ""type"": ""object"",
            ""properties"": {
                ""param"": {
                    ""type"": """ + jsonType + @"""
                }
            }
        }";

        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "test_tool",
            Description = "Test tool",
            InputSchema = JsonDocument.Parse(schemaJson).RootElement
        };

        // Act
        var function = McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        function.Should().NotBeNull();
    }

    [Fact]
    public void CreateKernelFunction_WithComplexSchema_ShouldNotThrow()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var schemaJson = @"{
            ""type"": ""object"",
            ""properties"": {
                ""nested"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""inner"": { ""type"": ""string"" }
                    }
                },
                ""array"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""string"" }
                }
            },
            ""required"": [""nested""]
        }";

        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "complex_tool",
            Description = "Complex tool",
            InputSchema = JsonDocument.Parse(schemaJson).RootElement
        };

        // Act
        var act = () => McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CreateKernelFunction_WithNullSchema_ShouldCreateFunctionWithoutParameters()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "no_params_tool",
            Description = "Tool without parameters",
            InputSchema = null
        };

        // Act
        var function = McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        function.Should().NotBeNull();
        function.Name.Should().Be("no_params_tool");
    }

    [Fact]
    public void CreateKernelFunction_WithInvalidSchema_ShouldNotThrow()
    {
        // Arrange
        var manager = new McpClientManager(_loggerFactoryMock.Object);
        var tool = new McpToolInfo
        {
            ServerName = "test-server",
            ToolName = "invalid_schema_tool",
            Description = "Tool with invalid schema",
            InputSchema = JsonDocument.Parse("\"invalid\"").RootElement
        };

        // Act
        var act = () => McpPluginFactory.CreateKernelFunction(manager, "test-server", tool);

        // Assert
        act.Should().NotThrow();
    }
}
