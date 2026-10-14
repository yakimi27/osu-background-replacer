using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using OsuBackgroundReplacerMain.Services;
using OsuBackgroundReplacerMain.Views;
using System;
using System.Threading.Tasks;

namespace OsuBackgroundReplacerMain
{
    public sealed partial class MainWindow : Window
    {
        public new static MainWindow Current { get; private set; } = null!;

        private bool _isLoaded;
        private static readonly DialogQueue _dialogQueue = new();

        public MainWindow()
        {
            this.InitializeComponent();
            Current = this;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 1024;
            presenter.PreferredMinimumHeight = 600;
            this.AppWindow.SetPresenter(presenter);

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 720));

            ApplySavedTheme();

            RootGrid.Loaded += MainWindow_Loaded;
            RootGrid.Unloaded += MainWindow_Unloaded;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            LogService.EntriesChanged -= UpdateLogBadge;
            LogService.EntriesChanged += UpdateLogBadge;
            UpdateLogBadge();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            LogService.EntriesChanged -= UpdateLogBadge;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _isLoaded = false;
            LogService.EntriesChanged -= UpdateLogBadge;
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = ReplaceNavItem;
            ContentFrame.Navigate(typeof(ReplacePage), null, new EntranceNavigationTransitionInfo());
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
            }
            else if (args.SelectedItemContainer is NavigationViewItem item)
            {
                Type? targetPage = item.Tag switch
                {
                    "ReplacePage" => typeof(ReplacePage),
                    "ActivityLogPage" => typeof(ActivityLogPage),
                    _ => null
                };

                if (targetPage != null && ContentFrame.CurrentSourcePageType != targetPage)
                {
                    ContentFrame.Navigate(targetPage, null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
                }
            }
        }

        private void UpdateLogBadge()
        {
            if (!_isLoaded) return;

            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isLoaded) return;

                int count = LogService.TotalReplacedCount;
                if (count > 0)
                {
                    NavLogBadge.Value = count;
                    NavLogBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    NavLogBadge.Visibility = Visibility.Collapsed;
                }
            });
        }

        public void SetAppTheme(ElementTheme theme)
        {
            RootGrid.RequestedTheme = theme;
        }

        private void ApplySavedTheme()
        {
            string theme = SettingsService.Current.AppTheme;
            ElementTheme target = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            SetAppTheme(target);
        }

        public static async Task<ContentDialogResult> ShowDialogAsync(string content, string title, string primaryBtnText = "OK", string? closeBtnText = null)
        {
            return await _dialogQueue.EnqueueAsync(async () =>
            {
                if (Current.DispatcherQueue.HasThreadAccess)
                {
                    return await ShowDialogInternal(content, title, primaryBtnText, closeBtnText);
                }
                else
                {
                    var tcs = new TaskCompletionSource<ContentDialogResult>();
                    bool enqueued = Current.DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            var result = await ShowDialogInternal(content, title, primaryBtnText, closeBtnText);
                            tcs.SetResult(result);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });

                    if (!enqueued)
                    {
                        tcs.SetException(new InvalidOperationException("Failed to enqueue dialog operation to the UI thread dispatcher queue."));
                    }

                    return await tcs.Task;
                }
            });
        }

        private static async Task<ContentDialogResult> ShowDialogInternal(string content, string title, string primaryButtonText, string? closeButtonText)
        {
            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = Current.Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText
            };
            var result = await dialog.ShowAsync();
            await Task.Yield();
            return result;
        }
    }
}