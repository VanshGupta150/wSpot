using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// Gets the icon Windows shows in Explorer for an app, a file or a folder.
    ///
    /// Lookups:
    ///   - Folders share the standard Windows folder icon, retrieved via shell attributes.
    ///   - Programs and shortcuts (.exe, .lnk, .ico, ...) have their own icon, so the real file is asked.
    ///   - Every other file type (.pdf, .docx, .png...) shares one icon per extension.
    /// </summary>
    public sealed class IconProvider
    {
        // Extensions whose icon belongs to the individual file.
        private static readonly string[] OwnIconExtensions = { ".exe", ".lnk", ".ico", ".url", ".scr", ".cpl", ".msc" };

        private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache =
            new ConcurrentDictionary<string, Task<ImageSource?>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the icon for apps, files and folders, or null for everything else (and when
        /// Windows has none). Never throws: an icon is decoration.
        /// </summary>
        public Task<ImageSource?> LoadAsync(AppEntry entry)
        {
            if (entry.Kind != ResultKind.App && entry.Kind != ResultKind.File && entry.Kind != ResultKind.Folder)
                return Task.FromResult<ImageSource?>(null);

            string path = entry.TargetPath;

            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult<ImageSource?>(null);

            string key = entry.Kind == ResultKind.Folder
                ? "folder"
                : (HasOwnIcon(path) ? path : "ext:" + Path.GetExtension(path));

            return _cache.GetOrAdd(key, _ => Task.Run(() => Extract(path, entry.Kind == ResultKind.Folder)));
        }

        private static bool HasOwnIcon(string path)
        {
            string extension = Path.GetExtension(path);

            foreach (string own in OwnIconExtensions)
            {
                if (string.Equals(extension, own, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static ImageSource? Extract(string path, bool isFolder)
        {
            IntPtr iconHandle = IntPtr.Zero;

            try
            {
                var info = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                string lookup = path;
                uint attributes = 0;

                if (isFolder)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_DIRECTORY;
                    lookup = "dummyFolder";
                }
                else if (HasOwnIcon(path))
                {
                    if (!File.Exists(path)) return null; // a shortcut's target may be gone
                }
                else
                {
                    // Ask about the type, not the file: "a file called x.pdf", which doesn't have to exist.
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_NORMAL;
                    lookup = "file" + Path.GetExtension(path);
                }

                IntPtr result = SHGetFileInfo(lookup, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                iconHandle = info.hIcon;

                BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                bitmap.Freeze(); // frozen objects can be handed from this worker thread to the UI thread
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                    DestroyIcon(iconHandle); // the shell gave us ownership of the handle
            }
        }

        private const uint SHGFI_ICON = 0x00000100;
        private const uint SHGFI_LARGEICON = 0x00000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
