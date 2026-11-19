using System;
using System.Diagnostics;
using System.IO;
using wSpot.Models;

namespace wSpot.Services
{
    public static class ProcessLauncher
    {
        /// <summary>
        /// Opens an app or a file the same way double-clicking it in Explorer would.
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

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Couldn't open \"{entry.Name}\": {ex.Message}";
                return false;
            }
        }
    }
}
