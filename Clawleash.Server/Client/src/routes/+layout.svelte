<script lang="ts">
  import '../app.css';

  let { children } = $props();
</script>

<div class="app">
  <header>
    <h1>🦀 Clawleash Chat</h1>
    <span class="subtitle">E2EE Secure Communication</span>
  </header>

  <main>
    {@render children()}
  </main>

  <footer>
    <small>Clawleash - OpenClow-style AI Agent</small>
  </footer>
</div>

<style>
  .app {
    display: flex;
    flex-direction: column;
    min-height: 100vh;
    max-width: 1200px;
    margin: 0 auto;
    padding: 1rem;
  }

  header {
    display: flex;
    align-items: baseline;
    gap: 1rem;
    padding-bottom: 1rem;
    border-bottom: 1px solid #e0e0e0;
    margin-bottom: 1rem;
  }

  header h1 {
    margin: 0;
    font-size: 1.5rem;
  }

  .subtitle {
    color: #666;
    font-size: 0.875rem;
  }

  main {
    flex: 1;
  }

  footer {
    text-align: center;
    padding-top: 1rem;
    border-top: 1px solid #e0e0e0;
    margin-top: 1rem;
    color: #666;
  }
</style>
