using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Clawleash.Skills;
using Clawleash.Models;
using System.Text.Json;

namespace Clawleash.Tests.Skills;

public class SkillLoaderTests : IDisposable
{
    private readonly Mock<ILogger<SkillLoader>> _loggerMock;
    private readonly SkillLoader _skillLoader;
    private readonly string _tempDirectory;

    public SkillLoaderTests()
    {
        _loggerMock = new Mock<ILogger<SkillLoader>>();
        _loggerMock.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ClawleashTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _skillLoader = new SkillLoader(_loggerMock.Object, _tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
        _skillLoader.Dispose();
    }

    [Fact]
    public async Task LoadFromJson_ShouldParseValidJson()
    {
        // Arrange
        var json = @"{
            ""name"": ""test-skill"",
            ""description"": ""A test skill"",
            ""version"": ""1.0.0"",
            ""prompt"": ""Hello {{name}}"",
            ""parameters"": [
                {
                    ""name"": ""name"",
                    ""type"": ""string"",
                    ""required"": true
                }
            ]
        }";

        // Act
        var result = _skillLoader.LoadFromJson(json);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("test-skill");
        result.Description.Should().Be("A test skill");
        result.Version.Should().Be("1.0.0");
        result.Prompt.Should().Be("Hello {{name}}");
        result.Parameters.Should().HaveCount(1);
        result.Parameters[0].Name.Should().Be("name");
    }

    [Fact]
    public async Task LoadFromYaml_ShouldParseValidYaml()
    {
        // Arrange
        var yaml = @"
name: yaml-skill
description: A YAML test skill
version: ""2.0.0""
prompt: |
  This is a
  multiline prompt
parameters:
  - name: input
    type: string
    required: true
    description: Input text
tags:
  - test
  - yaml
";

        // Act
        var result = _skillLoader.LoadFromYaml(yaml);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("yaml-skill");
        result.Description.Should().Be("A YAML test skill");
        result.Version.Should().Be("2.0.0");
        result.Parameters.Should().HaveCount(1);
        result.Tags.Should().Contain("test").And.Contain("yaml");
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldLoadYamlFile()
    {
        // Arrange
        var yaml = @"
name: file-skill
description: Loaded from file
prompt: Test prompt
";
        var filePath = Path.Combine(_tempDirectory, "test.skill.yaml");
        await File.WriteAllTextAsync(filePath, yaml);

        // Act
        var result = await _skillLoader.LoadFromFileAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("file-skill");
        result.FilePath.Should().Be(filePath);
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldLoadJsonFile()
    {
        // Arrange
        var json = @"{""name"": ""json-skill"", ""description"": ""JSON skill"", ""prompt"": ""test""}";
        var filePath = Path.Combine(_tempDirectory, "test.skill.json");
        await File.WriteAllTextAsync(filePath, json);

        // Act
        var result = await _skillLoader.LoadFromFileAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("json-skill");
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldReturnNull_WhenFileNotExists()
    {
        // Act
        var result = await _skillLoader.LoadFromFileAsync("/nonexistent/path.yaml");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldReturnNull_WhenInvalidJson()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "invalid.skill.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json }");

        // Act
        var result = await _skillLoader.LoadFromFileAsync(filePath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAllFromDirectoryAsync_ShouldLoadAllSkillFiles()
    {
        // Arrange
        var yaml1 = @"name: skill1
description: First skill
prompt: Prompt 1";
        var yaml2 = @"name: skill2
description: Second skill
prompt: Prompt 2";
        var json = @"{""name"": ""skill3"", ""description"": ""Third"", ""prompt"": ""p3""}";

        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "skill1.skill.yaml"), yaml1);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "skill2.skill.yml"), yaml2);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "skill3.skill.json"), json);

        // Act
        var count = await _skillLoader.LoadAllFromDirectoryAsync(_tempDirectory);

        // Assert
        count.Should().Be(3);
        _skillLoader.Skills.Should().HaveCount(3);
        _skillLoader.Skills.Should().ContainKey("skill1");
        _skillLoader.Skills.Should().ContainKey("skill2");
        _skillLoader.Skills.Should().ContainKey("skill3");
    }

    [Fact]
    public void GetSkill_ShouldReturnSkill_WhenExists()
    {
        // Arrange
        _skillLoader.LoadFromJson(@"{""name"": ""get-test"", ""description"": ""test"", ""prompt"": ""test""}");

        // Act
        var result = _skillLoader.GetSkill("get-test");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("get-test");
    }

    [Fact]
    public void GetSkill_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = _skillLoader.GetSkill("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RemoveSkill_ShouldRemoveAndReturnTrue_WhenExists()
    {
        // Arrange
        _skillLoader.LoadFromJson(@"{""name"": ""remove-test"", ""description"": ""test"", ""prompt"": ""test""}");

        // Act
        var result = _skillLoader.RemoveSkill("remove-test");

        // Assert
        result.Should().BeTrue();
        _skillLoader.GetSkill("remove-test").Should().BeNull();
    }

    [Fact]
    public void RemoveSkill_ShouldReturnFalse_WhenNotExists()
    {
        // Act
        var result = _skillLoader.RemoveSkill("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ListSkills_ShouldFilterByTag()
    {
        // Arrange
        var yaml1 = @"
name: tagged-skill
description: Has tags
prompt: test
tags:
  - important
  - production";
        var yaml2 = @"
name: untagged-skill
description: No tags
prompt: test";

        _skillLoader.LoadFromYaml(yaml1);
        _skillLoader.LoadFromYaml(yaml2);

        // Act
        var result = _skillLoader.ListSkills("important").ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("tagged-skill");
    }

    [Fact]
    public void LoadFromJson_WithCustomName_ShouldOverrideName()
    {
        // Arrange
        var json = @"{""name"": ""original"", ""description"": ""test"", ""prompt"": ""test""}";

        // Act
        var result = _skillLoader.LoadFromJson(json, "custom-name");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("custom-name");
        _skillLoader.Skills.Should().ContainKey("custom-name");
    }
}
