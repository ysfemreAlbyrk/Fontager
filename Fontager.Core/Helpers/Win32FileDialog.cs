using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Fontager.Core.Helpers;

/// <summary>
/// Win32 IFileOpenDialog wrapper used as a fallback to the WinRT FileOpenPicker.
/// The WinRT picker silently fails or hangs when the host process is elevated
/// (admin) under some MSIX/identity combinations. IFileOpenDialog has no such
/// restriction and is the dialog Explorer itself uses.
/// </summary>
public static class Win32FileDialog
{
    /// <summary>
    /// Shows a modern Open dialog filtered to the supplied extensions and
    /// returns the chosen path, or null if the user cancelled.
    /// </summary>
    /// <param name="ownerHwnd">Window handle to use as the modal owner.</param>
    /// <param name="title">Dialog title shown in the chrome.</param>
    /// <param name="filterLabel">Label for the file-type filter ("Font files").</param>
    /// <param name="extensions">Extensions including the dot, e.g. ".ttf".</param>
    public static string? PickSingleFile(IntPtr ownerHwnd, string title, string filterLabel, IReadOnlyList<string> extensions)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)Activator.CreateInstance(
                Type.GetTypeFromCLSID(CLSID_FileOpenDialog)!)!;

            dialog.SetTitle(title);

            var spec = string.Join(";", extensions).Replace(".", "*.");

            var rgSpec = new[]
            {
                new COMDLG_FILTERSPEC { pszName = filterLabel, pszSpec = spec },
                new COMDLG_FILTERSPEC { pszName = "All files", pszSpec = "*.*" }
            };
            dialog.SetFileTypes((uint)rgSpec.Length, rgSpec);
            dialog.SetFileTypeIndex(1);

            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);

            int hr = dialog.Show(ownerHwnd);
            if (hr == HRESULT_CANCELLED) return null;
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                if (item != null) Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    // ── COM definitions ────────────────────────────────────────────

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");

    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show([In] IntPtr parent);

        // IFileDialog
        void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid([In] ref Guid guid);
        void ClearClientData();
        void SetFilter([MarshalAs(UnmanagedType.IUnknown)] object pFilter);

        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
