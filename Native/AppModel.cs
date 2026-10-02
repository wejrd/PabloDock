using System.Runtime.InteropServices;
using System.Text;

namespace PabloDock.Native;

internal static class AppModel
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int ErrorInsufficientBuffer = 122;
    internal const uint PackageFilterHeadAndDirect = 0x30;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFullName(nint process, ref uint length, StringBuilder? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int PackageFamilyNameFromFullName(
        string packageFullName, ref uint length, StringBuilder? familyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int FindPackagesByPackageFamily(
        string familyName, uint filters, ref uint count, [Out] nint[]? packageFullNames,
        ref uint bufferLength, nint buffer, nint properties);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int OpenPackageInfoByFullName(
        string packageFullName, uint reserved, out nint packageInfo);

    [DllImport("kernel32.dll")]
    internal static extern int GetPackageApplicationIds(
        nint packageInfo, ref uint bufferLength, byte[]? buffer, out uint count);

    [DllImport("kernel32.dll")]
    internal static extern int ClosePackageInfo(nint packageInfo);
}
