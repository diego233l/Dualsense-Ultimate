using System.Reflection;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Community;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.App.Pages;

public sealed class SettingsPage : PageBase
{
    public override string Title => "Ajustes";
    public override string Subtitle => "Apariencia, inicio, controladores, copias de seguridad y diagnóstico";
    protected override bool RebuildOnControllers => false;
    private string _backup = "";

    public SettingsPage() => AppState.UpdateChanged += () => { if (IsLoaded) Rebuild(); };

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var s = rt.Settings;
        var root = new StackPanel();

        var general = new StackPanel();
        general.Children.Add(Labeled("Tema", Ui.Combo(["Oscuro", "Claro"], s.Theme == "Light" ? "Claro" : "Oscuro", v => { s.Theme = v == "Claro" ? "Light" : "Dark"; rt.SaveSettings(); App.ApplyTheme(s.Theme); })));
        if (OperatingSystem.IsWindows())
        {
            bool auto = false;
            try { auto = Platform.WindowsAutoStart.IsEnabled(); } catch { }
            general.Children.Add(Toggle("Iniciar con Windows", auto, v => { try { if (OperatingSystem.IsWindows()) Platform.WindowsAutoStart.Set(v); } catch (Exception ex) { Core.Diagnostics.Log.Error("autostart", ex); } }));
        }
        general.Children.Add(Toggle("Iniciar minimizado en la bandeja", s.StartMinimized, v => { s.StartMinimized = v; rt.SaveSettings(); }));
        general.Children.Add(Toggle("Al cerrar la ventana, seguir en la bandeja del sistema", s.MinimizeToTray, v => { s.MinimizeToTray = v; rt.SaveSettings(); }));
        general.Children.Add(new Border { Height = 6 });
        general.Children.Add(Ui.Btn("Salir de DualSense Ultimate", App.ExitApp));
        root.Children.Add(Card("General", general));

        var hid = rt.Platform.Hider; var pad = rt.Platform.PadFactory;
        root.Children.Add(Card("Controladores y compatibilidad",
            Ui.Text("• Acceso HID: " + "sin controladores adicionales; no requiere permisos de administrador.", "muted"),
            Ui.Text("• ViGEmBus (mandos virtuales): " + (pad == null ? "no disponible en esta plataforma" : pad.IsAvailable ? "instalado" : pad.Status), "muted"),
            Ui.Text("• HidHide (ocultar el mando físico): " + (hid?.Status ?? "no disponible en esta plataforma"), "muted"),
            Ui.Text("• Audio háptico USB / captura de audio: " + (rt.Platform.HapticAudio == null ? "no disponible en esta plataforma" : "disponible (WASAPI)"), "muted"),
            new Border { Height = 6 },
            Toggle("Prioridad de salida: reenviar luces, gatillos y vibración continuamente", s.AssertOutput, v => { s.AssertOutput = v; rt.SaveSettings(); rt.ApplyOutputPriority(); }),
            Ui.Text("Actívalo si el color, el LED del micrófono o los gatillos solo se aplican un instante: significa que otro programa (Steam, DSX, DS4Windows, software RGB…) está escribiendo en el mando. " +
                    "Con esta opción la app reescribe su estado unas 30 veces por segundo y prevalece, aunque lo correcto es cerrar el otro programa.", "muted"),
            new Border { Height = 6 },
            Ui.Row(8, Ui.Btn("Descargar ViGEmBus", () => FileIo.OpenUrl("https://github.com/nefarius/ViGEmBus/releases/latest")),
                Ui.Btn("Descargar HidHide", () => FileIo.OpenUrl("https://github.com/nefarius/HidHide/releases/latest")),
                Ui.Btn("Mostrar todos los mandos ocultos", () => hid?.UnhideAll())),
            Ui.Text("Ambos controladores son opcionales: ViGEmBus solo hace falta para emular un mando Xbox/DualShock 4, y HidHide para ocultar el mando físico (emulación y aislamiento).", "muted")));

