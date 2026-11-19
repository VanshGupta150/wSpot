using System;
using System.Collections.Generic;
using System.IO;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// Scans the two Start Menu locations Windows uses — the all-users
    /// folder and the current user's own folder — for .lnk shortcuts,
    /// and resolves each one via ShortcutResolver.
    /// </summary>
    public static class AppIndexer
    {
        public static List<AppEntry> ScanInstalledApps()
        {
            var results = new List<AppEntry>();

            var startMenuFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (var folder in startMenuFolders)
            {
                if (!Directory.Exists(folder)) continue;

                var shortcuts = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories);

                foreach (var shortcutPath in shortcuts)
                {
                    try
                    {
                        var (targetPath, arguments) = ShortcutResolver.Resolve(shortcutPath);

                        // If ShortcutResolver cannot extract an exe path (e.g. UWP / Windows Store apps,
                        // shell links), keep the shortcut path itself so UseShellExecute can launch it.
                        string effectiveTarget = string.IsNullOrWhiteSpace(targetPath) ? shortcutPath : targetPath;

                        results.Add(new AppEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(shortcutPath),
                            TargetPath = effectiveTarget,
                            Arguments = arguments
                        });
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return results;
        }
    }
}
