using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Clawleash.Mcp;

namespace Clawleash.Mcp;

/// <summary>
/// MCPツールをSemantic KernelのKernelFunctionとして公開するアダプター
/// </summary>
public class McpToolAdapter
{
    private readonly McpClientManager _clientManager;
    private readonly string _serverName;
    private readonly McpToolInfo _tool;

    public McpToolAdapter(McpClientManager clientManager, string serverName, McpToolInfo tool)
    {
        _clientManager = clientManager;
        _serverName = serverName;
        _tool = tool;
    }

    /// <summary>
    /// ツールを実行
    /// </summary>
    public async Task<string> ExecuteAsync(string? argumentsJson = null)
    {
        object? args = null;

        if (!string.IsNullOrEmpty(argumentsJson))
        {
            try
            {
                args = JsonSerializer.Deserialize<object>(argumentsJson);
            }
            catch
            {
                args = argumentsJson;
            }
        }

        return await _clientManager.ExecuteToolAsync(_serverName, _tool.ToolName, args);
    }
}

/// <summary>
/// MCPサーバー全体を1つのプラグインとして公開
/// </summary>
public class McpServerPlugin
{
    private readonly McpClientManager _clientManager;
    private readonly string _serverName;
    private readonly List<McpToolInfo> _tools;

    public McpServerPlugin(McpClientManager clientManager, string serverName, List<McpToolInfo> tools)
    {
        _clientManager = clientManager;
        _serverName = serverName;
        _tools = tools;
    }

    /// <summary>
    /// 利用可能なツール一覧を表示
    /// </summary>
    [KernelFunction("list_tools")]
    [Description("このMCPサーバーで利用可能なツール一覧を表示します")]
    public string ListTools()
    {
        if (_tools.Count == 0)
        {
            return "利用可能なツールがありません";
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine($"## MCPサーバー: {_serverName} ({_tools.Count} ツール)");
        result.AppendLine();

        foreach (var tool in _tools)
        {
            result.AppendLine($"### {tool.ToolName}");
            result.AppendLine($"{tool.Description}");
            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// ツールを実行
    /// </summary>
    [KernelFunction("execute_tool")]
    [Description("MCPツールを実行します")]
    public async Task<string> ExecuteToolAsync(
        [Description("ツール名")] string toolName,
        [Description("引数(JSON形式)")] string? arguments = null)
    {
        return await _clientManager.ExecuteToolAsync(_serverName, toolName, arguments);
    }
}
