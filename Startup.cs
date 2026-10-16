using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace MicNeverDown
{
    // Creates an ordinary, visible per-user Startup shortcut through the documented
    // Windows Shell APIs. No scripting host, registry modification, or elevation.
    public static class Startup
    {
        public static readonly string LinkPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MicNeverDown.lnk");

        public static bool Enabled
        {
            get
            {
                if (!File.Exists(LinkPath)) return false;
                try { return IsOwnedTarget(ReadShortcutTarget(LinkPath), Application.ExecutablePath); }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (COMException) { return false; }
                catch (ArgumentException) { return false; }
            }
        }

        public static void Set(bool enabled)
        {
            if (enabled)
            {
                CreateShortcut(LinkPath, Application.ExecutablePath);
                return;
            }

            if (!File.Exists(LinkPath)) return;
            // The filename alone is not proof that an existing shortcut belongs
            // to this application. Preserve unrecognized links for the user.
            RequireOwnedLink(LinkPath, Application.ExecutablePath);
            File.Delete(LinkPath);
        }

        // Also usable with a link in a test directory; this method does not
        // implicitly modify LinkPath or create an additional startup entry.
        public static void CreateShortcut(string linkPath, string executablePath)
        {
            string destination = NormalizePath(linkPath, "linkPath");
            string executable = NormalizePath(executablePath, "executablePath");
            if (!String.Equals(Path.GetExtension(destination), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A Windows shortcut must have a .lnk extension.", "linkPath");
            if (!File.Exists(executable)) throw new FileNotFoundException("The application executable was not found.", executable);
            if (File.Exists(destination)) RequireOwnedLink(destination, executable);

            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            IShellLinkW link = null;
            try
            {
                try
                {
                    link = (IShellLinkW)new ShellLinkComObject();
                    Check(link.SetPath(executable));
                    Check(link.SetArguments("--background"));
                    Check(link.SetWorkingDirectory(Path.GetDirectoryName(executable)));
                    Check(link.SetDescription(Ui.Text("MicNeverDown — microphone volume lock", "MicNeverDown — фиксация уровня микрофона")));
                    Check(link.SetShowCmd(1)); // SW_SHOWNORMAL; the application controls its tray-only startup.
                    Check(link.SetIconLocation(executable, 0));
                    IPersistFile persistence = (IPersistFile)link;
                    persistence.Save(temporary, true);
                    persistence.SaveCompleted(temporary);
                }
                finally
                {
                    Release(link);
                    link = null;
                }

                // Publish only a complete shortcut. Recheck ownership before
                // replacing an entry that could have changed during the save.
                if (File.Exists(destination))
                {
                    RequireOwnedLink(destination, executable);
                    File.Replace(temporary, destination, null, true);
                }
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static string ReadShortcutTarget(string linkPath)
        {
            string source = NormalizePath(linkPath, "linkPath");
            if (!File.Exists(source)) throw new FileNotFoundException("The shortcut was not found.", source);
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new ShellLinkComObject();
                ((IPersistFile)link).Load(source, 0); // STGM_READ
                StringBuilder target = new StringBuilder(32768);
                // SLGP_RAWPATH reads the stored target, including a target that
                // is currently missing. Never call Resolve: reading must not
                // track a different executable, show UI, or update the link.
                Check(link.GetPath(target, target.Capacity, IntPtr.Zero, 0x00000004));
                return Environment.ExpandEnvironmentVariables(target.ToString());
            }
            finally { Release(link); }
        }

        private static void RequireOwnedLink(string linkPath, string executablePath)
        {
            string target = ReadShortcutTarget(linkPath);
            if (!IsOwnedTarget(target, executablePath))
                throw new InvalidOperationException(Ui.Text("The existing startup shortcut points to another application and has been left unchanged: ", "Существующий ярлык автозапуска указывает на другую программу. Он оставлен без изменений: ") + linkPath);
        }

        private static bool IsOwnedTarget(string target, string executablePath)
        {
            if (String.IsNullOrWhiteSpace(target)) return false;
            string normalized = NormalizePath(target, "target");
            if (String.Equals(normalized, NormalizePath(executablePath, "executablePath"), StringComparison.OrdinalIgnoreCase))
                return true;

            // Recognize only the established installation path when upgrading
            // from the existing release. An arbitrary matching filename in
            // another directory is not accepted as ownership evidence.
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Codex", "Utilities", "MicNeverDown", "MicNeverDown.exe");
            return String.Equals(normalized, Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string value, string parameterName)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("A file path is required.", parameterName);
            return Path.GetFullPath(value);
        }

        private static void Check(int result)
        {
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        private static void Release(object value)
        {
            // IPersistFile and IShellLinkW are interfaces on the same RCW;
            // release the acquired object once, not once for each interface.
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkComObject { }

        // Exact IShellLinkW vtable after IUnknown. Native DWORD and WORD remain
        // uint and ushort on both architectures; HWND/PIDL use pointer-sized
        // IntPtr. GetPath accepts a null optional WIN32_FIND_DATAW pointer.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            [PreserveSig]
            int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
            [PreserveSig]
            int GetIDList(out IntPtr itemIdList);
            [PreserveSig]
            int SetIDList(IntPtr itemIdList);
            [PreserveSig]
            int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);
            [PreserveSig]
            int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            [PreserveSig]
            int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
            [PreserveSig]
            int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            [PreserveSig]
            int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
            [PreserveSig]
            int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            [PreserveSig]
            int GetHotkey(out ushort hotkey);
            [PreserveSig]
            int SetHotkey(ushort hotkey);
            [PreserveSig]
            int GetShowCmd(out int showCommand);
            [PreserveSig]
            int SetShowCmd(int showCommand);
            [PreserveSig]
            int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);
            [PreserveSig]
            int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            [PreserveSig]
            int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            [PreserveSig]
            int Resolve(IntPtr window, uint flags);
            [PreserveSig]
            int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
    }
}
