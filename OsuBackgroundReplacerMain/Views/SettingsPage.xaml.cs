using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OsuBackgroundReplacerMain.Logic;
using OsuBackgroundReplacerMain.Services;

namespace OsuBackgroundReplacerMain.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            ConfirmToggle.IsOn = SettingsService.Current.ConfirmBeforeReplace;
            SaveFolderToggle.IsOn = SettingsService.Current.SaveLastFolderPath;
            SaveImageToggle.IsOn = SettingsService.Current.SaveLastImagePath;

            string currentTheme = SettingsService.Current.AppTheme;
            int themeIndex = currentTheme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };
            ThemeComboBox.SelectedIndex = themeIndex;

            _isInitializing = false;
        }

        private void ConfirmToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsService.Current.ConfirmBeforeReplace = ConfirmToggle.IsOn;
            SettingsService.Save();
        }

        private void SaveFolderToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsService.Current.SaveLastFolderPath = SaveFolderToggle.IsOn;
            if (!SaveFolderToggle.IsOn)
            {
                SettingsService.Current.LastFolderPath = string.Empty;
            }
            else
            {
                SettingsService.Current.LastFolderPath = FolderOperations.getPath() ?? string.Empty;
            }
            SettingsService.Save();
        }

        private void SaveImageToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsService.Current.SaveLastImagePath = SaveImageToggle.IsOn;
            if (!SaveImageToggle.IsOn)
            {
                SettingsService.Current.LastImagePath = string.Empty;
            }
            else
            {
                SettingsService.Current.LastImagePath = ImageOperations.getPath() ?? string.Empty;
            }
            SettingsService.Save();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Current.AppTheme = tag;
                SettingsService.Save();

                ElementTheme targetTheme = tag switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };

                MainWindow.Current.SetAppTheme(targetTheme);
            }
        }

        private void RedetectPath_Click(object sender, RoutedEventArgs e)
        {
            string? detected = SettingsService.TryDetectOsuSongsPath();
            if (!string.IsNullOrEmpty(detected))
            {
                FolderOperations.setPath(detected);
                SettingsInfoBar.Message = $"Detected osu! Songs at: {detected}";
                SettingsInfoBar.Severity = InfoBarSeverity.Success;
                SettingsInfoBar.IsOpen = true;
            }
            else
            {
                SettingsInfoBar.Message = "No standard osu! Songs directory found. Please specify it manually on the Replace page.";
                SettingsInfoBar.Severity = InfoBarSeverity.Warning;
                SettingsInfoBar.IsOpen = true;
            }
        }
    }
}
