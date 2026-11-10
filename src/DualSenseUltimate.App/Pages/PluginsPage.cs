using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;

namespace DualSenseUltimate.App.Pages;

public sealed class PluginsPage : PageBase
{
    public override string Title => "Plugins";
    public override string Subtitle => "Extensiones e integraciones sin modificar el núcleo";
    protected override bool RebuildOnControllers => false;

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        rt.Plugins.Discover();
        var root = new StackPanel();
        root.Children.Add(Notice("Los plugins son código .NET que se ejecuta dentro de la aplicación con tus permisos de usuario. .NET no ofrece un aislamiento de seguridad real dentro del proceso: " +
                                 "los permisos declarados limitan lo que el plugin puede pedirle a DualSense Ultimate, no lo que su código puede hacer en tu equipo. Activa solo plugins en los que confíes. " +
                                 "Para integraciones sin ese riesgo, usa la API local desde un programa aparte."));
        var entries = rt.Plugins.Entries;
        if (entries.Count == 0) root.Children.Add(Card("No hay plugins instalados", Ui.Text("Copia la carpeta de un plugin (con su archivo plugin.json) en la carpeta de plugins y pulsa «Volver a buscar».", "muted")));
        foreach (var entry in entries)
        {
            var e = entry;
            var state = Ui.Text(e.Loaded ? "En ejecución" : e.Error != null ? "Error: " + e.Error : "Detenido", "muted");
            var toggle = Toggle("Activado", rt.Plugins.IsEnabled(e), v => { rt.Plugins.SetEnabled(e, v); Rebuild(); });
            root.Children.Add(Card(null,
                new TextBlock { Text = $"{e.Manifest.Name}  v{e.Manifest.Version}", FontSize = 16, FontWeight = FontWeight.SemiBold },
                Ui.Text($"{e.Manifest.Id} · {(e.Manifest.Author.Length > 0 ? e.Manifest.Author : "autor desconocido")}", "muted"),
                Ui.Text(e.Manifest.Description, "muted"),
                Ui.Text("Permisos que solicita: " + (e.Manifest.Permissions.Count == 0 ? "ninguno" : string.Join(", ", e.Manifest.Permissions.Select(PermissionName))), "muted"),
                new Border { Height = 4 }, toggle, state));
        }
        root.Children.Add(Card("Carpeta de plugins",
            Ui.Text(rt.Paths.Plugins, "muted"), new Border { Height = 6 },
            Ui.Row(8, Ui.Btn("Abrir carpeta", () => FileIo.OpenFolder(rt.Paths.Plugins)), Ui.Btn("Volver a buscar", Rebuild)),
            Ui.Text("Ejemplos incluidos en el código fuente: integración con Home Assistant (webhook) y registro de batería. Consulta docs/PLUGINS.md para crear el tuyo.", "muted")));
        return root;
    }

    private static string PermissionName(DualSenseUltimate.Plugins.PluginPermissions p) => p switch
    {
        DualSenseUltimate.Plugins.PluginPermissions.ReadState => "leer estado de los mandos",
        DualSenseUltimate.Plugins.PluginPermissions.ControlOutput => "controlar luces, vibración y gatillos",
        DualSenseUltimate.Plugins.PluginPermissions.Profiles => "cambiar perfiles",
        DualSenseUltimate.Plugins.PluginPermissions.Events => "recibir eventos y enviar notificaciones",
        _ => p.ToString(),
    };
}