        var allowed = new StackPanel { Spacing = 4 };
        foreach (var app in s.AllowedApplications.ToList())
        {
            var a = app;
            allowed.Children.Add(Ui.Row(8, Ui.Btn("✕", () => { rt.DisallowApplication(a); Rebuild(); }), Ui.Text(a, "muted")));
        }
        if (s.AllowedApplications.Count == 0) allowed.Children.Add(Ui.Text("Ningún programa adicional permitido.", "muted"));
        var isoState = Ui.Text("", "muted");
        Live(() =>
        {
            var real = rt.Sessions.Where(x => !x.Device.IsSimulated).ToList();
            isoState.Text = !s.IsolateControllers ? "Estado: desactivado."
                : hid is not { IsAvailable: true } ? "Estado: NO aplicado. Instala HidHide (botón de arriba) y vuelve a activar el aislamiento."
                : real.Count == 0 ? "Estado: activado; se aplicará al conectar un mando."
                : $"Estado: {real.Count(x => x.IsHidden)} de {real.Count} mandos aislados.";
        });
        root.Children.Add(Card("Aislamiento del mando",
            Ui.Text("Oculta el mando físico a todos los programas salvo DualSense Ultimate y los que permitas (tus juegos). Así Steam, DSX, DS4Windows o software RGB no pueden cambiar luces, gatillos ni vibración. " +
                    "Windows no ofrece esta protección por sí mismo: se consigue con el controlador HidHide, que debe estar instalado.", "muted"),
            Toggle("Aislar los mandos", s.IsolateControllers, v => { s.IsolateControllers = v; rt.SaveSettings(); Task.Run(rt.ApplyIsolation); }),
            isoState,
            Ui.Text("Programas permitidos (además de esta aplicación)", "label"), allowed,
            Ui.Row(8, Ui.Btn("Añadir programa…", async () =>
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null) return;
                var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Elige el ejecutable del juego", AllowMultiple = false,
                    FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Programas") { Patterns = ["*.exe"] }],
                });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { rt.AllowApplication(path); Rebuild(); }
            })),
            Ui.Text("• Los juegos asociados a un perfil sin emulación se añaden solos la primera vez que se detectan (si el juego no ve el mando esa primera vez, reinícialo).\n" +
                    "• Con emulación (mando Xbox/DS4 virtual) el juego no necesita permiso: usa el mando virtual.\n" +
                    "• Un programa que ya tenía el mando abierto lo conserva hasta que lo cierres o reconectes el mando: tras activar el aislamiento, cierra Steam/DSX o desconecta y vuelve a conectar el mando.\n" +
                    "• Al salir de la aplicación o desinstalarla, el mando vuelve a ser visible para todos.", "muted")));

        var upd = AppState.Update;
        var updStatus = Ui.Text(AppState.UpdateStatus, "muted");
        var updBar = new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Max(0, AppState.UpdateProgress), Height = 6, Width = 320, IsVisible = AppState.UpdateProgress >= 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var updButtons = Ui.Row(8, Ui.Btn("Buscar ahora", () => _ = AppState.CheckUpdateAsync(true)));
        if (upd != null)
        {
            if (upd.InstallerUrl != null && AppState.IsInstalled && OperatingSystem.IsWindows())
                updButtons.Children.Add(Ui.Btn($"Descargar e instalar {upd.Version}", () => _ = AppState.InstallUpdateAsync(), true));
            updButtons.Children.Add(Ui.Btn("Abrir página de descarga", () => FileIo.OpenUrl(upd.Url)));
            updButtons.Children.Add(Ui.Btn("Omitir esta versión", AppState.SkipUpdate));
        }
        var updCard = Card("Actualizaciones",
            Ui.Text($"Versión instalada: {AppState.CurrentVersion.ToString(3)}" + (AppState.IsInstalled ? "" : " (portable)"), "muted"),
            Toggle("Buscar actualizaciones al iniciar y periódicamente", s.CheckUpdates, v => { s.CheckUpdates = v; rt.SaveSettings(); }),
            updButtons, updBar, updStatus);
        if (upd != null)
        {
            var sp = (StackPanel)updCard.Child!;
            if (upd.Notes.Length > 0)
                sp.Children.Add(new Border
                {
                    Background = Ui.Brush(this, "CardBg2"), CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(12), Margin = new Avalonia.Thickness(0, 8, 0, 0),
                    Child = new ScrollViewer { MaxHeight = 180, Content = new SelectableTextBlock { Text = "Novedades de la versión " + upd.Version + "\n\n" + upd.Notes, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5 } },
                });
            if (!AppState.IsInstalled) sp.Children.Add(Ui.Text("Esta es la versión portable: descarga la nueva versión desde la página y sustituye la carpeta.", "muted"));
            else if (upd.InstallerUrl == null) sp.Children.Add(Ui.Text("Esta versión no publica un instalador verificable; usa la página de descarga.", "muted"));
        }
        ((StackPanel)updCard.Child!).Children.Add(Ui.Text("La app solo consulta la lista de versiones de GitHub. Nada se descarga ni instala sin que lo pidas. La descarga se limita al repositorio oficial por HTTPS y se comprueba su tamaño y su huella SHA-256 antes de ejecutarla; al instalar, la app se cierra, se actualiza conservando tus perfiles y vuelve a abrirse.", "muted"));
        root.Children.Add(updCard);

        var backups = ConfigBackup.List(rt.Paths);
        var backupBox = new ComboBox { ItemsSource = backups.Select(Path.GetFileNameWithoutExtension).ToList(), PlaceholderText = backups.Count == 0 ? "Sin copias" : "Elegir copia…", Width = 260 };
        root.Children.Add(Card("Copias de seguridad y recuperación",
            Ui.Text("Se crea una copia automática en cada inicio (se conservan las 10 últimas). Cada archivo de configuración guarda además su versión anterior y se recupera solo si se corrompe.", "muted"),
            new Border { Height = 6 },
            Ui.Row(8, Ui.Btn("Crear copia ahora", () => { AppState.FlushSave(); _backup = ConfigBackup.Create(rt.Paths) != null ? "Copia creada." : "No se pudo crear la copia."; Rebuild(); }), backupBox,
                Ui.Btn("Restaurar", () =>
                {
                    if (backupBox.SelectedIndex < 0) return;
                    AppState.FlushSave();
                    bool ok = ConfigBackup.Restore(rt.Paths, backups[backupBox.SelectedIndex]);
                    if (ok) { rt.ReloadAfterRestore(); AppState.CheckProfileSwitch(); App.ApplyTheme(rt.Settings.Theme); }
                    _backup = ok ? "Configuración restaurada." : "No se pudo restaurar."; Rebuild();
                })),
            Ui.Text(_backup, "muted")));

        root.Children.Add(Card("Diagnóstico",
            Toggle("Registro detallado", s.VerboseLogging, v => { s.VerboseLogging = v; rt.SaveSettings(); Core.Diagnostics.Log.MinLevel = v ? Core.Diagnostics.LogLevel.Debug : Core.Diagnostics.LogLevel.Info; }),
            Ui.Row(8, Ui.Btn("Abrir carpeta de registros", () => FileIo.OpenFolder(rt.Paths.Logs)), Ui.Btn("Abrir carpeta de datos", () => FileIo.OpenFolder(rt.Paths.Root))),
            Ui.Text(rt.Paths.Root, "muted")));

        root.Children.Add(Card("Acerca de",
            Ui.Text("DualSense Ultimate " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? ""), "h2"),
            Ui.Text("Proyecto independiente de código abierto (licencia MIT). No está afiliado a Sony Interactive Entertainment. «PlayStation», «DualSense» y «DualShock» son marcas de Sony.", "muted"),
            Ui.Text("Componentes: Avalonia UI (MIT), HidSharp (Apache-2.0), Nefarius.ViGEm.Client (MIT), NAudio (MIT), .NET (MIT). Protocolo documentado por la comunidad (hid-playstation de Linux, dualsensectl, DS4Windows).", "muted")));
        return root;
    }
}
