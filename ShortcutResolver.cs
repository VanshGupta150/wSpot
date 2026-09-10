using System;
using System.Runtime.InteropServices;
using System.Text;

namespace wSpot.Services
{
    // Resolves a .lnk shortcut into its real target path and arguments,
    // using the same IShellLink COM interface Windows Explorer itself
    // uses to read shortcuts. This is what correctly handles shortcuts
    // with arguments, which naive binary .lnk parsing tends to get wrong.
    public static class ShortcutResolver
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath(StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription(StringBuilder pszName, int cchMaxName);
            void SetDescription(string pszName);
            void GetWorkingDirectory(StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory(string pszDir);
            void GetArguments(StringBuilder pszArgs, int cchMaxPath);
            void SetArguments(string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation(StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation(string pszIconPath, int iIcon);
            void SetRelativePath(string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath(string pszFile);
        }

        [ComImport]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load(string pszFileName, int dwMode);
            void Save(string pszFileName, bool fRemember);
            void SaveCompleted(string pszFileName);
            void GetCurFile(out string ppszFileName);
        }

        public static (string TargetPath, string Arguments) Resolve(string lnkPath)
        {
            var link = (IShellLink)new ShellLink();
            var file = (IPersistFile)link;

            file.Load(lnkPath, 0);

            var targetBuilder = new StringBuilder(260);
            link.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);

            var argsBuilder = new StringBuilder(260);
            link.GetArguments(argsBuilder, argsBuilder.Capacity);

            return (targetBuilder.ToString(), argsBuilder.ToString());
        }
    }
}
