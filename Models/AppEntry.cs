using System;
using System.IO;

namespace wSpot.Models
{
    /// <summary>
    /// Which kind of thing a row in the results list represents.
    /// Add new kinds at the END: the number is stored in the usage_stats table.
    /// </summary>
    public enum ResultKind
    {
        App,
        File,
        Folder,
        Command,     // a built-in quick command: Lock, Restart, Task Manager...
        Calculator,  // the answer to a typed math expression
        WebSearch    // the "Search the web for ..." fallback row
    }

    /// <summary>
    /// One row in the results list.
    ///
    /// Apps come from the SQLite index (Id is the row id). Files and folders come from
    /// Everything or the folder-scan fallback. Commands, the calculator row and the
    /// web-search row are created in code. None of those are stored in the apps table,
    /// so they leave Id at 0.
    /// </summary>
    public class AppEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public ResultKind Kind { get; set; } = ResultKind.App;

        /// <summary>Grey second line for commands, the calculator and the web-search row.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Extra words that should find a command ("reboot" finds Restart). Comma-separated.</summary>
        public string Keywords { get; set; } = string.Empty;

        /// <summary>True for commands that need a second Enter within a few seconds (restart, shut down...).</summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>Start the program without showing a console window.</summary>
        public bool RunHidden { get; set; }

        /// <summary>False for rows that must not count as "frequently used" (calculator, web search, clear history).</summary>
        public bool TrackUsage { get; set; } = true;

        /// <summary>
        /// For built-in actions that are code instead of a program to start.
        /// Returns an error message, or null when it worked.
        /// </summary>
        public Func<string?>? Run { get; set; }

        /// <summary>Grey second line under the name.</summary>
        public string Subtitle => Kind switch
        {
            ResultKind.File => Path.GetDirectoryName(TargetPath) ?? string.Empty,
            ResultKind.Folder => TargetPath,
            ResultKind.App => "Application",
            _ => Description
        };

        /// <summary>Small badge shown on the right-hand side of the row.</summary>
        public string KindLabel => Kind switch
        {
            ResultKind.File => "File",
            ResultKind.Folder => "Folder",
            ResultKind.App => "App",
            ResultKind.Command => "Command",
            ResultKind.Calculator => "Calculator",
            ResultKind.WebSearch => "Web",
            _ => string.Empty
        };

        /// <summary>
        /// The identity used by the usage_stats table: stable across restarts (the apps
        /// table gets new ids every time it is rebuilt, so the id can't be used).
        /// Two entries for the same thing give the same key, even if they are different objects.
        /// </summary>
        public string UsageKey
        {
            get
            {
                string identity = Kind switch
                {
                    ResultKind.App => TargetPath + "|" + Name,
                    ResultKind.Command => Name,
                    _ => TargetPath
                };

                return (Kind.ToString() + "|" + identity).ToLowerInvariant();
            }
        }
    }
}
