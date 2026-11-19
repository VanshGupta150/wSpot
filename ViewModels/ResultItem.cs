using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using wSpot.Models;

namespace wSpot.ViewModels
{
    /// <summary>
    /// What one row of the results list binds to: the entry itself, plus the things
    /// that only exist on screen (which letters to draw in bold, and the icon, which
    /// loads a moment after the row appears). Keeping them here, rather than on
    /// AppEntry, leaves the model free of any UI types.
    /// </summary>
    public sealed class ResultItem : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public ResultItem(AppEntry entry, IEnumerable<int>? matchedIndices)
        {
            Entry = entry;
            MatchedIndices = matchedIndices;
        }

        public AppEntry Entry { get; }

        /// <summary>Positions in <see cref="Name"/> to draw in bold, or null for none.</summary>
        public IEnumerable<int>? MatchedIndices { get; }

        public string Name => Entry.Name;
        public string Subtitle => Entry.Subtitle;
        public string KindLabel => Entry.KindLabel;

        /// <summary>The full path, for files, folders and apps. Other rows already say everything on the second line.</summary>
        public string? ToolTipText =>
            (Entry.Kind == ResultKind.File || Entry.Kind == ResultKind.App || Entry.Kind == ResultKind.Folder)
                ? Entry.TargetPath
                : null;

        /// <summary>
        /// A symbol (from the Segoe MDL2 Assets font that ships with Windows 10 and 11)
        /// shown while the real icon is still loading, and for rows that have no file to
        /// take an icon from: commands, the calculator and the web search.
        /// </summary>
        public string Glyph => Entry.Kind switch
        {
            ResultKind.App => "",        // grid of tiles
            ResultKind.File => "",       // document
            ResultKind.Folder => "",     // folder
            ResultKind.Command => "",    // command prompt
            ResultKind.Calculator => "", // calculator
            ResultKind.WebSearch => "",  // globe
            _ => ""
        };

        public ImageSource? Icon => _icon;

        public bool HasIcon => _icon is not null;

        /// <summary>Called on the UI thread when the icon has been loaded.</summary>
        public void SetIcon(ImageSource? icon)
        {
            if (ReferenceEquals(_icon, icon)) return;

            _icon = icon;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(HasIcon));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
