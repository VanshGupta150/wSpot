using System.IO;

namespace wSpot.Models
{
    /// <summary>Which kind of thing a row in the results list represents.</summary>
    public enum ResultKind
    {
        App,
        File,
        Folder
    }

    /// <summary>
    /// One row in the results list. The name is left over from Phase 2, when every
    /// result was an installed app; it now covers files too, and <see cref="Kind"/>
    /// says which. Keeping the name means earlier phases merge in without a rename.
    ///
    /// Apps come from the SQLite index (Id is the row id). Files come from
    /// Everything or the folder-scan fallback, are never stored in SQLite, and
    /// leave Id at 0.
    /// </summary>
    public class AppEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public ResultKind Kind { get; set; } = ResultKind.App;

        /// <summary>Grey second line under the name: the folder for a file, "Application" for an app.</summary>
        public string Subtitle =>
            Kind switch
            {
                ResultKind.App    => "Application",
                ResultKind.Folder => TargetPath,
                _                 => Path.GetDirectoryName(TargetPath) ?? string.Empty
            };

        /// <summary>Small badge shown on the right-hand side of the row.</summary>
        public string KindLabel => Kind switch
        {
            ResultKind.App    => "App",
            ResultKind.Folder => "Folder",
            _                 => "File"
        };
    }
}
