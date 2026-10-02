using System.Runtime.InteropServices;
using System.Text;
using PabloDock.Native;

namespace PabloDock.Services;

internal static class ApplicationIdentityService
{
    internal static (bool IsPackaged, string? ApplicationUserModelId) Read(uint processId)
    {
        var handle = AppModel.OpenProcess(AppModel.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return (false, null);
        }

        try
        {
            uint length = 0;
            var packageResult = AppModel.GetPackageFullName(handle, ref length, null);
            var isPackaged = packageResult is 0 or AppModel.ErrorInsufficientBuffer;
            if (!isPackaged)
            {
                return (false, null);
            }

            length = 0;
            if (AppModel.GetApplicationUserModelId(handle, ref length, null) !=
                AppModel.ErrorInsufficientBuffer || length is 0 or > 4096)
            {
                return (true, null);
            }

            var id = new StringBuilder(checked((int)length));
            return AppModel.GetApplicationUserModelId(handle, ref length, id) == 0
                ? (true, id.ToString())
                : (true, null);
        }
        finally
        {
            AppModel.CloseHandle(handle);
        }
    }

    internal static string? ResolveAumidFromPackagedPath(string? executablePath)
    {
        if (executablePath is null)
        {
            return null;
        }

        const string windowsApps = "\\WindowsApps\\";
        var marker = executablePath.IndexOf(windowsApps, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + windowsApps.Length;
        var end = executablePath.IndexOf('\\', start);
        if (end <= start)
        {
            return null;
        }

        var savedPackage = executablePath[start..end];
        var id = GetSingleApplicationId(savedPackage);
        if (id is not null)
        {
            return id;
        }

        uint familyLength = 0;
        if (AppModel.PackageFamilyNameFromFullName(savedPackage, ref familyLength, null) !=
            AppModel.ErrorInsufficientBuffer || familyLength is 0 or > 4096)
        {
            return null;
        }

        var family = new StringBuilder(checked((int)familyLength));
        if (AppModel.PackageFamilyNameFromFullName(savedPackage, ref familyLength, family) != 0)
        {
            return null;
        }

        uint count = 0;
        uint bufferLength = 0;
        if (AppModel.FindPackagesByPackageFamily(family.ToString(),
                AppModel.PackageFilterHeadAndDirect, ref count, null,
                ref bufferLength, 0, 0) != AppModel.ErrorInsufficientBuffer ||
            count is 0 or > 100 || bufferLength is 0 or > 65536)
        {
            return null;
        }

        var names = new nint[checked((int)count)];
        var buffer = Marshal.AllocHGlobal(checked((int)bufferLength * sizeof(char)));
        try
        {
            if (AppModel.FindPackagesByPackageFamily(family.ToString(),
                    AppModel.PackageFilterHeadAndDirect, ref count, names,
                    ref bufferLength, buffer, 0) != 0)
            {
                return null;
            }

            foreach (var namePointer in names.Take(checked((int)count)))
            {
                var installedPackage = Marshal.PtrToStringUni(namePointer);
                if (installedPackage is not null &&
                    GetSingleApplicationId(installedPackage) is { } installedId)
                {
                    return installedId;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private static string? GetSingleApplicationId(string packageFullName)
    {
        if (AppModel.OpenPackageInfoByFullName(packageFullName, 0, out var packageInfo) != 0)
        {
            return null;
        }

        try
        {
            uint length = 0;
            if (AppModel.GetPackageApplicationIds(packageInfo, ref length, null, out _) !=
                AppModel.ErrorInsufficientBuffer || length <= IntPtr.Size || length > 65536)
            {
                return null;
            }

            var bytes = new byte[checked((int)length)];
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                if (AppModel.GetPackageApplicationIds(packageInfo, ref length, bytes, out var count) != 0 ||
                    count != 1)
                {
                    return null;
                }

                var idPointer = Marshal.ReadIntPtr(pinned.AddrOfPinnedObject());
                return idPointer == 0 ? null : Marshal.PtrToStringUni(idPointer);
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            AppModel.ClosePackageInfo(packageInfo);
        }
    }
}
