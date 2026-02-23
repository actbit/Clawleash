using Clawleash.Execution;
using Clawleash.Contracts;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Clawleash.Tools;

/// <summary>
/// ShellServer 経由でツールを実行するエグゼキューター
/// </summary>
public class ShellToolExecutor : IToolExecutor
{
    private readonly ILogger<ShellToolExecutor> _logger;
    private readonly ShellServer _shellServer;

    public ShellToolExecutor(ShellServer shellServer, ILogger<ShellToolExecutor> logger)
    {
        _shellServer = shellServer ?? throw new ArgumentNullException(nameof(shellServer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Shell 経由でツールメソッドを実行
    /// </summary>
    public async Task<object?> InvokeAsync(string toolName, string methodName, object?[] arguments)
    {
        _logger.LogDebug("ツール呼び出し: {Tool}.{Method}", toolName, methodName);

        try
        {
            var request = new ToolInvokeRequest
            {
                ToolName = toolName,
                MethodName = methodName,
                Arguments = arguments ?? Array.Empty<object?>()
            };

            var response = await _shellServer.InvokeToolAsync(
                request.ToolName,
                request.MethodName,
                request.Arguments);

            if (!response.Success)
            {
                _logger.LogWarning("ツール実行失敗: {Error}", response.Error);
                throw new ToolExecutionException(toolName, methodName, response.Error);
            }

            return response.Result;
        }
        catch (Exception ex) when (ex is not ToolExecutionException)
        {
            _logger.LogError(ex, "ツール呼び出しエラー: {Tool}.{Method}", toolName, methodName);
            throw new ToolExecutionException(toolName, methodName, ex.Message, ex);
        }
    }
}

/// <summary>
/// ツール実行例外
/// </summary>
public class ToolExecutionException : Exception
{
    public string ToolName { get; }
    public string MethodName { get; }

    public ToolExecutionException(string toolName, string methodName, string? message, Exception? innerException = null)
        : base($"Tool execution failed: {toolName}.{methodName}: {message}", innerException)
    {
        ToolName = toolName;
        MethodName = methodName;
    }
}
