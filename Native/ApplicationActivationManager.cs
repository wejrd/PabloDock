using System.Runtime.InteropServices;

namespace PabloDock.Native;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager
{
}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    void ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string applicationUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
        uint options,
        out uint processId);
}
