using FluentAssertions;
using Clawleash.Models;
using System.Text.Json;

namespace Clawleash.Tests.Models;

public class SkillTests
{
    [Fact]
    public void ApplyParameters_ShouldReplaceSingleParameter()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Hello, {{name}}!"
        };
        skill.Parameters.Add(new SkillParameter
        {
            Name = "name",
            Required = true
        });

        var args = new Dictionary<string, object> { { "name", "World" } };

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Hello, World!");
    }

    [Fact]
    public void ApplyParameters_ShouldReplaceMultipleParameters()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "{{greeting}}, {{name}}! How are you?"
        };
        skill.Parameters.Add(new SkillParameter { Name = "greeting", Required = true });
        skill.Parameters.Add(new SkillParameter { Name = "name", Required = true });

        var args = new Dictionary<string, object>
        {
            { "greeting", "Hello" },
            { "name", "Alice" }
        };

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Hello, Alice! How are you?");
    }

    [Fact]
    public void ApplyParameters_ShouldUseDefaultValue_WhenParameterNotProvided()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Style: {{style}}"
        };
        skill.Parameters.Add(new SkillParameter
        {
            Name = "style",
            Required = false,
            Default = "simple"
        });

        var args = new Dictionary<string, object>();

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Style: simple");
    }

    [Fact]
    public void ApplyParameters_ShouldThrowException_WhenRequiredParameterMissing()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Hello, {{name}}!"
        };
        skill.Parameters.Add(new SkillParameter
        {
            Name = "name",
            Required = true
        });

        var args = new Dictionary<string, object>();

        // Act
        var act = () => skill.ApplyParameters(args);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("*必須パラメータ 'name'*");
    }

    [Fact]
    public void ApplyParameters_ShouldHandleJsonElement_String()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Value: {{value}}"
        };
        skill.Parameters.Add(new SkillParameter { Name = "value", Required = true });

        var jsonElement = JsonDocument.Parse(@"""test string""").RootElement;
        var args = new Dictionary<string, object> { { "value", jsonElement } };

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Value: test string");
    }

    [Fact]
    public void ApplyParameters_ShouldHandleJsonElement_Number()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Count: {{count}}"
        };
        skill.Parameters.Add(new SkillParameter { Name = "count", Required = true });

        var jsonElement = JsonDocument.Parse("42").RootElement;
        var args = new Dictionary<string, object> { { "count", jsonElement } };

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Count: 42");
    }

    [Fact]
    public void ApplyParameters_ShouldHandleJsonElement_Boolean()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Enabled: {{enabled}}"
        };
        skill.Parameters.Add(new SkillParameter { Name = "enabled", Required = true });

        var jsonElement = JsonDocument.Parse("true").RootElement;
        var args = new Dictionary<string, object> { { "enabled", jsonElement } };

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Enabled: true");
    }

    [Fact]
    public void ApplyParameters_ShouldHandleNull_WhenNotRequired()
    {
        // Arrange
        var skill = new Skill
        {
            Name = "test",
            Prompt = "Value: {{value}}"
        };
        skill.Parameters.Add(new SkillParameter
        {
            Name = "value",
            Required = false,
            Default = null
        });

        var args = new Dictionary<string, object>();

        // Act
        var result = skill.ApplyParameters(args);

        // Assert
        result.Should().Be("Value: {{value}}"); // 置換されない
    }
}
