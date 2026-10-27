using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using wSpot.Helpers;
using wSpot.Models;
using wSpot.Services;

namespace wSpot.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly AppDatabase _database;

        private readonly List<AppEntry> _allApps;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                RunSearch();
            }
        }

        public ObservableCollection<AppEntry> Results { get; } = new();

        private AppEntry? _selectedResult;
        public AppEntry? SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); }
        }

        public RelayCommand LaunchCommand { get; }

        public MainViewModel(AppDatabase database)
        {
            _database = database;
            _allApps = _database.GetAllApps(); // loaded once, not re-queried per keystroke
            LaunchCommand = new RelayCommand(_ => LaunchSelected());
        }

        private void RunSearch()
        {
            Results.Clear();

            if (string.IsNullOrWhiteSpace(SearchText)) return;

            var scored = new List<(AppEntry App, int Score)>();
            foreach (var app in _allApps)
            {
                var score = FuzzyMatcher.Score(SearchText, app.Name);
                if (score.HasValue)
                {
                    scored.Add((app, score.Value));
                }
            }

            foreach (var match in scored.OrderByDescending(s => s.Score).Take(20))
            {
                Results.Add(match.App);
            }

            if (Results.Count > 0)
            {
                SelectedResult = Results[0];
            }
        }

        private void LaunchSelected()
        {
            if (SelectedResult is null) return;
            ProcessLauncher.Launch(SelectedResult);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
