using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskForce11Launcher.Interop;
using TaskForce11Launcher.ViewModels;

namespace TaskForce11Launcher;

public partial class MainWindow : Window
{
    private const int TabMods = 0;
    private const int TabLog = 1;

    private readonly MainViewModel _viewModel;
    private bool _drawerOpen;
    private int _activeTab = TabMods;

    public MainWindow()
    {
        InitializeComponent();
        WindowCornerHelper.ApplyRoundedCorners(this);
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel.Settings) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _viewModel.SaveSettings(settingsWindow.ResultSettings);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnLogTextChanged(object sender, TextChangedEventArgs e) => LogTextBox.ScrollToEnd();

    // --- Schublade Mods/Verlauf ----------------------------------------------
    // Ob sie offen ist und welcher Reiter aktiv ist, ist reiner Anzeigezustand ohne
    // Bedeutung fuer die Anwendungslogik - deshalb hier und nicht im ViewModel.

    private void OnModsChipClick(object sender, RoutedEventArgs e) => ToggleDrawer(TabMods);

    private void OnHistoryClick(object sender, RoutedEventArgs e) => ToggleDrawer(TabLog);

    private void OnModsTabClick(object sender, MouseButtonEventArgs e) => SetActiveTab(TabMods);

    private void OnLogTabClick(object sender, MouseButtonEventArgs e) => SetActiveTab(TabLog);

    private void OnCloseDrawerClick(object sender, RoutedEventArgs e) => CloseDrawer();

    private void OnScrimClick(object sender, MouseButtonEventArgs e) => CloseDrawer();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drawerOpen) CloseDrawer();
    }

    private void ToggleDrawer(int tab)
    {
        // Derselbe Knopf noch einmal schliesst wieder; ein anderer wechselt nur den
        // Reiter, statt die Schublade zu- und wieder aufzuklappen.
        if (_drawerOpen && _activeTab == tab)
        {
            CloseDrawer();
            return;
        }

        SetActiveTab(tab);
        if (!_drawerOpen) OpenDrawer();
    }

    private void SetActiveTab(int tab)
    {
        _activeTab = tab;
        ModsTabContent.Visibility = tab == TabMods ? Visibility.Visible : Visibility.Collapsed;
        LogTabContent.Visibility = tab == TabLog ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = (Brush)FindResource("TabActiveBrush");
        var accentBrush = (Brush)FindResource("AccentBrightBrush");
        var mutedBrush = (Brush)FindResource("MutedBrush");

        ModsTabChrome.Background = tab == TabMods ? activeBrush : Brushes.Transparent;
        ModsTabLabel.Foreground = tab == TabMods ? accentBrush : mutedBrush;
        LogTabChrome.Background = tab == TabLog ? activeBrush : Brushes.Transparent;
        LogTabLabel.Foreground = tab == TabLog ? accentBrush : mutedBrush;

        if (tab == TabLog) LogTextBox.ScrollToEnd();
    }

    private void OpenDrawer()
    {
        _drawerOpen = true;
        DrawerScrim.Visibility = Visibility.Visible;
        DrawerPanel.Visibility = Visibility.Visible;
        AnimateDrawer(show: true);
    }

    private void CloseDrawer()
    {
        if (!_drawerOpen) return;
        _drawerOpen = false;
        AnimateDrawer(show: false);
    }

    private void AnimateDrawer(bool show)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };

        var panelOpacity = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
        var panelScaleX = new DoubleAnimation(show ? 1 : 0.96, duration) { EasingFunction = ease };
        var panelScaleY = new DoubleAnimation(show ? 1 : 0.96, duration) { EasingFunction = ease };
        var scrimOpacity = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };

        if (!show)
        {
            // Erst nach dem Ausblenden wirklich ausblenden - sonst verschwindet die
            // Schublade schlagartig, statt weich auszulaufen.
            panelOpacity.Completed += (_, _) =>
            {
                DrawerPanel.Visibility = Visibility.Collapsed;
                DrawerScrim.Visibility = Visibility.Collapsed;
            };
        }

        DrawerPanel.BeginAnimation(OpacityProperty, panelOpacity);
        ((ScaleTransform)DrawerPanel.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, panelScaleX);
        ((ScaleTransform)DrawerPanel.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, panelScaleY);
        DrawerScrim.BeginAnimation(OpacityProperty, scrimOpacity);
    }
}
