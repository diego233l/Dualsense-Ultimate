// Renders every page of the real application off-screen (Avalonia headless + Skia) and saves PNGs.
// Used to review the UI in CI and to produce the images in docs/screenshots. The controller shown
// is the built-in simulated one.
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DualSenseUltimate.App;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;

string outDir = args.Length > 0 ? args[0] : "screenshots";
string theme = args.Length > 1 ? args[1] : "Dark";
Directory.CreateDirectory(outDir);
int errors = 0;
DualSenseUltimate.Core.Diagnostics.Log.Written += (level, msg) => { if (level == DualSenseUltimate.Core.Diagnostics.LogLevel.Error) { errors++; Console.Error.WriteLine("ERROR: " + msg); } };
string data = Path.Combine(Path.GetTempPath(), "dsu-shots-" + Guid.NewGuid().ToString("N"));

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();

var runtime = new AppRuntime(new AppPaths(data), new PlatformServices());
runtime.Settings.ApiEnabled = false; runtime.Settings.Theme = theme;
var p = runtime.Profiles.Profiles[0];
p.R2 = DualSenseUltimate.Core.Profiles.TriggerPresets.BuiltIn.First(t => t.Name == "Pistola").Clone();
p.L2 = DualSenseUltimate.Core.Profiles.TriggerPresets.BuiltIn.First(t => t.Name == "Resistencia progresiva").Clone();
p.Mapping.Bindings["Cross"] = new DualSenseUltimate.Core.Mapping.ButtonBinding { Press = [new(DualSenseUltimate.Core.Mapping.ActionType.Key, "Space")], Hold = [new(DualSenseUltimate.Core.Mapping.ActionType.Key, "Ctrl")] };
runtime.Profiles.Save(p);
AppState.Initialize(runtime);
App.ApplyTheme(theme);
runtime.EnableSimulation(true);
var second = new SimulatedController("edge", ConnectionType.Bluetooth, SonyIds.DualSenseEdge, "51:4D:55:4C:41:02", autoReportHz: 60);
second.Input.BatteryPercent = 15;
runtime.Simulator.Plug(second); runtime.Devices.Scan();
Thread.Sleep(600);

var window = new MainWindow { Width = 1320, Height = 900 };
window.Show();
string[] names = ["dashboard", "mandos", "iluminacion", "triggers", "haptica", "remapeo", "joysticks", "perfiles", "comunidad", "automatizaciones", "plugins", "api", "ajustes"];
for (int i = 0; i < names.Length; i++)
{
    window.Navigate(i);
    for (int k = 0; k < 12; k++)
    {
        Dispatcher.UIThread.RunJobs(); AppState.Tick(false); AppState.Tick(true);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(40);
    }
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    string file = Path.Combine(outDir, $"{i + 1:00}-{names[i]}{(theme == "Light" ? "-claro" : "")}.png");
    frame?.Save(file);
    Console.WriteLine(file);
}
AppState.Shutdown();
try { Directory.Delete(data, true); } catch { }
return errors == 0 ? 0 : 1;
