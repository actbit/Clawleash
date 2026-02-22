using System.Runtime.InteropServices;

namespace Clawleash.Sandbox;

/// <summary>
/// Windows ACL（Access Control List）管理クラス
/// AppContainerのPackage SIDに対してディレクトリアクセス権限を設定する
/// </summary>
public class AclManager
{
    /// <summary>
    /// 指定されたディレクトリにAppContainer SIDの読み書きアクセス権限を付与する
    /// </summary>
    /// <param name="directoryPath">権限を付与するディレクトリパス</param>
    /// <param name="appContainerSid">AppContainerのSID</param>
    /// <returns>成功した場合はtrue</returns>
    public bool GrantAccessToDirectory(string directoryPath, IntPtr appContainerSid)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ACL管理はWindowsでのみサポートされています");
        }

        if (!Directory.Exists(directoryPath))
        {
            // ディレクトリが存在しない場合は作成
            Directory.CreateDirectory(directoryPath);
        }

        // EXPLICIT_ACCESS構造体を作成
        var explicitAccess = new NativeMethods.EXPLICIT_ACCESS
        {
            grfAccessPermissions = NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_EXECUTE,
            grfAccessMode = NativeMethods.GRANT_ACCESS,
            grfInheritance = NativeMethods.SUB_CONTAINERS_AND_OBJECTS_INHERIT,
            Trustee = new NativeMethods.TRUSTEE
            {
                pMultipleTrustee = IntPtr.Zero,
                MultipleTrusteeOperation = NativeMethods.NO_MULTIPLE_TRUSTEE,
                TrusteeForm = NativeMethods.TRUSTEE_IS_SID,
                TrusteeType = NativeMethods.TRUSTEE_IS_WELL_KNOWN_GROUP,
                ptstrName = appContainerSid
            }
        };

        // ACLを作成
        if (!NativeMethods.SetEntriesInAcl(1, new[] { explicitAccess }, IntPtr.Zero, out var newAcl))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetEntriesInAcl failed with error: {error}");
        }

        try
        {
            // ディレクトリのセキュリティ情報を設定
            if (!NativeMethods.SetNamedSecurityInfo(
                directoryPath,
                NativeMethods.SE_FILE_OBJECT,
                NativeMethods.DACL_SECURITY_INFORMATION,
                IntPtr.Zero,
                IntPtr.Zero,
                newAcl,
                IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"SetNamedSecurityInfo failed with error: {error}");
            }

            return true;
        }
        finally
        {
            // ACLメモリを解放
            if (newAcl != IntPtr.Zero)
            {
                NativeMethods.LocalFree(newAcl);
            }
        }
    }

    /// <summary>
    /// 指定されたディレクトリにAppContainer SIDの読み取り専用アクセス権限を付与する
    /// </summary>
    /// <param name="directoryPath">権限を付与するディレクトリパス</param>
    /// <param name="appContainerSid">AppContainerのSID</param>
    /// <returns>成功した場合はtrue</returns>
    public bool GrantReadAccessToDirectory(string directoryPath, IntPtr appContainerSid)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ACL管理はWindowsでのみサポートされています");
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var explicitAccess = new NativeMethods.EXPLICIT_ACCESS
        {
            grfAccessPermissions = NativeMethods.GENERIC_READ | NativeMethods.GENERIC_EXECUTE,
            grfAccessMode = NativeMethods.GRANT_ACCESS,
            grfInheritance = NativeMethods.SUB_CONTAINERS_AND_OBJECTS_INHERIT,
            Trustee = new NativeMethods.TRUSTEE
            {
                pMultipleTrustee = IntPtr.Zero,
                MultipleTrusteeOperation = NativeMethods.NO_MULTIPLE_TRUSTEE,
                TrusteeForm = NativeMethods.TRUSTEE_IS_SID,
                TrusteeType = NativeMethods.TRUSTEE_IS_WELL_KNOWN_GROUP,
                ptstrName = appContainerSid
            }
        };

        if (!NativeMethods.SetEntriesInAcl(1, new[] { explicitAccess }, IntPtr.Zero, out var newAcl))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetEntriesInAcl failed with error: {error}");
        }

        try
        {
            if (!NativeMethods.SetNamedSecurityInfo(
                directoryPath,
                NativeMethods.SE_FILE_OBJECT,
                NativeMethods.DACL_SECURITY_INFORMATION,
                IntPtr.Zero,
                IntPtr.Zero,
                newAcl,
                IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"SetNamedSecurityInfo failed with error: {error}");
            }

            return true;
        }
        finally
        {
            if (newAcl != IntPtr.Zero)
            {
                NativeMethods.LocalFree(newAcl);
            }
        }
    }

    /// <summary>
    /// 複数のディレクトリにアクセス権限を一括で設定する
    /// </summary>
    /// <param name="directories">権限を付与するディレクトリパス一覧</param>
    /// <param name="appContainerSid">AppContainerのSID</param>
    /// <param name="readOnly">読み取り専用にする場合はtrue</param>
    /// <returns>成功したディレクトリのリスト</returns>
    public List<string> GrantAccessToDirectories(
        IEnumerable<string> directories,
        IntPtr appContainerSid,
        bool readOnly = false)
    {
        var successfulDirectories = new List<string>();
        var errors = new List<string>();

        foreach (var directory in directories)
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);

                if (readOnly)
                {
                    if (GrantReadAccessToDirectory(fullPath, appContainerSid))
                    {
                        successfulDirectories.Add(fullPath);
                    }
                }
                else
                {
                    if (GrantAccessToDirectory(fullPath, appContainerSid))
                    {
                        successfulDirectories.Add(fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{directory}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine($"Warning: Failed to grant access to some directories:\n{string.Join("\n", errors)}");
        }

        return successfulDirectories;
    }
}
