using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using BLauncher.Services;
using BLauncher.Models;
using System;
using System.Text;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Windows.ApplicationModel.DataTransfer;
using H.NotifyIcon;

namespace BLauncher;

public class MainWindow : Window
{
    private Grid _authView = null!, _profileSetupView = null!;
    private Grid _homeViewContent = null!, _consoleViewContent = null!, _settingsViewContent = null!;
    private Border _homeView = null!, _consoleView = null!, _settingsView = null!;
    
    private TextBlock _logText = null!, _setupStatus = null!, _usernameTag = null!, _appTitleText = null!;
    private Button _setupButton = null!, _refreshButton = null!, _logoutButtonBase = null!;
    private ScrollViewer _logScroll = null!;
    private NavigationView _navView = null!;
    private readonly ModService _modService = new();
    private Border _instanceDetailView = null!;
    private NavigationViewItem _navItemHome = null!, _navItemConsole = null!, _navItemSettings = null!;
    private TextBlock _lastHeartbeatText = null!, _heartbeatStatusText = null!;
    
    private static readonly StringBuilder _logBuilder = new("[Launcher] Initialized...\n");
    private static readonly object _logLock = new object();
    private static bool _updateQueued = false;
    private static MainWindow? _instance;
    private readonly LaunchService _launchService = new();
    private readonly AuthService _authService = new();
    private readonly VersionService _versionService = new();
    private readonly InstanceService _instanceService = new();
    private readonly PresenceManager _presenceManager = new();
    private readonly ModLoaderService _modLoaderService = new();
    private readonly UpdateService _updateService = new();
    private AppWindow? _appWindow;
    private ProgressBar _updateProgressBar = null!;
    private TextBlock _updateStatusText = null!;
    private Button _checkUpdateBtn = null!;
    private TaskbarIcon? _trayIcon;

    public MainWindow()
    {
        _instance = this;
        Title = "BLauncher";
        SetWindowSize(1200, 780);
        TrySetMicaBackdrop();
        ConfigureTitleBar();
        SetupTray();
        BuildUI();
        CheckAuthStatus();
        this.Activated += (s, e) => {
            if (_updateChecked) return;
            _updateChecked = true;
            _ = CheckForUpdatesOnInit();
        };
    }
    private bool _updateChecked = false;

    private async Task CheckForUpdatesOnInit()
    {
        var (available, version, url) = await _updateService.CheckForUpdateAsync();
        if (available && !string.IsNullOrEmpty(url))
        {
            var dlg = new ContentDialog {
                Title = "Critical Update Available",
                Content = $"A new version of BLauncher ({version}) is available. It's recommended to update now to ensure stability and access to new features.",
                PrimaryButtonText = "Update Now",
                CloseButtonText = "Later",
                XamlRoot = this.Content.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                // Switch to settings to show progress if we're in settings, or show a standalone mini-dialog/overlay
                // For simplicity, we just use the progress tracking system we're adding to settings
                _navView.SelectedItem = _navItemSettings;
                ShowView(_settingsView);
                _ = StartUpdateFlow(url);
            }
        }
    }

