using System.Reflection;
using System.Reflection.Emit;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Clawleash.Tools;

/// <summary>
/// Reflection.Emit を使用してツールプロキシを動的生成
/// 元のクラスと同じ属性・シグネチャを持ち、IPCでShellに処理を委譲
/// </summary>
public class ToolProxyGenerator
{
    private readonly ILogger<ToolProxyGenerator> _logger;
    private readonly AssemblyBuilder _assemblyBuilder;
    private readonly ModuleBuilder _moduleBuilder;
    private static int _assemblyCounter;

    public ToolProxyGenerator(ILogger<ToolProxyGenerator> logger)
    {
        _logger = logger;

        // 動的アセンブリを作成
        var assemblyName = new AssemblyName($"Clawleash.ToolProxies_{Interlocked.Increment(ref _assemblyCounter):x8}");
        _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);

        _moduleBuilder = _assemblyBuilder.DefineDynamicModule("ToolProxies");
    }

    /// <summary>
    /// ツールタイプのプロキシを生成
    /// </summary>
    public Type GenerateProxyType(ToolTypeInfo toolType, IToolExecutor executor)
    {
        var originalType = toolType.Type;
        var proxyTypeName = $"{originalType.Namespace}.Proxies.{originalType.Name}Proxy";

        _logger.LogDebug("プロキシ生成開始: {TypeName}", proxyTypeName);

        // クラスを定義
        var typeBuilder = _moduleBuilder.DefineType(
            proxyTypeName,
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(object));

        // フィールド: _executor
        var executorField = typeBuilder.DefineField(
            "_executor",
            typeof(IToolExecutor),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        // フィールド: _toolName
        var toolNameField = typeBuilder.DefineField(
            "_toolName",
            typeof(string),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        // コンストラクタを生成
        GenerateConstructor(typeBuilder, executorField, toolNameField, toolType.TypeName);

        // クラス属性をコピー
        CopyClassAttributes(typeBuilder, originalType);

        // 各メソッドのプロキシを生成
        foreach (var method in toolType.KernelFunctionMethods)
        {
            GenerateProxyMethod(typeBuilder, method, executorField, toolNameField);
        }

        var proxyType = typeBuilder.CreateType();
        _logger.LogInformation("プロキシ生成完了: {TypeName}", proxyTypeName);

        return proxyType;
    }

    /// <summary>
    /// コンストラクタを生成
    /// </summary>
    private void GenerateConstructor(
        TypeBuilder typeBuilder,
        FieldBuilder executorField,
        FieldBuilder toolNameField,
        string toolName)
    {
        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(IToolExecutor) });

        var il = constructor.GetILGenerator();

        // base constructor call
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

        // _executor = executor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, executorField);

        // _toolName = toolName
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, toolName);
        il.Emit(OpCodes.Stfld, toolNameField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// プロキシメソッドを生成
    /// </summary>
    private void GenerateProxyMethod(
        TypeBuilder typeBuilder,
        MethodInfo originalMethod,
        FieldBuilder executorField,
        FieldBuilder toolNameField)
    {
        // メソッドシグネチャを構築
        var parameters = originalMethod.GetParameters();
        var paramTypes = parameters.Select(p => p.ParameterType).ToArray();

        var methodBuilder = typeBuilder.DefineMethod(
            originalMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual,
            originalMethod.ReturnType,
            paramTypes);

        // 属性をコピー
        CopyMethodAttributes(methodBuilder, originalMethod, parameters);

        // ILを生成
        var il = methodBuilder.GetILGenerator();

        // ローカル変数: args array
        var argsLocal = il.DeclareLocal(typeof(object[]));

        // 引数配列を作成
        il.Emit(OpCodes.Ldc_I4, parameters.Length);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsLocal);

        for (var i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldc_I4, i);

            // 引数をロード (arg0=this, arg1=param1, ...)
            il.Emit(OpCodes.Ldarg, i + 1);

            // 値型の場合はボックス化
            if (parameters[i].ParameterType.IsValueType)
            {
                il.Emit(OpCodes.Box, parameters[i].ParameterType);
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        // _executor.InvokeAsync(_toolName, methodName, args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, executorField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, toolNameField);

        il.Emit(OpCodes.Ldstr, originalMethod.Name);

        il.Emit(OpCodes.Ldloc, argsLocal);

        var invokeMethod = typeof(IToolExecutor).GetMethod(
            "InvokeAsync",
            new[] { typeof(string), typeof(string), typeof(object[]) })!;

        il.Emit(OpCodes.Callvirt, invokeMethod);

        // 戻り値の処理
        if (originalMethod.ReturnType == typeof(void))
        {
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
        else if (originalMethod.ReturnType == typeof(Task))
        {
            il.Emit(OpCodes.Ret);
        }
        else if (originalMethod.ReturnType.IsGenericType &&
                 originalMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            // Task<T> の場合、UnwrapResult で変換
            var resultType = originalMethod.ReturnType.GetGenericArguments()[0];
            var unwrapMethod = typeof(TaskHelper).GetMethod("UnwrapResult")!
                .MakeGenericMethod(resultType);

            il.Emit(OpCodes.Call, unwrapMethod);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // 同期メソッドの場合、Result プロパティを取得
            var resultProperty = typeof(Task<>).MakeGenericType(originalMethod.ReturnType)
                .GetProperty("Result")!;

            il.Emit(OpCodes.Callvirt, resultProperty.GetMethod!);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// クラス属性をコピー
    /// </summary>
    private void CopyClassAttributes(TypeBuilder typeBuilder, Type originalType)
    {
        foreach (var attrData in originalType.GetCustomAttributesData())
        {
            try
            {
                var attrBuilder = CreateCustomAttributeBuilder(attrData);
                if (attrBuilder != null)
                {
                    typeBuilder.SetCustomAttribute(attrBuilder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "属性コピー エラー: {Attribute}", attrData.AttributeType.Name);
            }
        }
    }

    /// <summary>
    /// メソッド属性をコピー
    /// </summary>
    private void CopyMethodAttributes(
        MethodBuilder methodBuilder,
        MethodInfo originalMethod,
        ParameterInfo[] parameters)
    {
        // メソッド属性
        foreach (var attrData in originalMethod.GetCustomAttributesData())
        {
            try
            {
                var attrBuilder = CreateCustomAttributeBuilder(attrData);
                if (attrBuilder != null)
                {
                    methodBuilder.SetCustomAttribute(attrBuilder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "メソッド属性コピー エラー: {Attribute}", attrData.AttributeType.Name);
            }
        }

        // パラメータ属性
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramBuilder = methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, param.Name);

            foreach (var attrData in param.GetCustomAttributesData())
            {
                try
                {
                    var attrBuilder = CreateCustomAttributeBuilder(attrData);
                    if (attrBuilder != null)
                    {
                        paramBuilder.SetCustomAttribute(attrBuilder);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "パラメータ属性コピー エラー: {Attribute}", attrData.AttributeType.Name);
                }
            }
        }
    }

    /// <summary>
    /// CustomAttributeBuilder を作成
    /// </summary>
    private static CustomAttributeBuilder? CreateCustomAttributeBuilder(CustomAttributeData attrData)
    {
        try
        {
            var attrType = attrData.AttributeType;

            // コンストラクタ引数
            var constructorArgs = attrData.ConstructorArguments
                .Select(a => ResolveTypedArgument(a))
                .ToArray();

            var constructor = attrData.Constructor;

            // 名前付き引数
            var namedProperties = new List<PropertyInfo>();
            var namedPropertyValues = new List<object?>();

            var namedFields = new List<FieldInfo>();
            var namedFieldValues = new List<object?>();

            foreach (var namedArg in attrData.NamedArguments)
            {
                if (namedArg.IsField)
                {
                    namedFields.Add(attrType.GetField(namedArg.MemberName)!);
                    namedFieldValues.Add(ResolveTypedArgument(namedArg.TypedValue));
                }
                else
                {
                    namedProperties.Add(attrType.GetProperty(namedArg.MemberName)!);
                    namedPropertyValues.Add(ResolveTypedArgument(namedArg.TypedValue));
                }
            }

            return new CustomAttributeBuilder(
                constructor,
                constructorArgs,
                namedProperties.ToArray(),
                namedPropertyValues.ToArray(),
                namedFields.ToArray(),
                namedFieldValues.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static object? ResolveTypedArgument(CustomAttributeTypedArgument arg)
    {
        if (arg.Value is ReadOnlyCollection<CustomAttributeTypedArgument> nested)
        {
            var array = Array.CreateInstance(
                arg.ArgumentType.GetElementType() ?? typeof(object),
                nested.Count);

            for (var i = 0; i < nested.Count; i++)
            {
                array.SetValue(ResolveTypedArgument(nested[i]), i);
            }
            return array;
        }

        return arg.Value;
    }
}

/// <summary>
/// ツール実行インターフェース
/// </summary>
public interface IToolExecutor
{
    Task<object?> InvokeAsync(string toolName, string methodName, object?[] arguments);
}

/// <summary>
/// Task ヘルパー
/// </summary>
public static class TaskHelper
{
    public static async Task<T> UnwrapResult<T>(Task<object?> task)
    {
        var result = await task;
        if (result == null)
            return default!;
        return (T)result;
    }
}
