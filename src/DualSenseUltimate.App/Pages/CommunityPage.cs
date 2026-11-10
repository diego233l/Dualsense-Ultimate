using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Community;
using DualSenseUltimate.Core.Profiles;

namespace DualSenseUltimate.App.Pages;

public sealed class CommunityPage : PageBase
{
    public override string Title => "Biblioteca comunitaria";
    public override string Subtitle => "Perfiles, efectos de gatillo, patrones hápticos, iluminación, macros y giroscopio compartidos";
    protected override bool RebuildOnControllers => false;
    private readonly CommunityClient _client = new();
    private CommunityIndex? _index;
    private string _status = "Pulsa «Actualizar catálogo» para descargar la lista (requiere Internet).";
    private string _filter = "Todo";
    private bool _busy;

    private static string KindName(string k) => k switch
    {
        PackageKinds.Profile => "Perfil", PackageKinds.Trigger => "Gatillos", PackageKinds.Lighting => "Iluminación", PackageKinds.Haptic => "Háptica", PackageKinds.Macro => "Macro", _ => "Giroscopio",
    };

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var root = new StackPanel();
        var url = new TextBox { Text = rt.Settings.CommunityIndexUrl, Width = 560 };
        url.LostFocus += (_, _) => { rt.Settings.CommunityIndexUrl = url.Text?.Trim() ?? ""; rt.SaveSettings(); };
        string[] filters = ["Todo", .. PackageKinds.All.Select(KindName)];
        root.Children.Add(Card("Catálogo",
            Labeled("Dirección del catálogo (índice JSON por HTTPS)", url), new Border { Height = 8 },
            Ui.Row(10, Ui.Btn(_busy ? "Descargando…" : "Actualizar catálogo", Refresh, true), Ui.Combo(filters, _filter, v => { _filter = v; Rebuild(); })),
            Ui.Text(_status, "muted")));

        if (_index != null)
        {
            var list = new WrapPanel();
            foreach (var entry in _index.Entries.Where(e => _filter == "Todo" || KindName(e.Kind) == _filter))
            {
                var e = entry;
                var pill = new Border { Child = new TextBlock { Text = KindName(e.Kind), FontSize = 11 }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                pill.Classes.Add("pill");
                var card = Card(null, pill, new TextBlock { Text = e.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) },
                    Ui.Text((e.Game.Length > 0 ? e.Game + " · " : "") + "por " + (e.Author.Length > 0 ? e.Author : "anónimo") + " · v" + e.Version, "muted"),
                    Ui.Text(e.Description, "muted"), new Border { Height = 8 }, Ui.Btn("Descargar e instalar", () => Install(e)));
                card.Width = 320;
                list.Children.Add(card);
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Text("No hay elementos en esta categoría.", "muted"));
            root.Children.Add(list);
        }

        root.Children.Add(Card("Publicar y valorar",
            Ui.Text("La biblioteca es un repositorio Git público: cada elemento es un archivo JSON validado y el catálogo es un índice estático. No hay servidor propio, cuentas ni seguimiento.", "muted"),
            Ui.Text("• Publicar o actualizar: exporta tu perfil o efecto desde su sección y envíalo como pull request a la carpeta «community/» del repositorio, añadiendo su entrada al índice.", "muted"),
            Ui.Text("• Valorar: las valoraciones se hacen con reacciones y comentarios en GitHub. Un sistema de puntuación dentro de la app requeriría un servicio en línea con cuentas, que este proyecto no opera.", "muted"),
            Ui.Text("• Seguridad: todo lo descargado pasa por el mismo validador que las importaciones locales; los paquetes solo contienen datos, nunca código.", "muted"),
            new Border { Height = 6 },
            Ui.Btn("Abrir el repositorio de la comunidad", () => FileIo.OpenUrl("https://github.com/" + rt.Settings.UpdateRepository + "/tree/main/community"))));
        return root;
    }

    private void Refresh()
    {
        if (_busy) return;
        _busy = true; _status = "Descargando catálogo…"; Rebuild();
        string url = AppState.Runtime.Settings.CommunityIndexUrl;
        Task.Run(async () =>
        {
            try { _index = await _client.FetchIndexAsync(url); _status = $"{_index.Entries.Count} elementos disponibles."; }
            catch (Exception ex) { _status = "No se pudo descargar el catálogo: " + ex.Message; }
            _busy = false;
            Dispatcher.UIThread.Post(Rebuild);
        });
    }

    private void Install(CommunityEntry e)
    {
        string url = AppState.Runtime.Settings.CommunityIndexUrl;
        _status = $"Descargando «{e.Name}»…"; Rebuild();
        Task.Run(async () =>
        {
            try
            {
                string json = await _client.DownloadAsync(url, e);
                var (r, summary) = AppState.Runtime.Profiles.Import(json);
                _status = r.Ok ? summary + "." : "Paquete rechazado: " + string.Join(" ", r.Errors);
            }
            catch (Exception ex) { _status = "Error al descargar: " + ex.Message; }
            Dispatcher.UIThread.Post(Rebuild);
        });
    }
}
