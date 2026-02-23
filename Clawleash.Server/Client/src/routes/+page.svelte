<script lang="ts">
  import { ChatService, type Message, type ConnectionState } from '$lib/chat';

  let serverUrl = $state('http://localhost:8080');
  let channel = $state('general');
  let username = $state('User');
  let messageInput = $state('');
  let enableE2ee = $state(true);

  let chatService: ChatService | null = $state(null);
  let connectionState: ConnectionState = $state({ status: 'disconnected' });
  let messages: Message[] = $state([]);
  let isConnecting = $state(false);

  async function connect() {
    if (chatService) {
      await chatService.disconnect();
    }

    isConnecting = true;
    messages = [];

    chatService = new ChatService(enableE2ee);

    chatService.onMessageReceived = (message) => {
      messages = [...messages, message];
    };

    chatService.onConnectionStateChanged = (state) => {
      connectionState = state;
      isConnecting = false;
    };

    try {
      await chatService.connect(serverUrl);
      await chatService.joinChannel(channel);
    } catch (error) {
      console.error('Connection failed:', error);
      isConnecting = false;
    }
  }

  async function disconnect() {
    if (chatService) {
      await chatService.disconnect();
      chatService = null;
    }
  }

  async function sendMessage() {
    if (!chatService || !messageInput.trim()) return;

    await chatService.sendMessage(messageInput.trim(), channel, username);
    messageInput = '';
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    }
  }

  function formatTime(date: Date): string {
    return date.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit' });
  }

  function getStatusBadgeClass(): string {
    switch (connectionState.status) {
      case 'connected': return 'badge-success';
      case 'connecting':
      case 'reconnecting': return 'badge-warning';
      default: return 'badge-danger';
    }
  }

  function getStatusText(): string {
    switch (connectionState.status) {
      case 'connected': return 'Connected';
      case 'connecting': return 'Connecting...';
      case 'reconnecting': return 'Reconnecting...';
      default: return 'Disconnected';
    }
  }
</script>

<svelte:head>
  <title>Clawleash Chat</title>
</svelte:head>

<div class="chat-container">
  <!-- Connection Panel -->
  <div class="card connection-panel">
    <div class="connection-form">
      <div class="form-group">
        <label for="server">Server URL</label>
        <input type="text" id="server" bind:value={serverUrl} disabled={connectionState.status === 'connected'} />
      </div>

      <div class="form-group">
        <label for="channel">Channel</label>
        <input type="text" id="channel" bind:value={channel} disabled={connectionState.status === 'connected'} />
      </div>

      <div class="form-group">
        <label for="username">Username</label>
        <input type="text" id="username" bind:value={username} />
      </div>

      <div class="form-group checkbox">
        <label>
          <input type="checkbox" bind:checked={enableE2ee} disabled={connectionState.status === 'connected'} />
          Enable E2EE
        </label>
      </div>

      <div class="button-group">
        {#if connectionState.status === 'connected'}
          <button class="btn-secondary" onclick={disconnect}>Disconnect</button>
        {:else}
          <button class="btn-primary" onclick={connect} disabled={isConnecting}>
            {isConnecting ? 'Connecting...' : 'Connect'}
          </button>
        {/if}
      </div>
    </div>

    <div class="status-section">
      <span class="badge {getStatusBadgeClass()}">{getStatusText()}</span>
      {#if connectionState.status === 'connected' && chatService?.isE2eeEnabled}
        <span class="badge badge-success">🔒 E2EE</span>
      {/if}
      {#if connectionState.error}
        <span class="error-text">{connectionState.error}</span>
      {/if}
    </div>
  </div>

  <!-- Chat Area -->
  <div class="card chat-area">
    <div class="chat-header">
      <span class="channel-name"># {channel}</span>
      <span class="message-count">{messages.length} messages</span>
    </div>

    <div class="messages">
      {#if messages.length === 0}
        <div class="empty-state">
          <p>No messages yet. Connect and start chatting!</p>
        </div>
      {:else}
        {#each messages as message (message.messageId)}
          <div class="message">
            <div class="message-header">
              <span class="sender">{message.senderName}</span>
              <span class="time">{formatTime(message.timestamp)}</span>
              {#if message.encrypted}
                <span class="encrypted-badge" title="End-to-end encrypted">🔒</span>
              {/if}
            </div>
            <div class="message-content">{message.content}</div>
          </div>
        {/each}
      {/if}
    </div>

    <div class="input-area">
      <textarea
        placeholder={connectionState.status === 'connected' ? 'Type a message...' : 'Connect to start chatting'}
        bind:value={messageInput}
        onkeydown={handleKeydown}
        disabled={connectionState.status !== 'connected'}
        rows="2"
      ></textarea>
      <button
        class="btn-primary send-btn"
        onclick={sendMessage}
        disabled={connectionState.status !== 'connected' || !messageInput.trim()}
      >
        Send
      </button>
    </div>
  </div>
</div>

<style>
  .chat-container {
    display: grid;
    grid-template-columns: 300px 1fr;
    gap: 1rem;
    height: calc(100vh - 150px);
  }

  @media (max-width: 768px) {
    .chat-container {
      grid-template-columns: 1fr;
      grid-template-rows: auto 1fr;
    }
  }

  .connection-panel {
    display: flex;
    flex-direction: column;
    gap: 1rem;
  }

  .connection-form {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }

  .form-group {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  .form-group label {
    font-size: 0.75rem;
    font-weight: 500;
    color: var(--text-secondary);
  }

  .form-group input {
    width: 100%;
  }

  .checkbox label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    cursor: pointer;
  }

  .checkbox input[type="checkbox"] {
    width: auto;
  }

  .button-group {
    display: flex;
    gap: 0.5rem;
  }

  .button-group button {
    flex: 1;
  }

  .status-section {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: center;
    padding-top: 1rem;
    border-top: 1px solid var(--border);
  }

  .error-text {
    font-size: 0.75rem;
    color: var(--danger);
  }

  .chat-area {
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .chat-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding-bottom: 0.75rem;
    border-bottom: 1px solid var(--border);
    margin-bottom: 0.75rem;
  }

  .channel-name {
    font-weight: 600;
  }

  .message-count {
    font-size: 0.75rem;
    color: var(--text-secondary);
  }

  .messages {
    flex: 1;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    padding-right: 0.5rem;
  }

  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--text-secondary);
  }

  .message {
    padding: 0.5rem;
    border-radius: 0.375rem;
    background-color: var(--bg-secondary);
  }

  .message-header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.25rem;
  }

  .sender {
    font-weight: 500;
    font-size: 0.875rem;
  }

  .time {
    font-size: 0.75rem;
    color: var(--text-secondary);
  }

  .encrypted-badge {
    font-size: 0.75rem;
  }

  .message-content {
    font-size: 0.875rem;
    white-space: pre-wrap;
    word-break: break-word;
  }

  .input-area {
    display: flex;
    gap: 0.5rem;
    margin-top: 0.75rem;
    padding-top: 0.75rem;
    border-top: 1px solid var(--border);
  }

  .input-area textarea {
    flex: 1;
    resize: none;
  }

  .send-btn {
    align-self: flex-end;
  }
</style>
