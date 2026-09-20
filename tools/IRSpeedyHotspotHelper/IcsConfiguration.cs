using System.Runtime.InteropServices;

namespace IRSpeedy.Hotspot;

// Adapted from Potisan (MIT); see THIRD-PARTY-NOTICES.txt. Order is ABI-critical.
[ComImport, Guid("C08956B6-1CD3-11D1-B1C5-00805FC1270E")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IcsConfiguration
{
    [PreserveSig] int get_SharingEnabled([MarshalAs(UnmanagedType.VariantBool)] out bool enabled);
    [PreserveSig] int get_SharingConnectionType(out int role);
    [PreserveSig] int DisableSharing();
    [PreserveSig] int EnableSharing(int role);
}
