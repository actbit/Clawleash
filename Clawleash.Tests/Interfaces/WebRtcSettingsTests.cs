using Clawleash.Interfaces.WebRTC;

namespace Clawleash.Tests.Interfaces;

public class WebRtcSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveCorrectValues()
    {
        // Arrange & Act
        var settings = new WebRtcSettings();

        // Assert
        Assert.Equal("ws://localhost:8080/signaling", settings.SignalingServerUrl);
        Assert.Equal(2, settings.StunServers.Count);
        Assert.Contains("stun:stun.l.google.com:19302", settings.StunServers);
        Assert.Contains("stun:stun1.l.google.com:19302", settings.StunServers);
        Assert.Null(settings.TurnServerUrl);
        Assert.Null(settings.TurnUsername);
        Assert.Null(settings.TurnPassword);
        Assert.Equal(5000, settings.ReconnectIntervalMs);
        Assert.Equal(30000, settings.IceConnectionTimeoutMs);
        Assert.Equal("clawleash-chat", settings.DataChannelName);
        Assert.Equal(5, settings.MaxReconnectAttempts);
        Assert.True(settings.TryUseNativeClient);
        Assert.Equal(10000, settings.IceGatheringTimeoutMs);
        Assert.True(settings.DataChannelReliable);
        Assert.Equal(30000, settings.HeartbeatIntervalMs);
        Assert.Equal(60000, settings.PeerIdleTimeoutMs);
    }

    [Fact]
    public void StunServers_CanBeModified()
    {
        // Arrange
        var settings = new WebRtcSettings();

        // Act
        settings.StunServers.Add("stun:stun2.l.google.com:19302");

        // Assert
        Assert.Equal(3, settings.StunServers.Count);
    }

    [Fact]
    public void TurnServer_CanBeConfigured()
    {
        // Arrange
        var settings = new WebRtcSettings();

        // Act
        settings.TurnServerUrl = "turn:turn.example.com:3478";
        settings.TurnUsername = "user";
        settings.TurnPassword = "pass";

        // Assert
        Assert.Equal("turn:turn.example.com:3478", settings.TurnServerUrl);
        Assert.Equal("user", settings.TurnUsername);
        Assert.Equal("pass", settings.TurnPassword);
    }

    [Fact]
    public void SignalingServerUrl_CanBeChanged()
    {
        // Arrange
        var settings = new WebRtcSettings();

        // Act
        settings.SignalingServerUrl = "https://example.com/signalr";

        // Assert
        Assert.Equal("https://example.com/signalr", settings.SignalingServerUrl);
    }

    [Fact]
    public void Timeouts_CanBeAdjusted()
    {
        // Arrange
        var settings = new WebRtcSettings();

        // Act
        settings.IceConnectionTimeoutMs = 60000;
        settings.ReconnectIntervalMs = 10000;
        settings.IceGatheringTimeoutMs = 20000;
        settings.HeartbeatIntervalMs = 15000;
        settings.PeerIdleTimeoutMs = 120000;

        // Assert
        Assert.Equal(60000, settings.IceConnectionTimeoutMs);
        Assert.Equal(10000, settings.ReconnectIntervalMs);
        Assert.Equal(20000, settings.IceGatheringTimeoutMs);
        Assert.Equal(15000, settings.HeartbeatIntervalMs);
        Assert.Equal(120000, settings.PeerIdleTimeoutMs);
    }

    [Fact]
    public void DataChannelSettings_CanBeConfigured()
    {
        // Arrange
        var settings = new WebRtcSettings();

        // Act
        settings.DataChannelName = "custom-channel";
        settings.DataChannelReliable = false;

        // Assert
        Assert.Equal("custom-channel", settings.DataChannelName);
        Assert.False(settings.DataChannelReliable);
    }
}
