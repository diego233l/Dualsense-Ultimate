using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Profiles;

namespace DualSenseUltimate.App.Pages;

public sealed class ProfilesPage : PageBase
{
    public override string Title => "Perfiles";
    public override string Subtitle => "Perfiles globales, por mando y automáticos por videojuego";
    private string _status = "";

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var root = new StackPanel();
        var session = AppState.Session;
        var editing = AppState.EditProfile;

        root.Children.Add(Card("Cómo se elige el perfil",
            Ui.Text("1) Si se está ejecutando un juego asociado a un perfil, se aplica ese perfil automáticamente y al cerrarlo se restaura el anterior.  " +
                    "2) Si no, el perfil asignado manualmente a cada mando.  3) Si no hay ninguno, el perfil predeterminado (global).", "muted"),
            new Border { Height = 8 },
            Ui.Row(8,
                Ui.Btn("+ Nuevo perfil", () =>
                {
                    var p = new Profile { Name = "Perfil " + (rt.Profiles.Profiles.Count + 1) };
                    rt.Profiles.Save(p); Edit(p);
                }, true),
                Ui.Btn("Importar…", async () =>
                {
                    var json = await FileIo.OpenTextAsync(this);
                    if (json == null) return;
                    var (r, summary) = rt.Profiles.Import(json);
                    _status = r.Ok ? summary + (r.Warnings.Count > 0 ? $" ({r.Warnings.Count} elementos no válidos descartados)" : "") : string.Join(" ", r.Errors);
                    Rebuild();
                })),
            Ui.Text(_status, "muted")));

        var list = new WrapPanel();
        foreach (var prof in rt.Profiles.Profiles.OrderBy(x => x.Name))
        {
            var p = prof;
            bool isEditing = p.Id == editing.Id, isDefault = p.Id == (rt.Settings.DefaultProfileId ?? rt.Profiles.Profiles[0].Id);
            var tags = new WrapPanel();
            void Tag(string text, string brush) { var b = new Border { Child = new TextBlock { Text = text, FontSize = 11 }, Margin = new Avalonia.Thickness(0, 0, 6, 4), Background = Ui.Brush(this, brush) }; b.Classes.Add("pill"); tags.Children.Add(b); }
            if (isDefault) Tag("Predeterminado", "CardBg2");
            if (p.Processes.Count > 0) Tag("Juego: " + string.Join(", ", p.Processes.Take(2)) + (p.Processes.Count > 2 ? "…" : ""), "CardBg2");
            if (p.ControllerId != null) Tag("Solo un mando", "CardBg2");
            Tag(p.Emulation.Mode == Core.Virtual.EmulationMode.None ? "DualSense nativo" : p.Emulation.Mode.ToString(), "CardBg2");
            var card = Card(null,
                new TextBlock { Text = p.Name, FontSize = 16, FontWeight = FontWeight.SemiBold },
                Ui.Text(p.Description.Length > 0 ? p.Description : "Sin descripción", "muted"), new Border { Height = 6 }, tags,
                Ui.Text($"L2: {DashboardPage.TriggerName(p.L2)} · R2: {DashboardPage.TriggerName(p.R2)} · {p.Mapping.Bindings.Count} asignaciones · {p.Mapping.Macros.Count} macros", "muted"),
                new Border { Height = 8 },
                Wrap(Ui.Btn(session != null ? "Usar y editar" : "Editar", () => Edit(p), !isEditing), new Border { Width = 6 },
                    Ui.Btn("Duplicar", () => { var c = p.Clone(); c.Id = Guid.NewGuid().ToString("N"); c.Name = p.Name + " (copia)"; c.Processes.Clear(); rt.Profiles.Save(c); Rebuild(); }), new Border { Width = 6 },
                    Ui.Btn("Exportar…", async () => { AppState.FlushSave(); if (await FileIo.SaveTextAsync(this, p.Name, rt.Profiles.Export(p))) { _status = $"«{p.Name}» exportado."; Rebuild(); } }), new Border { Width = 6 },
                    Ui.Btn("Eliminar", () => { if (rt.Profiles.Delete(p.Id)) { rt.ResolveAllProfiles(); AppState.CheckProfileSwitch(); } else _status = "Debe existir al menos un perfil."; Rebuild(); })));
            card.Width = 400;
            if (isEditing) card.BorderBrush = Ui.Brush(this, "Accent");
            list.Children.Add(card);
        }
        root.Children.Add(list);

        // ---- details of the profile being edited
        var e = editing;
        var name = new TextBox { Text = e.Name, Width = 280 };
        name.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text) && name.Text != e.Name) { e.Name = name.Text.Trim(); AppState.Tick(false); AppState.FlushSave(); AppState.SelectController(AppState.SelectedControllerId); Rebuild(); } };
        var desc = new TextBox { Text = e.Description, Width = 420, Watermark = "Descripción" };
        desc.LostFocus += (_, _) => e.Description = desc.Text ?? "";
        var author = new TextBox { Text = e.Author, Width = 200, Watermark = "Autor" };
        author.LostFocus += (_, _) => e.Author = author.Text ?? "";
        var procs = new TextBox { Text = string.Join(", ", e.Processes), Width = 420, Watermark = "eldenring.exe, Cyberpunk2077.exe" };
        procs.LostFocus += (_, _) => e.Processes = (procs.Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var sessions = rt.Sessions;
        var scope = new ComboBox { Width = 280, ItemsSource = new[] { "Cualquier mando" }.Concat(sessions.Select(s => "Solo " + rt.DisplayName(s) + " · " + s.Id)).ToList(),
            SelectedIndex = e.ControllerId == null ? 0 : sessions.ToList().FindIndex(s => s.Id == e.ControllerId) + 1 };
        scope.SelectionChanged += (_, _) => e.ControllerId = scope.SelectedIndex <= 0 ? null : sessions[scope.SelectedIndex - 1].Id;
        var running = Ui.Text("", "muted");
        root.Children.Add(Card("Perfil en edición: " + e.Name,
            Wrap(Labeled("Nombre", name), new Border { Width = 14 }, Labeled("Autor", author), new Border { Width = 14 }, Labeled("Descripción", desc)),
            Wrap(Labeled("Ejecutables que activan este perfil (separados por comas)", procs), new Border { Width = 14 }, Labeled("Ámbito", scope)),
            new Border { Height = 8 },
            Ui.Row(8, Ui.Btn("Establecer como predeterminado", () => { rt.Settings.DefaultProfileId = e.Id; rt.SaveSettings(); rt.ResolveAllProfiles(); Rebuild(); }),
                Ui.Btn("Ver procesos en ejecución", () => running.Text = "En ejecución: " + string.Join(", ", rt.Platform.ProcessSource.RunningProcessNames().OrderBy(x => x).Take(150)))),
            running,
            Ui.Text("Cada perfil guarda: iluminación, gatillos adaptativos, háptica, remapeo, macros, joysticks, giroscopio, touchpad y emulación. " +
                    "Edita esas secciones con este perfil activo; los cambios se aplican al instante y se guardan solos.", "muted")));
        return root;
    }

    private void Edit(Profile p)
    {
        AppState.FlushSave();
        AppState.OfflineProfileId = p.Id;
        if (AppState.Session is { } s) AppState.Runtime.AssignProfile(s.Id, p.Id);
        AppState.CheckProfileSwitch();
        Rebuild();
    }
}
