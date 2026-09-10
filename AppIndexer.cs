using System;
using System.Collections.Generic;
using System.IO;
using wSpot.Models;

namespace wSpot.Services{
    public static class AppIndexer{
        public static List<AppEntry> ScanInstalledApps(){
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
                        // Some shortcuts (uninstallers, help links) resolve
                        // to an empty target — skip rather than show dead entries.
                        if (string.IsNullOrWhiteSpace(targetPath)) continue;

                        results.Add(new AppEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(shortcutPath),
                            TargetPath = targetPath,
                            Arguments = arguments
                        });
                    }
                    catch
                    {
                        // Matches the blueprint's fallback: if a .lnk fails
                        // to parse (UWP apps, unusual shortcut types), skip
                        // it instead of crashing the whole scan.
                        continue;
                    }
                }
            }

            return results;
        }
    }
}
