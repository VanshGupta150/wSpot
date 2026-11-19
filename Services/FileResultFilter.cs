using System;

namespace wSpot.Services
{
    /// <summary>
    /// Decides which file paths are "noise" that nobody wants in a launcher:
    /// operating-system files, installed-program files, app caches, build output
    /// and package folders. Used on the Everything results and by the fallback scan.
    ///
    /// Plain lists on purpose, so a teammate can read and extend them.
    /// </summary>
    public static class FileResultFilter
    {
        // Folders that are noise wherever they appear in a path.
        private static readonly string[] NoiseFolderSegments =
        {
            @"\AppData\",
            @"\node_modules\",
            @"\.git\",
            @"\.vs\",
            @"\.venv\",
            @"\__pycache__\",
            @"\site-packages\",
            @"\bin\Debug\",
            @"\bin\Release\",
            @"\obj\Debug\",
            @"\obj\Release\",
            @"\$Recycle.Bin\",
            @"\System Volume Information\",
            @"\@Resources\",
            @"\Rainmeter\"
        };

        // Folders directly under a drive root, e.g. C:\Windows or C:\Program Files.
        private static readonly string[] SystemRootFolders =
        {
            "Windows",
            "Program Files",
            "Program Files (x86)",
            "ProgramData"
        };

        // File types nobody opens on purpose from a launcher.
        private static readonly string[] NoiseExtensions =
        {
            ".dll", ".sys", ".tmp", ".pyc", ".pdb", ".obj", ".ini", ".cache"
        };

        /// <summary>True if this file should be hidden from the results.</summary>
        public static bool IsNoise(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return true;

            foreach (string extension in NoiseExtensions)
            {
                if (fullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return IsInNoiseFolder(fullPath);
        }

        /// <summary>
        /// True if the path sits inside a noise folder. The fallback scan also calls
        /// this on folder paths (with a trailing backslash added) so it never walks
        /// into node_modules, .git and the like.
        /// </summary>
        public static bool IsInNoiseFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Ensure path ends with a backslash so directory paths without trailing
            // slashes (e.g. "C:\...\node_modules") still match segments like "\node_modules\".
            string normalized = path.EndsWith("\\") ? path : path + "\\";

            foreach (string segment in NoiseFolderSegments)
            {
                if (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase)) return true;
            }

            // "C:\Windows\..." : look at the first folder after the drive root.
            if (normalized.Length > 3 && normalized[1] == ':' && normalized[2] == '\\')
            {
                string afterRoot = normalized.Substring(3);

                foreach (string folder in SystemRootFolders)
                {
                    if (afterRoot.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }
    }
}
