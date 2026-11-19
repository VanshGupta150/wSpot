using System;
using System.Diagnostics;
using System.IO;
using wSpot.Models;

namespace wSpot.Services
{
    public static class ProcessLauncher
    {
        /// <summary>
        /// Opens an app, a file, a folder, a quick command or a web address the same way
        /// double-clicking it in Explorer would.
        ///
        /// Returns false (with a human-readable reason) instead of throwing, because a
        /// result can go stale: a file may have been moved or deleted since it was
        /// found, or Windows may have no program associated with its type.
        /// </summary>
        public static bool TryLaunch(AppEntry entry, out string? error)
        {
            error = null;

            if (entry.Kind == ResultKind.Folder && !Directory.Exists(entry.TargetPath))
            {
                error = $"\"{entry.Name}\" no longer exists at that location.";
                return false;
            }

            if (entry.Kind == ResultKind.File && !File.Exists(entry.TargetPath))
            {
                error = $"\"{entry.Name}\" no longer exists at that location.";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = entry.TargetPath,
                    Arguments = entry.Arguments,
                    UseShellExecute = true
                };

                if (entry.RunHidden)
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                Process.Start(startInfo)?.Dispose(); // we don't keep a handle on what we started
                return true;
            }
            catch (Exception ex)
            {
                error = $"Couldn't open \"{entry.Name}\": {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Opens an Explorer window with the file, folder or app selected ("Show in
        /// folder"). Only files, folders and apps have a location to show.
        /// </summary>
        public static bool TryReveal(AppEntry entry, out string? error)
        {
            error = null;

            if (entry.Kind != ResultKind.File && entry.Kind != ResultKind.App && entry.Kind != ResultKind.Folder)
            {
                error = "Only files, folders and apps can be shown in their location.";
                return false;
            }

            bool exists = entry.Kind == ResultKind.Folder
                ? Directory.Exists(entry.TargetPath)
                : File.Exists(entry.TargetPath);

            if (!exists)
            {
                error = $"\"{entry.Name}\" no longer exists at that location.";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{entry.TargetPath}\"",
                    UseShellExecute = true
                };

                Process.Start(startInfo)?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Couldn't show \"{entry.Name}\" in its location: {ex.Message}";
                return false;
            }
        }
    }
}