    private void TrySetMicaBackdrop() => this.SystemBackdrop = new MicaBackdrop();

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow?.Resize(new SizeInt32(width, height));
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath)) {
            _appWindow?.SetIcon(iconPath);
        } else {
            // Try relative as fallback
            _appWindow?.SetIcon("Assets\\icon.ico");
        }
    }

    private void ConfigureTitleBar()
    {
        if (_appWindow != null && AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            this.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor        = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor         = Colors.White;
            titleBar.ButtonHoverBackgroundColor    = ColorHelper.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor  = ColorHelper.FromArgb(60, 255, 255, 255);
            _appWindow.Closing += (s, e) => {
                e.Cancel = true;
                _appWindow.Hide();
                _trayIcon?.ShowNotification("BLauncher", "Launcher is still running in the background.");
            };
        }
    }

    private void SetupTray()
    {
        try {
            _trayIcon = new TaskbarIcon {
                ToolTipText = "BLauncher",
                Icon = new System.Drawing.Icon("Assets\\icon.ico"),
                Visibility = Visibility.Visible
            };
            
            // Tray interactions can be added here if names are confirmed.

            var menu = new MenuFlyout();
            var showItem = new MenuFlyoutItem { Text = "Show Launcher", Icon = new SymbolIcon(Symbol.View) };
            showItem.Click += (s, e) => ShowLauncher();
            menu.Items.Add(showItem);

            var killItem = new MenuFlyoutItem { Text = "Stop Minecraft", Icon = new SymbolIcon(Symbol.Cancel) };
            killItem.Click += (s, e) => _launchService.Terminate();
            menu.Items.Add(killItem);

            var clearItem = new MenuFlyoutItem { Text = "Clear Logs", Icon = new SymbolIcon(Symbol.Clear) };
            clearItem.Click += (s, e) => { if(_instance != null) _instance.DispatcherQueue.TryEnqueue(() => _logText.Text = ""); };
            menu.Items.Add(clearItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var exitItem = new MenuFlyoutItem { Text = "Exit Application", Icon = new SymbolIcon(Symbol.Cancel) };
            exitItem.Click += (s, e) => {
                _trayIcon.Dispose();
                Environment.Exit(0);
            };
            menu.Items.Add(exitItem);

            _trayIcon.ContextFlyout = menu;
        } catch {}
    }

    private void ShowLauncher() {
        _instance?.DispatcherQueue?.TryEnqueue(() => {
            _appWindow?.Show();
            _instance?.Activate();
        });
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private void CheckAuthStatus()
    {
        if (_authService.CurrentSession == null) {
            ShowNav(false);
            ShowView(_authView);
        }
        else if (!_authService.CurrentSession.IsProfileComplete) {
            ShowNav(false);
            ShowView(_profileSetupView);
            UpdateProfileStatus();
        }
        else {
            ShowNav(true);
            ShowHome();
            UpdateUserDetails();
            if (_authService.CurrentSession?.LocalId != null)
                _ = _presenceManager.StartHeartbeat(_authService.CurrentSession.LocalId);
        }
    }

    private void UpdateProfileStatus()
    {
        var s = _authService.CurrentSession;
        if (s == null) return;
        // Profile setup text is static for now but buttons are reactive
    }

    private void UpdateUserDetails()
    {
        var s = _authService.CurrentSession;
        if (s == null) return;
        if (_usernameTag != null) _usernameTag.Text = s.MinecraftUsername ?? "Unknown";
        // Re-generate home view to show personal instances
        _homeView.Child = CreateHomeView();
    }

    private void ShowHome()
    {
        ShowView(_homeView);
        _navView.SelectedItem = _navItemHome;
    }

    private void ShowNav(bool visible)
    {
        _navView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (_appTitleText != null) _appTitleText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowView(FrameworkElement view)
    {
        _authView.Visibility         = Visibility.Collapsed;
        _profileSetupView.Visibility = Visibility.Collapsed;
        _homeView.Visibility         = Visibility.Collapsed;
        _consoleView.Visibility      = Visibility.Collapsed;
        _settingsView.Visibility     = Visibility.Collapsed;
        _instanceDetailView.Visibility = Visibility.Collapsed;
        
        view.Visibility              = Visibility.Visible;
    }

    // ── Gradient helpers ──────────────────────────────────────────────────────

    private LinearGradientBrush GetMainGradient()
    {
        var lgb = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint   = new Windows.Foundation.Point(1, 1)
        };
        lgb.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(255, 18, 18, 30), Offset = 0 });
        lgb.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(255, 12, 12, 22), Offset = 1 });
        return lgb;
    }

    private LinearGradientBrush GetCardGradient()
    {
        var lgb = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint   = new Windows.Foundation.Point(0, 1)
        };
        lgb.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(130, 45, 45, 65), Offset = 0 });
        lgb.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(130, 30, 30, 50), Offset = 1 });
        return lgb;
    }

    // ── Button factory ────────────────────────────────────────────────────────

    private static Button MakeBtn(string label, byte r, byte g, byte b, byte a = 255,
        double width = 0, double height = 40, double cornerRadius = 8,
        bool outlined = false, double fontSize = 13)
    {
        var btn = new Button
        {
            Content    = new TextBlock
            {
                Text       = label,
                FontSize   = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            },
            Height         = height,
            CornerRadius   = new CornerRadius(cornerRadius),
            Background     = outlined ? new SolidColorBrush(Colors.Transparent) : new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b)),
            Foreground     = new SolidColorBrush(Colors.White),
            BorderBrush    = outlined ? new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b)) : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(outlined ? 1 : 0),
            Padding        = new Thickness(24, 0, 24, 0),
        };
        if (width > 0) btn.Width = width;
        return btn;
    }

    // ── Build UI scaffold ─────────────────────────────────────────────────────

    private void BuildUI()
    {
        var rootGrid = new Grid { Background = GetMainGradient() };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Initialize persistent views
        _authView         = CreateAuthView();
        _profileSetupView = CreateProfileSetupView();
        
        // Setup placeholders
        _homeView     = new Border { Visibility = Visibility.Collapsed };
        _consoleView  = new Border { Visibility = Visibility.Collapsed };
        _settingsView = new Border { Visibility = Visibility.Collapsed };
        _instanceDetailView = new Border { Visibility = Visibility.Collapsed };

        // Real content generators
        _consoleView.Child  = CreateConsoleView();
        _settingsView.Child = CreateSettingsView();
        _homeView.Child     = CreateHomeView();

        _navView = new NavigationView
        {
            PaneDisplayMode     = NavigationViewPaneDisplayMode.Top,
            IsSettingsVisible   = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            Background          = new SolidColorBrush(Colors.Transparent),
            Visibility          = Visibility.Collapsed,
            Height              = 48,
            Margin              = new Thickness(0, 4, 140, 0),
            VerticalAlignment   = VerticalAlignment.Top
        };

        _navItemHome     = new NavigationViewItem { Content = "Home",     Icon = new SymbolIcon(Symbol.Home),    Tag = "home" };
        _navItemConsole  = new NavigationViewItem { Content = "Console",  Icon = new SymbolIcon(Symbol.Document), Tag = "console" };
        _navItemSettings = new NavigationViewItem { Content = "Settings", Icon = new SymbolIcon(Symbol.Setting),  Tag = "settings" };

        _navView.MenuItems.Add(_navItemHome);
        _navView.MenuItems.Add(_navItemConsole);
        _navView.MenuItems.Add(_navItemSettings);
        _navView.SelectionChanged += NavView_SelectionChanged;

        Grid.SetRow(_navView, 0); rootGrid.Children.Add(_navView);

        _appTitleText = new TextBlock
        {
            Text = "BLauncher",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(120, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_appTitleText, 0); rootGrid.Children.Add(_appTitleText);

        Grid.SetRow(_homeView,    1); rootGrid.Children.Add(_homeView);
        Grid.SetRow(_consoleView, 1); rootGrid.Children.Add(_consoleView);
        Grid.SetRow(_settingsView,1); rootGrid.Children.Add(_settingsView);
        Grid.SetRow(_instanceDetailView, 1); rootGrid.Children.Add(_instanceDetailView);

        Grid.SetRowSpan(_authView, 2);         rootGrid.Children.Add(_authView);
        Grid.SetRowSpan(_profileSetupView, 2);  rootGrid.Children.Add(_profileSetupView);

        this.Content = rootGrid;
    }

    // ── Views ─────────────────────────────────────────────────────────────────

    private Grid CreateAuthView()
    {
        var g = new Grid { Padding = new Thickness(48), Background = GetMainGradient() };
        var card = new Border { Background = GetCardGradient(), CornerRadius = new CornerRadius(24), Padding = new Thickness(64), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 500 };
        var stack = new StackPanel { Spacing = 28, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "BLauncher", FontSize = 48, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "The next-gen Minecraft portal.", FontSize = 16, Foreground = new SolidColorBrush(ColorHelper.FromArgb(200, 180, 180, 200)), HorizontalAlignment = HorizontalAlignment.Center });
        
        var loginBtn = MakeBtn("Login with CSPackage", 66, 133, 244, height: 56, cornerRadius: 12, fontSize: 16, width: 340);
        var status = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Colors.Gray), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
        
        loginBtn.Click += async (s, e) => {
            loginBtn.IsEnabled = false; status.Visibility = Visibility.Visible;
            bool ok = await _authService.LoginWithCSPackageAsync(m => DispatcherQueue.TryEnqueue(() => status.Text = m));
            if (ok) CheckAuthStatus(); else loginBtn.IsEnabled = true;
        };
        
        stack.Children.Add(loginBtn); stack.Children.Add(status);
        card.Child = stack; g.Children.Add(card);
        return g;
    }

    private Grid CreateProfileSetupView()
    {
        var g = new Grid { Padding = new Thickness(48), Background = GetMainGradient() };
        var card = new Border { Background = GetCardGradient(), CornerRadius = new CornerRadius(24), Padding = new Thickness(64), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 500 };
        var stack = new StackPanel { Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "Set Up Your Profile", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "A Minecraft profile is required to play. You can manage this on the CSPackage dashboard.", FontSize = 14, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        
        var setupBtn  = MakeBtn("Open Dashboard", 70, 80, 200, height: 48, cornerRadius: 12, width: 300);
        var refreshBtn = MakeBtn("Check Again",   50, 50, 70,  height: 48, cornerRadius: 12, width: 300);
        _setupStatus = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Colors.White), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
        
        setupBtn.Click += (s, e) => Process.Start(new ProcessStartInfo("https://cspack.online/apps/blauncher") { UseShellExecute = true });
        refreshBtn.Click += async (s, e) => {
            refreshBtn.IsEnabled = false; _setupStatus.Visibility = Visibility.Visible;
            bool ok = await _authService.ValidateProfileAsync(m => DispatcherQueue.TryEnqueue(() => _setupStatus.Text = m));
            if (ok) CheckAuthStatus(); else refreshBtn.IsEnabled = true;
        };
        
        stack.Children.Add(setupBtn); stack.Children.Add(refreshBtn); stack.Children.Add(_setupStatus);
        card.Child = stack; g.Children.Add(card);
        return g;
    }

    private Grid CreateHomeView()
    {
        var g = new Grid { Padding = new Thickness(40) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Spacing = 24 };
        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        hdr.Children.Add(new TextBlock { Text = "MY INSTANCES", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Gray), CharacterSpacing = 100, VerticalAlignment = VerticalAlignment.Center });
        
        var addBtn = new Button {
            Content = new FontIcon { Glyph = "\uE710", FontSize = 14 }, 
            Width = 32, Height = 32, 
            CornerRadius = new CornerRadius(16), 
            Padding = new Thickness(0),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(addBtn, "Create new instance");

        addBtn.Click += async (s, e) => await ShowAddInstanceDialog();
        hdr.Children.Add(addBtn);
        left.Children.Add(hdr);

        var list = _instanceService.LoadInstances();
        if (list.Count == 0) {
            left.Children.Add(new TextBlock { Text = "No instances found. Create one to get started!", Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0,20,0,0) });
        } else {
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 220, ItemHeight = 160 };
            foreach (var inst in list) {
                var card = CreateInstanceCard(inst);
                card.Margin = new Thickness(0, 0, 16, 16);
                wrap.Children.Add(card);
            }
            left.Children.Add(wrap);
        }
        Grid.SetColumn(left, 0); g.Children.Add(left);

        var right = new StackPanel { Spacing = 24, Margin = new Thickness(40,0,0,0) };
        right.Children.Add(new TextBlock { Text = "UPDATES", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Gray), CharacterSpacing = 100 });
        var news = new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(30, 255, 255, 255)), CornerRadius = new CornerRadius(16), Padding = new Thickness(24), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)) };
        var nc = new StackPanel { Spacing = 12 };
        nc.Children.Add(new TextBlock { Text = "BLauncher Stability Patch", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
        nc.Children.Add(new TextBlock { Text = "New asset downloader and persistent UI engine implemented for maximum performance.", Foreground = new SolidColorBrush(Colors.Gray), TextWrapping = TextWrapping.Wrap });
        news.Child = nc; right.Children.Add(news);
        Grid.SetColumn(right, 1); g.Children.Add(right);

        return g;
    }

    private Border CreateInstanceCard(BLauncher.Models.InstanceMetadata inst)
    {
        var card = new Border { Background = GetCardGradient(), CornerRadius = new CornerRadius(20), Padding = new Thickness(16), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), Width = 200, Height = 250 };
        var mainStack = new Grid();
        mainStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Badge + Settings button
        var hdr = new Grid();
        hdr.ColumnDefinitions.Add(new ColumnDefinition()); hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var badgeColor = inst.ModLoader switch { "Fabric" => ColorHelper.FromArgb(255, 59, 130, 246), "Forge" => Colors.Orange, "NeoForge" => ColorHelper.FromArgb(255, 147, 51, 234), _ => ColorHelper.FromArgb(255, 16, 185, 129) };
        var loaderName = inst.ModLoader.ToLower();
        string ext = (loaderName == "forge") ? "jpeg" : "png";
        var badge = new Image();
        badge.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{loaderName}_logo.{ext}"));
        badge.Width = 32;
        badge.Height = 32;
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(badge, 0); hdr.Children.Add(badge);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        
        Button MkSBtn(Symbol s, Windows.UI.Color fg, string tip) {
            var btn = new Button { 
                Content = new FontIcon { Glyph = ((char)s).ToString(), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 12 }, 
                Foreground = new SolidColorBrush(fg),
                Padding = new Thickness(0), Width = 28, Height = 28, CornerRadius = new CornerRadius(14), 
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(ColorHelper.FromArgb(20, 255, 255, 255))
            };
            ToolTipService.SetToolTip(btn, tip);
            return btn;
        }

        var playSmall = MkSBtn(Symbol.Play, ColorHelper.FromArgb(255, 34, 197, 94), "Play Instance");
        var editBtn = MkSBtn(Symbol.Edit, Colors.White, "Edit/Rename");
        var folderBtn = MkSBtn(Symbol.Folder, Colors.White, "Open instance folder");
        var delBtn = MkSBtn(Symbol.Delete, ColorHelper.FromArgb(255, 244, 63, 94), "Delete Instance");

        playSmall.Click += async (s, e) => {
            if (_launchService.IsRunning) return;
            try {
                _navView.SelectedItem = _navItemConsole;
                ShowView(_consoleView);
                string user = _authService.CurrentSession?.MinecraftUsername ?? "Player";
                await _launchService.LaunchAsync(inst, user, m => AppendLog(m));
            } catch {}
        };
        editBtn.Click += async (s, e) => await ShowManageInstanceDialog(inst);
        folderBtn.Click += (s, e) => {
            string path = _instanceService.GetInstancePath(inst.Id);
            if (Directory.Exists(path)) {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
        };
        delBtn.Click += async (s, e) => {
            var conf = new ContentDialog { Title = "Delete Instance", Content = $"Are you sure you want to delete '{inst.Name}' forever? This will erase all worlds and data inside.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = this.Content.XamlRoot };
            if (await conf.ShowAsync() == ContentDialogResult.Primary) { _instanceService.DeleteInstance(inst.Id); _homeView.Child = CreateHomeView(); }
        };

        actions.Children.Add(playSmall); actions.Children.Add(editBtn); actions.Children.Add(folderBtn); actions.Children.Add(delBtn);
        Grid.SetColumn(actions, 1); hdr.Children.Add(actions);
        
        Grid.SetRow(hdr, 0); mainStack.Children.Add(hdr);

        // Center: Infos
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        infoStack.Children.Add(new TextBlock { Text = inst.Name, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        infoStack.Children.Add(new TextBlock { Text = inst.Version, FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center });
        Grid.SetRow(infoStack, 1); mainStack.Children.Add(infoStack);

        card.Child = mainStack;
        card.Height = 140;
        card.PointerEntered += (s, e) => { card.BorderBrush = new SolidColorBrush(badgeColor); };
        card.PointerExited += (s, e) => { card.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)); };
        card.PointerPressed += (s, e) => {
            _instanceDetailView.Child = CreateInstanceDetailView(inst);
            ShowView(_instanceDetailView);
        };
        return card;
    }

    private async Task ShowManageInstanceDialog(BLauncher.Models.InstanceMetadata inst)
    {
        var stack = new StackPanel { Spacing = 20, Width = 380 };
        var nBox = new TextBox { Header = "Instance Name", Text = inst.Name };
        stack.Children.Add(nBox);

        var dlg = new ContentDialog { 
            Title = "Manage Instance", Content = stack, PrimaryButtonText = "Save", 
            SecondaryButtonText = "Delete Instance", CloseButtonText = "Cancel", 
            XamlRoot = this.Content.XamlRoot 
        };

        var res = await dlg.ShowAsync();
        if (res == ContentDialogResult.Primary) {
            inst.Name = nBox.Text;
            _instanceService.UpdateInstance(inst);
            _homeView.Child = CreateHomeView();
        } 
        else if (res == ContentDialogResult.Secondary) {
            // Confirm delete
            var conf = new ContentDialog { Title = "Confirm Delete", Content = $"Are you sure you want to delete '{inst.Name}'? This will erase all world data inside.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = this.Content.XamlRoot };
            if (await conf.ShowAsync() == ContentDialogResult.Primary) {
                _instanceService.DeleteInstance(inst.Id);
                _homeView.Child = CreateHomeView();
            }
        }
    }

    private Grid CreateConsoleView()
    {
        var g = new Grid { Padding = new Thickness(40) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 24) };
        hdr.Children.Add(new TextBlock { Text = "Console", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Foreground = new SolidColorBrush(Colors.White) });
        
        Button MkIconBtn(Symbol s, Windows.UI.Color bg, string tip) {
            var btn = new Button { 
                Content = new FontIcon { Glyph = ((char)s).ToString(), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 14 },
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Padding = new Thickness(0),
                Background = new SolidColorBrush(bg), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(btn, tip);
            return btn;
        }

        var killBtn = MkIconBtn(Symbol.Cancel, ColorHelper.FromArgb(255, 239, 68, 68), "Kill Process");
        var clearBtn = MkIconBtn(Symbol.Delete, ColorHelper.FromArgb(255, 71, 85, 105), "Clear Console");
        var copyBtn = MkIconBtn(Symbol.Copy, ColorHelper.FromArgb(255, 59, 130, 246), "Copy Logs");
        
        killBtn.Click += (s, e) => { _launchService.Terminate(); AppendLog("Killed Minecraft process."); };
        clearBtn.Click += (s, e) => { lock (_logLock) { _logBuilder.Clear(); _logBuilder.AppendLine("[Launcher] Cleared."); _logText.Text = _logBuilder.ToString(); } };
        copyBtn.Click += (s, e) => { string t; lock (_logLock) { t = _logBuilder.ToString(); } var dp = new DataPackage(); dp.SetText(t); Clipboard.SetContent(dp); };
        
        hdr.Children.Add(killBtn); hdr.Children.Add(clearBtn); hdr.Children.Add(copyBtn);
        Grid.SetRow(hdr, 0); g.Children.Add(hdr);

        _logText = new TextBlock { Text = _logBuilder.ToString(), Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 200, 210, 220)), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
        _logScroll = new ScrollViewer { Content = _logText, Background = new SolidColorBrush(ColorHelper.FromArgb(200, 10, 10, 20)), Padding = new Thickness(24), CornerRadius = new CornerRadius(16), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1) };
        
        Grid.SetRow(_logScroll, 1); g.Children.Add(_logScroll);
        return g;
    }

    private Grid CreateSettingsView()
    {
        var g = new Grid { Padding = new Thickness(40) };
        var stack = new StackPanel { Spacing = 32, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(new TextBlock { Text = "Settings", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Foreground = new SolidColorBrush(Colors.White) });

        var s = _authService.CurrentSession;
        var card = new Border { Background = GetCardGradient(), CornerRadius = new CornerRadius(20), Padding = new Thickness(32), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1) };
        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition()); details.ColumnDefinitions.Add(new ColumnDefinition());
        for(int i=0; i<4; i++) details.RowDefinitions.Add(new RowDefinition());

        void AddD(string l, string? v, int r, int c, out TextBlock valOut) {
            var st = new StackPanel { Margin = new Thickness(0,0,20,20) };
            st.Children.Add(new TextBlock { Text = l, FontSize = 10, Foreground = new SolidColorBrush(Colors.Gray), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            valOut = new TextBlock { Text = v ?? "N/A", FontSize = 16, Foreground = new SolidColorBrush(Colors.White) };
            st.Children.Add(valOut);
            Grid.SetRow(st, r); Grid.SetColumn(st, c); details.Children.Add(st);
        }

        AddD("USERNAME", s?.MinecraftUsername, 0, 0, out _);
        AddD("EMAIL", s?.Email, 0, 1, out _);
        AddD("COOKIE POINTS", $"{s?.Balance:F0} CP", 1, 0, out _);
        AddD("UID", s?.LocalId, 1, 1, out _);
        
        AddD("LAST HEARTBEAT", "N/A", 2, 0, out _lastHeartbeatText);
        AddD("HEARTBEAT STATUS", "N/A", 2, 1, out _heartbeatStatusText);
        
        UpdateSettingsDisplay();

        card.Child = details; 

        // Update Card
        var updateCard = new Border { Background = GetCardGradient(), CornerRadius = new CornerRadius(20), Padding = new Thickness(32), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1) };
        var upStack = new StackPanel { Spacing = 16 };
        upStack.Children.Add(new TextBlock { Text = "APP VERSION", FontSize = 10, Foreground = new SolidColorBrush(Colors.Gray), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        
        var verRow = new Grid();
        verRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        verRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var currentVerLab = new TextBlock { Text = $"v{UpdateService.CurrentVersion}", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(currentVerLab, 0); verRow.Children.Add(currentVerLab);
        
        _checkUpdateBtn = MakeBtn("Check for Updates", 59, 130, 246, height: 38, cornerRadius: 10);
        _checkUpdateBtn.Margin = new Thickness(16, 0, 0, 0);
        _checkUpdateBtn.Click += async (s, e) => {
            _checkUpdateBtn.IsEnabled = false;
            _updateStatusText.Text = "Checking...";
            _updateStatusText.Visibility = Visibility.Visible;
            var (av, v, url) = await _updateService.CheckForUpdateAsync();
            if (av && !string.IsNullOrEmpty(url)) {
                _updateStatusText.Text = $"v{v} is available!";
                _checkUpdateBtn.Content = "Update Now";
                _checkUpdateBtn.IsEnabled = true;
                _checkUpdateBtn.Click += (s2, e2) => { _ = StartUpdateFlow(url); };
            } else {
                _updateStatusText.Text = "You're on the latest version.";
                _checkUpdateBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(_checkUpdateBtn, 1); verRow.Children.Add(_checkUpdateBtn);
        upStack.Children.Add(verRow);

        _updateProgressBar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Value = 0, Visibility = Visibility.Collapsed, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 133, 244)), Background = new SolidColorBrush(ColorHelper.FromArgb(30, 255, 255, 255)), IsIndeterminate = false };
        _updateStatusText = new TextBlock { Text = "", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
        
        upStack.Children.Add(_updateProgressBar);
        upStack.Children.Add(_updateStatusText);
        updateCard.Child = upStack;

        var cardsGrid = new Grid();
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnSpacing = 24;

        Grid.SetColumn(card, 0); cardsGrid.Children.Add(card);
        Grid.SetColumn(updateCard, 1); cardsGrid.Children.Add(updateCard);

        stack.Children.Add(cardsGrid);
        
        var logoutBtn = MakeBtn("Sign Out", 180, 50, 50, height: 44, cornerRadius: 8);
        logoutBtn.Click += (s, e) => { _presenceManager.StopHeartbeat(); _authService.Logout(); CheckAuthStatus(); };
        stack.Children.Add(logoutBtn);

        g.Children.Add(stack);

        return g;
    }

    private async Task StartUpdateFlow(string url)
    {
        _checkUpdateBtn.IsEnabled = false;
        _updateProgressBar.Visibility = Visibility.Visible;
        _updateStatusText.Visibility = Visibility.Visible;
        _updateStatusText.Text = "Downloading update... 0%";
        
        await _updateService.DownloadAndInstallAsync(url, progress => {
            DispatcherQueue.TryEnqueue(() => {
                _updateProgressBar.Value = progress;
                _updateStatusText.Text = $"Downloading update... {progress:F0}%";
            });
        });
    }

    private async Task ShowAddInstanceDialog()
    {
        // Step 1
        var loaderContent = new StackPanel { Spacing = 20, Width = 440 };
        loaderContent.Children.Add(new TextBlock { Text = "Select Mod Loader", FontSize = 14, Foreground = new SolidColorBrush(Colors.Gray) });
        var loaderGrid = new Grid();
        loaderGrid.ColumnDefinitions.Add(new ColumnDefinition()); loaderGrid.ColumnDefinitions.Add(new ColumnDefinition());
        loaderGrid.RowDefinitions.Add(new RowDefinition { MinHeight = 90 }); loaderGrid.RowDefinitions.Add(new RowDefinition { MinHeight = 90 });
        
        string sel = "Vanilla"; Border? selC = null;
        Border MkC(string n, string d, int r, int c) {
            var b = new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(4), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Colors.Transparent) };
            var sp = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = n, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
            sp.Children.Add(new TextBlock { Text = d, FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
            b.Child = sp; Grid.SetRow(b, r); Grid.SetColumn(b, c); return b;
        }
        var cV = MkC("Vanilla", "Default", 0,0); var cF = MkC("Fabric", "Lightweight", 0,1);
        var cG = MkC("Forge (WIP)", "Experimental", 1,0); var cN = MkC("NeoForge (WIP)", "Experimental", 1,1);
        
        void Sl(Border b, string n) { if(selC!=null) selC.BorderBrush = new SolidColorBrush(Colors.Transparent); selC = b; b.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 133, 244)); sel = n; }
        cV.PointerPressed += (s,e)=>Sl(cV,"Vanilla"); cF.PointerPressed += (s,e)=>Sl(cF,"Fabric");
        cG.PointerPressed += (s,e)=>Sl(cG,"Forge"); cN.PointerPressed += (s,e)=>Sl(cN,"NeoForge");
        loaderGrid.Children.Add(cV); loaderGrid.Children.Add(cF); loaderGrid.Children.Add(cG); loaderGrid.Children.Add(cN);
        loaderContent.Children.Add(loaderGrid); Sl(cV, "Vanilla");

        var d1 = new ContentDialog { Title = "Create Instance", Content = loaderContent, PrimaryButtonText = "Next", CloseButtonText = "Cancel", XamlRoot = this.Content.XamlRoot };
        if (await d1.ShowAsync() != ContentDialogResult.Primary) return;

        // Step 2
        var cfg = new StackPanel { Spacing = 16, Width = 440 };
        var nBox = new TextBox { Header = "Name", Text = sel };
        var vBox = new ComboBox { Header = "Minecraft Version", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        var lvBox = new ComboBox { Header = "Loader Version", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false, Visibility = sel == "Vanilla" ? Visibility.Collapsed : Visibility.Visible };
        var status = new TextBlock { Text = "Loading versions...", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) };
        
        cfg.Children.Add(nBox); cfg.Children.Add(vBox); cfg.Children.Add(lvBox); cfg.Children.Add(status);
        var d2 = new ContentDialog { Title = "Configure " + sel, Content = cfg, PrimaryButtonText = "Create", SecondaryButtonText = "Back", CloseButtonText = "Cancel", XamlRoot = this.Content.XamlRoot, IsPrimaryButtonEnabled = false };
        
        _ = Task.Run(async () => {
            var vs = await _versionService.GetAvailableVersionsAsync();
            DispatcherQueue.TryEnqueue(() => { 
                vBox.Items.Clear(); 
                foreach(var v in vs) vBox.Items.Add(v); 
                vBox.SelectedIndex = 0; 
                vBox.IsEnabled = true; 
                if(sel=="Vanilla") d2.IsPrimaryButtonEnabled = true;

                // ATTACH EVENT HANDLER ONLY AFTER INITIAL POPULATION
                vBox.SelectionChanged += async (s,e) => {
                    if (sel == "Vanilla" || vBox.SelectedItem == null || !vBox.IsEnabled) return;
                    string raw = vBox.SelectedItem.ToString()!;
                    string m = raw.Split(' ')[0].Trim();
                    status.Text = "Fetching " + sel + " versions for " + m + "..."; d2.IsPrimaryButtonEnabled = false;
                    var lvs = (sel == "Fabric") ? await _modLoaderService.GetFabricLoadersAsync(m) : (sel == "Forge") ? await _modLoaderService.GetForgeVersionsAsync(m) : await _modLoaderService.GetNeoForgeVersionsAsync(m);
                    DispatcherQueue.TryEnqueue(() => {
                        if (vBox.SelectedItem?.ToString()?.Split(' ')[0].Trim() != m) return;
                        lvBox.Items.Clear(); 
                        foreach(var v in lvs) lvBox.Items.Add(v.Version); 
                        if(lvs.Count > 0) lvBox.SelectedIndex = 0; 
                        lvBox.IsEnabled = lvs.Count > 0; 
                        d2.IsPrimaryButtonEnabled = lvs.Count > 0;
                        status.Text = lvs.Count > 0 ? "Ready" : "No loaders for " + m;
                    });
                };
            });
        });

        var res = await d2.ShowAsync();
        if (res == ContentDialogResult.Secondary) { await ShowAddInstanceDialog(); return; }
        if (res != ContentDialogResult.Primary) return;

        string mcVerRaw = vBox.SelectedItem?.ToString() ?? "1.21.1";
        string mcVersion = mcVerRaw.Split(' ')[0];
        mcVersion = new string(mcVersion.Where(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');

        _instanceService.AddInstance(new BLauncher.Models.InstanceMetadata { 
            Name = nBox.Text, 
            Version = mcVersion, 
            ModLoader = sel, 
            LoaderVersion = lvBox.SelectedItem?.ToString() ?? "" 
        });
        _homeView.Child = CreateHomeView();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "home": ShowView(_homeView); break;
                case "console": ShowView(_consoleView); break;
                case "settings": 
                    UpdateSettingsDisplay();
                    ShowView(_settingsView); 
                    break;
            }
        }
    }

    private void UpdateSettingsDisplay()
    {
        if (_lastHeartbeatText == null || _heartbeatStatusText == null) return;

        if (_presenceManager.LastSignalTime == DateTime.MinValue)
        {
            _lastHeartbeatText.Text = "Not yet sent";
            _heartbeatStatusText.Text = "Waiting...";
            _heartbeatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        }
        else
        {
            _lastHeartbeatText.Text = _presenceManager.LastSignalTime.ToString("HH:mm:ss");
            _heartbeatStatusText.Text = _presenceManager.LastSignalSuccess ? "Success" : "Failed";
            _heartbeatStatusText.Foreground = new SolidColorBrush(_presenceManager.LastSignalSuccess ? Colors.LimeGreen : Colors.OrangeRed);
        }
    }

    private Grid CreateInstanceDetailView(BLauncher.Models.InstanceMetadata inst)
    {
        var g = new Grid { Padding = new Thickness(40) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 24) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backBtn = new Button { 
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 14 }, 
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), 
            Background = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), Padding = new Thickness(0) 
        };
        backBtn.Click += (s, e) => ShowView(_homeView);
        Grid.SetColumn(backBtn, 0); header.Children.Add(backBtn);

        var titleStack = new StackPanel { Margin = new Thickness(16,0,0,0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = inst.Name, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
        titleStack.Children.Add(new TextBlock { Text = $"{inst.ModLoader} {inst.Version}", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) });
        Grid.SetColumn(titleStack, 1); header.Children.Add(titleStack);

        var playBtn = MakeBtn("Launch", 59, 130, 246, height: 40, cornerRadius: 20);
        playBtn.Padding = new Thickness(32, 0, 32, 0);
        playBtn.Click += async (s, e) => {
            _navView.SelectedItem = _navItemConsole; ShowView(_consoleView);
            await _launchService.LaunchAsync(inst, _authService.CurrentSession?.MinecraftUsername ?? "Player", m => AppendLog(m));
        };
        Grid.SetColumn(playBtn, 2); header.Children.Add(playBtn);
        Grid.SetRow(header, 0); g.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var left = new StackPanel { Spacing = 20 };
        var tabs = new Pivot();
        
        // Mods Tab
        var modsView = new StackPanel { Spacing = 15 };
        var searchBox = new TextBox { PlaceholderText = "Search mods...", Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8) };
        modsView.Children.Add(searchBox);

        var modsList = new ListView { SelectionMode = ListViewSelectionMode.Single, Background = new SolidColorBrush(Colors.Transparent), Height = 400 };
        
        var mods = _modService.LoadMods(_instanceService.GetModsPath(inst.Id));
        void PopulateMods(string filter = "") {
            modsList.Items.Clear();
            foreach (var m in mods.Where(x => x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))) {
                var row = new Grid { Padding = new Thickness(12), Background = new SolidColorBrush(ColorHelper.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(ColorHelper.FromArgb(60, 255,255,255)), Margin = new Thickness(0,0,12,0) };
                icon.Child = new FontIcon { Glyph = "\uE943", FontSize = 16 };
                Grid.SetColumn(icon, 0); row.Children.Add(icon);

                var nameStack = new StackPanel();
                nameStack.Children.Add(new TextBlock { Text = m.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
                nameStack.Children.Add(new TextBlock { Text = m.Author, FontSize = 10, Foreground = new SolidColorBrush(Colors.Gray) });
                Grid.SetColumn(nameStack, 1); row.Children.Add(nameStack);

                var verText = new TextBlock { Text = m.Version, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 20, 0) };
                Grid.SetColumn(verText, 2); row.Children.Add(verText);
                
                var toggle = new ToggleSwitch { OnContent = "", OffContent = "", IsOn = m.Enabled, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(toggle, 3);
                toggle.Toggled += (s, e) => _modService.ToggleMod(_instanceService.GetModsPath(inst.Id), m, toggle.IsOn);
                row.Children.Add(toggle);

                modsList.Items.Add(row);
            }
        }
        PopulateMods();
        searchBox.TextChanged += (s,e) => PopulateMods(searchBox.Text);
        modsView.Children.Add(modsList);

        tabs.Items.Add(new PivotItem { Header = "Mods", Content = modsView });
        tabs.Items.Add(new PivotItem { Header = "Worlds", Content = new TextBlock { Text = "Saves manager coming soon...", Margin = new Thickness(20) } });
        tabs.Items.Add(new PivotItem { Header = "Servers", Content = new TextBlock { Text = "Server list manager coming soon...", Margin = new Thickness(20) } });

        Grid.SetColumn(tabs, 0); content.Children.Add(tabs);

        // Details Side Pane
        var side = new Border { 
            Margin = new Thickness(24, 48, 0, 0), Padding = new Thickness(24), 
            Background = new SolidColorBrush(ColorHelper.FromArgb(30, 255, 255, 255)), 
            CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Top 
        };
        var sideStack = new StackPanel { Spacing = 12 };
        sideStack.Children.Add(new TextBlock { Text = "Instance Info", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        sideStack.Children.Add(new TextBlock { Text = "Created: " + inst.CreatedAt.ToShortDateString(), FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) });
        sideStack.Children.Add(new TextBlock { Text = "Last Played: " + (inst.LastPlayedAt == DateTime.MinValue ? "Never" : inst.LastPlayedAt.ToShortDateString()), FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) });
        
        var openFold = MakeBtn("Open Folder", 100, 100, 100, height: 32, cornerRadius: 8);
        openFold.Click += (s,e) => Process.Start("explorer.exe", _instanceService.GetInstancePath(inst.Id));
        sideStack.Children.Add(openFold);

        side.Child = sideStack;
        Grid.SetColumn(side, 1); content.Children.Add(side);

        Grid.SetRow(content, 1); g.Children.Add(content);
        return g;
    }

    public static void AppendLog(string msg)
    {
        try {
            lock (_logLock) {
                if (_logBuilder.Length > 250000)
                    _logBuilder.Remove(0, _logBuilder.Length - 150000);
                
                _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

                if (!_updateQueued && _instance != null) {
                    _updateQueued = true;
                    _instance.DispatcherQueue.TryEnqueue(() => {
                        try {
                            lock (_logLock) {
                                if (_instance?._logText != null) {
                                    _instance._logText.Text = _logBuilder.ToString();
                                    _instance._logScroll?.ChangeView(null, _instance._logScroll.ScrollableHeight, null);
                                }
                                _updateQueued = false;
                            }
                        } catch {}
                    });
                }
            }
        } catch {}
    }
}
