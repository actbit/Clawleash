using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Clawleash.Mcp;

/// <summary>
/// MCPツールからSemantic KernelのKernelFunctionを動的生成するファクトリ
/// </summary>
public static class McpPluginFactory
{
    /// <summary>
    /// MCPツール情報からKernelFunctionを生成
    /// </summary>
    public static KernelFunction CreateKernelFunction(
        McpClientManager clientManager,
        string serverName,
        McpToolInfo tool)
    {
        // パラメータメタデータを生成
        var parameters = ParseInputSchema(tool.InputSchema);

        // デリゲートを作成
        Func<Kernel, KernelArguments, Task<string>> executeAsync = async (kernel, args) =>
        {
            // 引数を辞書に変換
            var arguments = new Dictionary<string, object?>();
            foreach (var param in parameters)
            {
                if (args.TryGetValue(param.Name, out var value))
                {
                    arguments[param.Name] = value;
                }
            }

            return await clientManager.ExecuteToolAsync(serverName, tool.ToolName, arguments);
        };

        // KernelFunctionを作成
        var function = KernelFunctionFactory.CreateFromMethod(
            method: executeAsync,
            functionName: tool.ToolName,
            description: tool.Description,
            parameters: parameters);

        return function;
    }

    /// <summary>
    /// MCPサーバー全体をKernelPluginとして生成
    /// </summary>
    public static KernelPlugin CreateKernelPlugin(
        McpClientManager clientManager,
        string serverName,
        IEnumerable<McpToolInfo> tools)
    {
        var functions = new List<KernelFunction>();

        foreach (var tool in tools)
        {
            var function = CreateKernelFunction(clientManager, serverName, tool);
            functions.Add(function);
        }

        // プラグイン名を正規化（ハイフン等をアンダースコアに変換）
        var normalizedServerName = serverName.Replace("-", "_").Replace(" ", "_");

        return KernelPluginFactory.CreateFromFunctions(
            pluginName: $"Mcp_{normalizedServerName}",
            description: $"MCP Server: {serverName}",
            functions: functions);
    }

    /// <summary>
    /// JSON SchemaからKernelParameterMetadataを生成
    /// </summary>
    private static List<KernelParameterMetadata> ParseInputSchema(JsonElement? inputSchema)
    {
        var parameters = new List<KernelParameterMetadata>();

        if (inputSchema == null)
        {
            return parameters;
        }

        try
        {
            var schema = inputSchema.Value;

            // JSON Schemaのpropertiesからパラメータを抽出
            if (schema.TryGetProperty("properties", out var properties))
            {
                // requiredフィールドを取得
                var requiredParams = new HashSet<string>();
                if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                {
                    foreach (var req in required.EnumerateArray())
                    {
                        requiredParams.Add(req.GetString() ?? "");
                    }
                }

                foreach (var prop in properties.EnumerateObject())
                {
                    var paramName = prop.Name;
                    var paramSchema = prop.Value;

                    // 説明を取得
                    var description = "";
                    if (paramSchema.TryGetProperty("description", out var desc))
                    {
                        description = desc.GetString() ?? "";
                    }

                    // 型を推測
                    var type = InferParameterType(paramSchema);

                    // デフォルト値
                    object? defaultValue = null;
                    if (paramSchema.TryGetProperty("default", out var defaultVal))
                    {
                        defaultValue = GetDefaultValue(defaultVal, type);
                    }

                    var metadata = new KernelParameterMetadata(paramName)
                    {
                        Description = description,
                        ParameterType = type,
                        IsRequired = requiredParams.Contains(paramName),
                        DefaultValue = defaultValue
                    };

                    parameters.Add(metadata);
                }
            }
        }
        catch (Exception)
        {
            // パースエラーは無視（空のパラメータリストを返す）
        }

        return parameters;
    }

    /// <summary>
    /// JSON Schemaから.NET型を推測
    /// </summary>
    private static Type InferParameterType(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var typeProp))
        {
            var typeStr = typeProp.GetString() ?? "";

            return typeStr switch
            {
                "string" => typeof(string),
                "integer" => typeof(int),
                "number" => typeof(double),
                "boolean" => typeof(bool),
                "array" => typeof(object[]),
                "object" => typeof(object),
                _ => typeof(object)
            };
        }

        // typeがない場合、他のヒントから推測
        if (schema.TryGetProperty("enum", out _))
        {
            return typeof(string);
        }

        return typeof(object);
    }

    /// <summary>
    /// デフォルト値を取得
    /// </summary>
    private static object? GetDefaultValue(JsonElement defaultVal, Type type)
    {
        if (defaultVal.ValueKind == JsonValueKind.Null || defaultVal.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            if (type == typeof(string))
            {
                return defaultVal.GetString();
            }
            if (type == typeof(int))
            {
                return defaultVal.GetInt32();
            }
            if (type == typeof(double))
            {
                return defaultVal.GetDouble();
            }
            if (type == typeof(bool))
            {
                return defaultVal.GetBoolean();
            }
        }
        catch
        {
            // 変換エラーは無視
        }

        return null;
    }
}
