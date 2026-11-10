using System.Reflection;
using Avalonia.Controls;
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
    private string _update = "", _backup = "";

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
            Ui.Row(8, Ui.Btn("Descargar ViGEmBus", () => FileIo.OpenUrl("https://github.com/nefarius/ViGEmBus/releases/latest")),
                Ui.Btn("Descargar HidHide", () => FileIo.OpenUrl("https://github.com/nefarius/HidHide/releases/latest")),
                Ui.Btn("Mostrar todos los mandos ocultos", () => hid?.UnhideAll())),
            Ui.Text("Ambos controladores son opcionales: solo hacen falta para emular un mando Xbox/DualShock 4. El resto de funciones no los necesita.", "muted")));

        root.Children.Add(Card("Actualizaciones",
            Toggle("Buscar actualizaciones al iniciar (consulta GitHub Releases; nunca instala nada sola)", s.CheckUpdates, v => { s.CheckUpdates = v; rt.SaveSettings(); }),
            Ui.Row(8, Ui.Btn("Buscar ahora", CheckUpdate), Ui.Text(_update, "muted"))));

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

    private void CheckUpdate()
    {
        _update = "Consultando…"; Rebuild();
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        string repo = AppState.Runtime.Settings.UpdateRepository;
        Task.Run(async () =>
        {
            try
            {
                var info = await UpdateChecker.CheckAsync(repo, current);
                _update = info == null ? "No hay versiones publicadas todavía." : info.IsNewer ? $"Versión {info.Version} disponible en {info.Url}" : "Tienes la última versión.";
            }
            catch (Exception ex) { _update = "No se pudo consultar: " + ex.Message; }
            Dispatcher.UIThread.Post(Rebuild);
        });
    }
}
