// Network Lab - Windows shell integration: taskbar identity and "Pin to taskbar".
using System;
using System.Runtime.InteropServices;

namespace NetworkLab
{
    public static class ShellIntegration
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint c);
            void GetAt(uint i, out PropertyKey k);
            void GetValue(ref PropertyKey k, out PropVariant v);
            void SetValue(ref PropertyKey k, ref PropVariant v);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Ptr; }

        static readonly Guid AppUserModelProps = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        /// <summary>
        /// Gives the process its own taskbar identity instead of grouping under PowerShell.
        /// Must be called before the first window is shown.
        /// </summary>
        public static bool SetAppId(string appId)
        {
            try { return SetCurrentProcessExplicitAppUserModelID(appId) == 0; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Tells the taskbar how to relaunch this window, so "Pin to taskbar" pins the app itself (its name, icon and
        /// command) rather than powershell.exe.
        /// </summary>
        public static bool SetRelaunchInfo(IntPtr hwnd, string appId, string command, string displayName, string iconPath)
        {
            IPropertyStore store = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return false;
                // Windows wants the relaunch properties together with the ID
                Set(store, 5, appId);          // System.AppUserModel.ID
                Set(store, 2, command);        // System.AppUserModel.RelaunchCommand
                Set(store, 4, displayName);    // System.AppUserModel.RelaunchDisplayNameResource
                Set(store, 3, iconPath);       // System.AppUserModel.RelaunchIconResource
                store.Commit();
                return true;
            }
            catch (Exception) { return false; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }

        static void Set(IPropertyStore store, uint pid, string value)
        {
            PropertyKey k = new PropertyKey();
            k.FormatId = AppUserModelProps; k.PropertyId = pid;
            PropVariant v = new PropVariant();
            v.Vt = 31; // VT_LPWSTR
            v.Ptr = Marshal.StringToCoTaskMemUni(value);
            try { store.SetValue(ref k, ref v); }
            finally { Marshal.FreeCoTaskMem(v.Ptr); }
        }
    }
}
