using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DfoGmTool
{
    // Local-only system picker. The browser cannot return a real filesystem path.
    internal static class NativePathDialog
    {
        private static int _busy;

        public static object Pick(BrowsePathRequest request)
        {
            if (!OperatingSystem.IsWindows())
                return Fail("当前系统请直接填写路径。");
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return Fail("已有选择窗口打开。");

            try
            {
                var kind = (request?.Kind ?? string.Empty).Trim().ToLowerInvariant();
                string title;
                bool folder;
                COMDLG_FILTERSPEC[] filters = null;
                string defaultName = null;
                switch (kind)
                {
                    case "database":
                        title = "选择 A21 数据库";
                        folder = false;
                        defaultName = "inventory.db";
                        filters = new[]
                        {
                            new COMDLG_FILTERSPEC { pszName = "SQLite 数据库", pszSpec = "inventory.db;*.db" },
                            new COMDLG_FILTERSPEC { pszName = "所有文件", pszSpec = "*.*" },
                        };
                        break;
                    case "pvf":
                        title = "选择 Script.pvf";
                        folder = false;
                        defaultName = "Script.pvf";
                        filters = new[]
                        {
                            new COMDLG_FILTERSPEC { pszName = "PVF 文件", pszSpec = "Script.pvf;*.pvf" },
                            new COMDLG_FILTERSPEC { pszName = "所有文件", pszSpec = "*.*" },
                        };
                        break;
                    case "imagepacks":
                        title = "选择 ImagePacks2 目录";
                        folder = true;
                        break;
                    default:
                        return Fail("未知的选择类型。");
                }

                string path = null;
                string error = null;
                var currentPath = request?.CurrentPath;
                var thread = new Thread(() =>
                {
                    try
                    {
                        path = ShowDialog(title, folder, filters, defaultName, currentPath);
                    }
                    catch (Exception ex)
                    {
                        error = ex.GetBaseException().Message;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                thread.Join();

                if (!string.IsNullOrWhiteSpace(error))
                    return Fail(error);
                if (string.IsNullOrWhiteSpace(path))
                    return new { success = true, cancelled = true, path = (string)null };
                return new { success = true, cancelled = false, path };
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static object Fail(string error)
        {
            return new { success = false, error = error ?? "无法打开系统选择框。" };
        }

        private static string ShowDialog(
            string title,
            bool folder,
            COMDLG_FILTERSPEC[] filters,
            string defaultName,
            string currentPath)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT;
                if (folder)
                    options |= FOS_PICKFOLDERS;
                else
                    options |= FOS_FILEMUSTEXIST;
                dialog.SetOptions(options);
                dialog.SetTitle(title);

                if (!folder && filters != null && filters.Length > 0)
                {
                    dialog.SetFileTypes((uint)filters.Length, filters);
                    dialog.SetFileTypeIndex(1);
                    if (!string.IsNullOrWhiteSpace(defaultName))
                        dialog.SetFileName(defaultName);
                }

                ApplyInitialLocation(dialog, currentPath, defaultName);

                var hr = dialog.Show(GetForegroundWindow());
                if (hr == HRESULT_CANCELLED)
                    return null;
                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);

                IShellItem item;
                dialog.GetResult(out item);
                try
                {
                    IntPtr pszPath;
                    item.GetDisplayName(SIGDN_FILESYSPATH, out pszPath);
                    try
                    {
                        return Marshal.PtrToStringUni(pszPath);
                    }
                    finally
                    {
                        if (pszPath != IntPtr.Zero)
                            Marshal.FreeCoTaskMem(pszPath);
                    }
                }
                finally
                {
                    ReleaseCom(item);
                }
            }
            finally
            {
                ReleaseCom(dialog);
            }
        }

        private static void ApplyInitialLocation(IFileOpenDialog dialog, string currentPath, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            string folder = null;
            string fileName = null;
            try
            {
                var trimmed = currentPath.Trim();
                if (File.Exists(trimmed))
                {
                    folder = Path.GetDirectoryName(trimmed);
                    fileName = Path.GetFileName(trimmed);
                }
                else if (Directory.Exists(trimmed))
                {
                    folder = trimmed;
                }
                else
                {
                    var parent = Path.GetDirectoryName(trimmed);
                    if (Directory.Exists(parent))
                    {
                        folder = parent;
                        fileName = Path.GetFileName(trimmed);
                    }
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(fileName) && fileName != defaultName)
            {
                try
                {
                    dialog.SetFileName(fileName);
                }
                catch (COMException)
                {
                }
            }

            if (string.IsNullOrWhiteSpace(folder))
                return;

            var iid = typeof(IShellItem).GUID;
            IShellItem item;
            if (SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out item) != 0 || item == null)
                return;

            try
            {
                dialog.SetFolder(item);
            }
            catch (COMException)
            {
            }
            finally
            {
                ReleaseCom(item);
            }
        }

        private static void ReleaseCom(object com)
        {
            if (OperatingSystem.IsWindows() && com != null)
                Marshal.ReleaseComObject(com);
        }

        private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
        private const uint FOS_NOCHANGEDIR = 0x8;
        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const uint FOS_FILEMUSTEXIST = 0x1000;
        private const uint FOS_DONTADDTORECENT = 0x2000000;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            ref Guid riid,
            out IShellItem ppv);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszSpec;
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW
        {
        }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName(out IntPtr pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }

    public sealed class BrowsePathRequest
    {
        public string Kind { get; set; }
        public string CurrentPath { get; set; }
    }
}
