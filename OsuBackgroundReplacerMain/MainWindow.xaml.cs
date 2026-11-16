using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using OsuBackgroundReplacerMain.Services;
using OsuBackgroundReplacerMain.Views;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OsuBackgroundReplacerMain
{
    public sealed partial class MainWindow : Window
    {
        public new static MainWindow Current { get; private set; } = null!;

        private bool _isLoaded;
        private double _lastIconScale;
        private IntPtr _hSmallIcon;
        private IntPtr _hBigIcon;
        private readonly string _iconPath;
        private readonly string _logoPngPath;
        private static readonly DialogQueue _dialogQueue = new();

        public MainWindow()
        {
            this.InitializeComponent();
            Current = this;

            _iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "obr_icon.ico");
            _logoPngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.png");

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 1024;
            presenter.PreferredMinimumHeight = 600;
            this.AppWindow.SetPresenter(presenter);

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1024, 720));

            if (File.Exists(_iconPath))
            {
                ApplyDpiAwareWindowIcons(_iconPath);
            }

            ApplySavedTheme();

            RootGrid.Loaded += MainWindow_Loaded;
            RootGrid.Unloaded += MainWindow_Unloaded;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            if (RootGrid.XamlRoot != null)
            {
                RootGrid.XamlRoot.Changed -= XamlRoot_Changed;
                RootGrid.XamlRoot.Changed += XamlRoot_Changed;
            }
            UpdateIconsForDpi();

            LogService.EntriesChanged -= UpdateLogBadge;
            LogService.EntriesChanged += UpdateLogBadge;
            UpdateLogBadge();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            if (RootGrid.XamlRoot != null)
            {
                RootGrid.XamlRoot.Changed -= XamlRoot_Changed;
            }
            LogService.EntriesChanged -= UpdateLogBadge;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _isLoaded = false;
            if (RootGrid.XamlRoot != null)
            {
                RootGrid.XamlRoot.Changed -= XamlRoot_Changed;
            }
            LogService.EntriesChanged -= UpdateLogBadge;
            DestroyWindowIconHandles();
        }

        private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            UpdateIconsForDpi();
        }

        private void UpdateIconsForDpi()
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (Math.Abs(scale - _lastIconScale) < 0.01)
            {
                return;
            }

            _lastIconScale = scale;

            if (File.Exists(_logoPngPath))
            {
                int pixelSize = Math.Max(20, (int)Math.Round(20.0 * scale));
                TitleBarIcon.Source = new BitmapImage(new Uri(_logoPngPath))
                {
                    DecodePixelType = DecodePixelType.Physical,
                    DecodePixelWidth = pixelSize,
                    DecodePixelHeight = pixelSize
                };
            }

            if (File.Exists(_iconPath))
            {
                ApplyDpiAwareWindowIcons(_iconPath);
            }
        }

        private void ApplyDpiAwareWindowIcons(string iconPath)
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            uint dpi = GetDpiForWindow(hWnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            int smallSize = Math.Max(16, (int)Math.Round(16.0 * dpi / 96.0));
            const int bigSize = 256;

            IntPtr newSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, smallSize, smallSize, LR_LOADFROMFILE);
            IntPtr newBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, bigSize, bigSize, LR_LOADFROMFILE);

            if (newBig != IntPtr.Zero)
            {
                this.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(newBig));
                SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, newBig);
                SetClassLongPtr(hWnd, GCLP_HICON, newBig);
                if (_hBigIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hBigIcon);
                }
                _hBigIcon = newBig;
            }

            if (newSmall != IntPtr.Zero)
            {
                SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, newSmall);
                SetClassLongPtr(hWnd, GCLP_HICONSM, newSmall);
                if (_hSmallIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hSmallIcon);
                }
                _hSmallIcon = newSmall;
            }
        }

        private void DestroyWindowIconHandles()
        {
            if (_hSmallIcon != IntPtr.Zero)
            {
                DestroyIcon(_hSmallIcon);
                _hSmallIcon = IntPtr.Zero;
            }

            if (_hBigIcon != IntPtr.Zero)
            {
                DestroyIcon(_hBigIcon);
                _hBigIcon = IntPtr.Zero;
            }
        }

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

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
                if (Current?.DispatcherQueue == null)
                {
                    return ContentDialogResult.None;
                }

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
            var xamlRoot = Current?.Content?.XamlRoot;
            if (xamlRoot == null)
            {
                return ContentDialogResult.None;
            }

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
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