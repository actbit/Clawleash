using System.Runtime.InteropServices;
using System.Text;
using Clawleash.Configuration;
using Clawleash.Models;

namespace Clawleash.Sandbox;

/// <summary>
/// Windows AppContainerを使用したサンドボックスプロバイダー
/// プロセスレベルでの分離とACLによるファイルアクセス制御を提供
/// </summary>
public class AppContainerProvider : ISandboxProvider
{
    private readonly ClawleashSettings _settings;
    private readonly AclManager _aclManager;
    private IntPtr _packageSid;
    private bool _disposed;
    private readonly List<IntPtr> _allocatedMemory = new();

    public SandboxType SandboxType => SandboxType.AppContainer;
    public bool IsInitialized { get; private set; }

    public AppContainerProvider(ClawleashSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _aclManager = new AclManager();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AppContainerはWindowsでのみサポートされています");
        }
    }

    public async Task InitializeAsync(IEnumerable<string> allowedDirectories, CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            return;
        }

        // AppContainerプロファイルを作成または既存のSIDを取得
        _packageSid = GetOrCreateAppContainerSid();

        if (_packageSid == IntPtr.Zero)
        {
            throw new InvalidOperationException("AppContainer SIDの取得に失敗しました");
        }

        // 許可されたディレクトリにACLを設定
        var directoryList = allowedDirectories.ToList();
        if (directoryList.Count > 0)
        {
            _aclManager.GrantAccessToDirectories(directoryList, _packageSid);
        }

        IsInitialized = true;
        await Task.CompletedTask;
    }

    public async Task<CommandResult> ExecuteAsync(
        string executable,
        string args,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("サンドボックスが初期化されていません。InitializeAsync()を先に呼び出してください");
        }

        return await Task.Run(() => ExecuteInAppContainer(executable, args, workingDirectory), cancellationToken);
    }

    public async Task<CommandResult> ExecuteShellAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        // Windowsではcmd.exeを使用してコマンドを実行
        return await ExecuteAsync("cmd.exe", $"/c \"{command}\"", workingDirectory, cancellationToken);
    }

    private IntPtr GetOrCreateAppContainerSid()
    {
        var containerName = _settings.Sandbox.AppContainerName;

        // まず既存のプロファイルからSIDを取得を試行
        var result = NativeMethods.DeriveAppContainerSidFromAppContainerName(containerName, out var sid);

        if (result == NativeMethods.ERROR_SUCCESS && sid != IntPtr.Zero)
        {
            return sid;
        }

        // 新しいプロファイルを作成
        result = NativeMethods.CreateAppContainerProfile(
            containerName,
            "Clawleash Sandbox",
            "Clawleash execution sandbox",
            IntPtr.Zero,
            0,
            out sid);

        if (result == NativeMethods.ERROR_ALREADY_EXISTS)
        {
            // 既に存在する場合は再度SIDを取得
            result = NativeMethods.DeriveAppContainerSidFromAppContainerName(containerName, out sid);
            if (result != NativeMethods.ERROR_SUCCESS || sid == IntPtr.Zero)
            {
                throw new InvalidOperationException($"既存のAppContainer SIDの取得に失敗: {result}");
            }
        }
        else if (result != NativeMethods.ERROR_SUCCESS || sid == IntPtr.Zero)
        {
            throw new InvalidOperationException($"AppContainerプロファイルの作成に失敗: {result}");
        }

        return sid;
    }

    private CommandResult ExecuteInAppContainer(string executable, string args, string? workingDirectory)
    {
        IntPtr hReadPipe = IntPtr.Zero;
        IntPtr hWritePipe = IntPtr.Zero;
        IntPtr hErrorReadPipe = IntPtr.Zero;
        IntPtr hErrorWritePipe = IntPtr.Zero;

        try
        {
            // 標準出力用パイプ作成
            if (!NativeMethods.CreateSecurePipe(out hReadPipe, out hWritePipe))
            {
                throw new InvalidOperationException("標準出力パイプの作成に失敗しました");
            }

            // 標準エラー用パイプ作成
            if (!NativeMethods.CreateSecurePipe(out hErrorReadPipe, out hErrorWritePipe))
            {
                throw new InvalidOperationException("標準エラーパイプの作成に失敗しました");
            }

            // 書き込みハンドルは継承させない
            NativeMethods.SetHandleInformation(hReadPipe, NativeMethods.HANDLE_FLAG_INHERIT, 0);
            NativeMethods.SetHandleInformation(hErrorReadPipe, NativeMethods.HANDLE_FLAG_INHERIT, 0);

            // プロセス属性リストのサイズを取得
            var attributeListSize = NativeMethods.GetProcThreadAttributeListSize(1);
            var attributeList = Marshal.AllocHGlobal(attributeListSize);
            _allocatedMemory.Add(attributeList);

            // 属性リストを初期化
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {error}");
            }

            // SECURITY_CAPABILITIES構造体を設定
            var capabilities = new NativeMethods.SECURITY_CAPABILITIES
            {
                AppContainerSid = _packageSid,
                Capabilities = IntPtr.Zero,
                CapabilityCount = 0,
                Reserved = 0
            };

            // PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIESを設定
            var attributePtr = new IntPtr(0x00020015); // PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES
            if (!NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                attributePtr,
                ref capabilities,
                Marshal.SizeOf<NativeMethods.SECURITY_CAPABILITIES>(),
                IntPtr.Zero,
                IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {error}");
            }

            // STARTUPINFOEX構造体を設定
            var startupInfoEx = new NativeMethods.STARTUPINFOEX
            {
                StartupInfo = new NativeMethods.STARTUPINFO
                {
                    cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>(),
                    dwFlags = 0x100, // STARTF_USESTDHANDLES
                    hStdOutput = hWritePipe,
                    hStdError = hErrorWritePipe
                },
                lpAttributeList = attributeList
            };

            var processInfo = new NativeMethods.PROCESS_INFORMATION();
            var commandLine = $"{executable} {args}";

            // プロセスを作成
            var creationFlags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT |
                               NativeMethods.CREATE_NO_WINDOW |
                               NativeMethods.CREATE_UNICODE_ENVIRONMENT;

            var success = NativeMethods.CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true, // inherit handles
                creationFlags,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfoEx,
                out processInfo);

            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateProcess failed: {error}");
            }

            // 書き込みハンドルを閉じる（子プロセスが継承した後）
            NativeMethods.CloseHandle(hWritePipe);
            NativeMethods.CloseHandle(hErrorWritePipe);
            hWritePipe = IntPtr.Zero;
            hErrorWritePipe = IntPtr.Zero;

            // 出力を読み取り
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var buffer = new byte[4096];

            // プロセス終了を待機
            var waitResult = NativeMethods.WaitForSingleObject(processInfo.hProcess, 30000); // 30秒タイムアウト

            // 出力を読み取り
            ReadFromPipe(hReadPipe, outputBuilder, buffer);
            ReadFromPipe(hErrorReadPipe, errorBuilder, buffer);

            // 終了コードを取得
            NativeMethods.GetExitCodeProcess(processInfo.hProcess, out var exitCode);

            // ハンドルを閉じる
            NativeMethods.CloseHandle(processInfo.hProcess);
            NativeMethods.CloseHandle(processInfo.hThread);

            return new CommandResult((int)exitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }
        finally
        {
            // パイプハンドルを閉じる
            if (hReadPipe != IntPtr.Zero) NativeMethods.CloseHandle(hReadPipe);
            if (hWritePipe != IntPtr.Zero) NativeMethods.CloseHandle(hWritePipe);
            if (hErrorReadPipe != IntPtr.Zero) NativeMethods.CloseHandle(hErrorReadPipe);
            if (hErrorWritePipe != IntPtr.Zero) NativeMethods.CloseHandle(hErrorWritePipe);
        }
    }

    private static void ReadFromPipe(IntPtr hPipe, StringBuilder builder, byte[] buffer)
    {
        while (true)
        {
            if (!NativeMethods.ReadFile(hPipe, buffer, (uint)buffer.Length, out var bytesRead, IntPtr.Zero))
            {
                break;
            }

            if (bytesRead == 0)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, (int)bytesRead));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // 割り当てたメモリを解放
        foreach (var ptr in _allocatedMemory)
        {
            if (ptr != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(ptr);
                Marshal.FreeHGlobal(ptr);
            }
        }
        _allocatedMemory.Clear();

        // SIDを解放
        if (_packageSid != IntPtr.Zero)
        {
            NativeMethods.FreeSid(_packageSid);
            _packageSid = IntPtr.Zero;
        }

        // AppContainerプロファイルを削除（オプション）
        // 注意: 実際の運用では削除しない方がパフォーマンスが良い場合がある
        // NativeMethods.DeleteAppContainerProfile(_settings.Sandbox.AppContainerName);

        _disposed = true;
        await ValueTask.CompletedTask;
    }
}
