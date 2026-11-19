using System;
using System.Collections.Generic;
using System.IO;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// The built-in quick commands: things you'd otherwise dig through menus for.
    /// They are ordinary results (Kind = Command), found by the same fuzzy matcher as
    /// apps. Keywords are extra words that find them: "reboot" finds Restart.
    ///
    /// Windows' Settings, Calculator and Snipping Tool are Store apps, which the
    /// Start Menu scan doesn't see (it reads .lnk shortcuts only). The commands here
    /// are how wSpot reaches them.
    ///
    /// Restart, Shut down and Sign out need a second Enter (RequiresConfirmation) so one
    /// stray keypress can't end the user's session.
    /// </summary>
    public static class QuickCommandCatalog
    {
        public static List<AppEntry> Create()
        {
            string system = Environment.SystemDirectory;                                    // C:\Windows\System32
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);  // C:\Windows

            string rundll32 = Path.Combine(system, "rundll32.exe");
            string shutdown = Path.Combine(system, "shutdown.exe");
            string explorer = Path.Combine(windows, "explorer.exe");

            return new List<AppEntry>
            {
                Exe("Lock", "Lock this PC", rundll32, "user32.dll,LockWorkStation", keywords: "lock screen", hidden: true),

                Builtin("Sleep", "Put this PC to sleep", SystemActions.Sleep, keywords: "suspend"),

                Exe("Restart", "Restart this PC", shutdown, "/r /t 0", keywords: "reboot", hidden: true, confirm: true),
                Exe("Shut down", "Turn this PC off", shutdown, "/s /t 0", keywords: "power off,turn off", hidden: true, confirm: true),
                Exe("Sign out", "Sign out of Windows", shutdown, "/l", keywords: "log off,logout,log out", hidden: true, confirm: true),

                Exe("Task Manager", "See and close running programs", Path.Combine(system, "taskmgr.exe"), keywords: "processes,end task"),
                Exe("Settings", "Windows Settings", "ms-settings:", keywords: "preferences,options"),
                Exe("Control Panel", "The classic Control Panel", Path.Combine(system, "control.exe")),
                Exe("Calculator", "Windows Calculator", Path.Combine(system, "calc.exe"), keywords: "calc"),
                Exe("Device Manager", "Hardware and drivers", Path.Combine(system, "devmgmt.msc"), keywords: "drivers,hardware"),
                Exe("Services", "Windows services", Path.Combine(system, "services.msc")),
                Exe("File Explorer", "Browse your files", explorer, keywords: "files,this pc,my computer"),
                Exe("Downloads", "Open your Downloads folder", explorer, "shell:Downloads", keywords: "downloaded files"),
                Exe("Recycle Bin", "Open the Recycle Bin", explorer, "shell:RecycleBinFolder", keywords: "trash,deleted files"),
                Exe("Snipping Tool", "Take a screenshot", "ms-screenclip:", keywords: "screenshot,screen capture,snip"),
                Exe("Windows Update", "Check for updates", "ms-settings:windowsupdate", keywords: "updates,upgrade")
            };
        }

        /// <summary>A command that starts a program, a document or a URI like ms-settings:.</summary>
        private static AppEntry Exe(
            string name,
            string description,
            string target,
            string arguments = "",
            string keywords = "",
            bool hidden = false,
            bool confirm = false) => new AppEntry
            {
                Kind = ResultKind.Command,
                Name = name,
                Description = description,
                TargetPath = target,
                Arguments = arguments,
                Keywords = keywords,
                RunHidden = hidden,
                RequiresConfirmation = confirm
            };

        /// <summary>A command that runs code instead of starting a program.</summary>
        private static AppEntry Builtin(
            string name,
            string description,
            Func<string?> run,
            string keywords = "") => new AppEntry
            {
                Kind = ResultKind.Command,
                Name = name,
                Description = description,
                Keywords = keywords,
                Run = run
            };
    }
}
