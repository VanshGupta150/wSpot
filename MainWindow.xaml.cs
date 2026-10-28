using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using wSpot.Models;
using wSpot.Services;
using wSpot.ViewModels;

namespace wSpot
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_SPACE = 0x20;
        private const int WM_HOTKEY = 0x0312;

        private HwndSource? _source;
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            var database = new AppDatabase();
            database.ReplaceAllApps(AppIndexer.ScanInstalledApps());

            _viewModel = new MainViewModel(database);
            DataContext = _viewModel;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(HwndHook);

            bool registered = RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL, VK_SPACE);
            if (!registered)
            {
                MessageBox.Show(
                    "Could not register the global hotkey (Ctrl+Space).\n" +
                    "Another application is already using it.\n\n" +
                    "wSpot cannot run without a hotkey and will now exit.",
                    "wSpot", MessageBoxButton.OK, MessageBoxImage.Error);

                Application.Current.Shutdown();
                return;
            }

            HideWindow();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
                HideWindow();
            else
                ShowWindow();
        }

        private void ShowWindow()
        {
            CenterOnScreen();
            Visibility = Visibility.Visible;
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private void HideWindow()
        {
            _viewModel.SearchText = string.Empty;
            Visibility = Visibility.Hidden;
        }

        private void CenterOnScreen()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + workArea.Height * 0.3;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            HideWindow();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideWindow();
            }
            else if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
        }

        private void MoveSelection(int direction)
        {
            var results = _viewModel.Results;
            if (results.Count == 0) return;

            int currentIndex = _viewModel.SelectedResult is null
                ? -1
                : results.IndexOf(_viewModel.SelectedResult);

            int newIndex = Math.Max(0, Math.Min(results.Count - 1, currentIndex + direction));

            _viewModel.SelectedResult = results[newIndex];
            ResultsListBox.ScrollIntoView(_viewModel.SelectedResult);
        }

        private void ResultsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var item = ItemsControl.ContainerFromElement(ResultsListBox, source) as ListBoxItem;
                if (item?.DataContext is AppEntry entry)
                {
                    _viewModel.SelectedResult = entry;
                    _viewModel.LaunchCommand.Execute(null);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            _source?.RemoveHook(HwndHook);
            base.OnClosed(e);
        }
    }
}
