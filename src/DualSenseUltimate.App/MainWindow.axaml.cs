using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.App.Pages;

namespace DualSenseUltimate.App;

public partial class MainWindow : Window
{
    private readonly (string Name, string Icon, Func<PageBase> Create)[] _pages;
    private readonly Dictionary<int, PageBase> _cache = new();
    private readonly DispatcherTimer _liveTimer, _slowTimer;
    private bool _updatingPicker;
    public bool ForceClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _pages =
        [
            ("Dashboard", "M3,3h8v8h-8z M13,3h8v5h-8z M13,10h8v11h-8z M3,13h8v8h-8z", () => new DashboardPage(this)),
            ("Mandos", "M6,7 h12 a5,5 0 0 1 5,5 v2 a4,4 0 0 1 -7.5,2 h-7 a4,4 0 0 1 -7.5,-2 v-2 a5,5 0 0 1 5,-5 z", () => new ControllersPage()),
            ("Iluminación RGB", "M12,6 a6,6 0 1 0 0.01,0 z M11,1h2v3h-2z M11,20h2v3h-2z M1,11h3v2h-3z M20,11h3v2h-3z", () => new LightingPage()),
            ("Adaptive Triggers", "M5,3 h9 a6,6 0 0 1 6,6 v12 h-6 v-9 h-9 z", () => new TriggersPage()),
            ("Háptica", "M3,10h2v4h-2z M7,6h2v12h-2z M11,3h2v18h-2z M15,7h2v10h-2z M19,10h2v4h-2z", () => new HapticsPage()),
            ("Remapeo", "M4,7 h12 v-3 l5,4.5 l-5,4.5 v-3 h-12 z M20,17 h-12 v3 l-5,-4.5 l5,-4.5 v3 h12 z", () => new RemapPage()),
            ("Joysticks y giroscopio", "M12,2 a10,10 0 1 0 0.01,0 z M12,8 a4,4 0 1 1 -0.01,0 z", () => new SticksPage()),
            ("Perfiles", "M12,2 l10,5 l-10,5 l-10,-5 z M4,11 l8,4 l8,-4 v3 l-8,4 l-8,-4 z M4,16 l8,4 l8,-4 v3 l-8,4 l-8,-4 z", () => new ProfilesPage()),
            ("Biblioteca comunitaria", "M8,4 a3.5,3.5 0 1 0 0.01,0 z M16,4 a3.5,3.5 0 1 0 0.01,0 z M1,20 a7,7 0 0 1 14,0 z M13,20 a7,7 0 0 1 10,-6.3 v6.3 z", () => new CommunityPage()),
            ("Automatizaciones", "M13,2 L4,14 h6 l-1,8 l9,-12 h-6 z", () => new AutomationsPage()),
            ("Plugins", "M9,2 h6 v4 h5 v6 h-3 a2,2 0 0 0 0,4 h3 v6 h-16 v-6 h3 a2,2 0 0 0 0,-4 h-3 v-6 h5 z", () => new PluginsPage()),
            ("API", "M8,6 l-6,6 l6,6 l1.5,-1.5 l-4.5,-4.5 l4.5,-4.5 z M16,6 l6,6 l-6,6 l-1.5,-1.5 l4.5,-4.5 l-4.5,-4.5 z", () => new ApiPage()),
            ("Ajustes", "M3,5h10v2h-10z M17,5h4v2h-4z M13,3h4v6h-4z M3,11h4v2h-4z M11,11h10v2h-10z M7,9h4v6h-4z M3,17h10v2h-10z M17,17h4v2h-4z M13,15h4v6h-4z", () => new SettingsPage()),
        ];
        Nav.ItemsSource = _pages.Select(p => (Control)new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children =
            {
                new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(p.Icon), Width = 18, Height = 18, Stretch = Stretch.Uniform },
                new TextBlock { Text = p.Name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center },
            },
        }).ToList();
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        AppState.ControllersChanged += RefreshHeader;
        AppState.EditProfileChanged += RefreshHeader;
        _liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => { if (IsVisible) AppState.Tick(true); });
        _slowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => { AppState.Tick(false); RefreshStatus(); });
        _liveTimer.Start(); _slowTimer.Start();
        Nav.SelectedIndex = 0;
        RefreshHeader();
        Closing += (_, e) =>
        {
            // Closing the window keeps the app running in the tray (mappings and effects stay active).
            if (App.IsExiting) return;
            if (!ForceClose && AppState.Runtime.Settings.MinimizeToTray) { e.Cancel = true; Hide(); }
            else App.ExitApp();
        };
    }

    public void Navigate(int index) => Nav.SelectedIndex = index;
    public T? Page<T>() where T : PageBase => _cache.Values.OfType<T>().FirstOrDefault();

    private void NavChanged(object? sender, SelectionChangedEventArgs e)
    {
        int i = Nav.SelectedIndex;
        if (i < 0) return;
        if (!_cache.TryGetValue(i, out var page)) _cache[i] = page = _pages[i].Create();
        PageTitle.Text = page.Title; PageSubtitle.Text = page.Subtitle;
        PageSubtitle.IsVisible = page.Subtitle.Length > 0;
        PageHost.Content = page;
    }

    private void RefreshHeader()
    {
        var rt = AppState.Runtime;
        var sessions = rt.Sessions;
        _updatingPicker = true;
        ControllerPicker.ItemsSource = sessions.Select(s => $"{rt.DisplayName(s)}  ·  {(s.Device.Connection == Core.Protocol.ConnectionType.Usb ? "USB" : "Bluetooth")}").ToList();
        var current = AppState.Session;
        ControllerPicker.SelectedIndex = current == null ? -1 : sessions.ToList().FindIndex(s => s.Id == current.Id);
        _updatingPicker = false;
        SimBadge.IsVisible = current?.Device.IsSimulated == true;
        ProfileBadge.Text = "Perfil: " + AppState.EditProfile.Name + (current?.ProfileReason.StartsWith("game:") == true ? "  (automático)" : "");
    }

    private void ControllerPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPicker) return;
        var sessions = AppState.Runtime.Sessions;
        int i = ControllerPicker.SelectedIndex;
        if (i >= 0 && i < sessions.Count) AppState.SelectController(sessions[i].Id);
    }

    private void RefreshStatus()
    {
        var rt = AppState.Runtime;
        int n = rt.Sessions.Count;
        StatusText.Text = n == 0 ? "Sin mandos" : n == 1 ? "1 mando conectado" : $"{n} mandos conectados";
        var conflicts = rt.Conflicts;
        ConflictBar.IsVisible = conflicts.Count > 0;
        if (conflicts.Count > 0)
            ConflictText.Text = "Detectado: " + string.Join(", ", conflicts) + ". Otros programas que controlan el mando pueden sobrescribir luces, gatillos o vibración y duplicar entradas. " +
                                "Ciérralos o, en Steam, desactiva Steam Input para PlayStation si notas conflictos.";
    }
}
