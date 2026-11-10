// Guided validation on a PHYSICAL controller. It talks to the hardware through exactly the same code
// as the application and asks the tester to confirm what they see and feel. The answers are written
// to a Markdown report that can be attached to TESTING.md / a GitHub issue.
using System.Text;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Platform;

var hid = new HidSharpEnumerator();
var found = hid.Enumerate().Where(d => d.Model != null).ToList();
if (found.Count == 0)
{
    Console.WriteLine("No se ha encontrado ningún DualSense, DualSense Edge o DualShock 4. Conéctalo por USB o Bluetooth y vuelve a ejecutar.");
    return 2;
}
var report = new StringBuilder();
report.AppendLine($"# Informe de validación en hardware real\n\nFecha: {DateTime.Now:yyyy-MM-dd HH:mm} · SO: {Environment.OSVersion}\n");
int failures = 0;

foreach (var d in found)
{
    ControllerDevice dev;
    try { dev = new ControllerDevice(d, hid.Open(d)); }
    catch (Exception ex) { Console.WriteLine($"No se pudo abrir {d.Path}: {ex.Message}"); failures++; continue; }
    dev.Start();
    Thread.Sleep(800);
    Console.WriteLine($"\n=== {dev.Model} [{dev.Id}] por {dev.Connection} · firmware {dev.FirmwareVersion ?? "?"} ===");
    report.AppendLine($"## {dev.Model} · {dev.Connection} · firmware {dev.FirmwareVersion ?? "desconocido"}\n\n| Prueba | Resultado |\n|---|---|");

    void Row(string name, bool ok, string detail = "") { if (!ok) failures++; report.AppendLine($"| {name} | {(ok ? "OK" : "FALLO")} {detail} |"); Console.WriteLine($"  [{(ok ? "OK" : "FALLO")}] {name} {detail}"); }
    bool Ask(string question)
    {
        Console.Write($"  ¿{question}? [s/n] ");
        string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        return a.StartsWith('s') || a.StartsWith('y');
    }

    var st = dev.State;
    Row("Lectura de informes de entrada", dev.ReportCount > 50, $"({dev.ReportCount} informes, {dev.ReportRate:0} Hz, {dev.ErrorCount} errores)");
    Row("Identificador (MAC) leído", dev.Id.Contains(':'), dev.Id);
    Row("Batería", st.BatteryStatus != BatteryStatus.Unknown, $"{st.BatteryPercent} % {st.BatteryStatus}");
    Row("Calibración IMU", !dev.IsDualSense || dev.Calibration.Valid);

    var o = new OutputState { Red = 255, PlayerLeds = 0b11111 };
    dev.SetOutput(o); Row("Barra de luz en ROJO y 5 LEDs de jugador encendidos", Ask("La barra está roja y los 5 LEDs encendidos"));
    o = new OutputState { Green = 255, PlayerLeds = 0b00100, MicLed = MicLedMode.On };
    dev.SetOutput(o); Row("Barra en VERDE, LED central y LED de micrófono", Ask("La barra está verde, solo el LED central y el LED del micrófono encendidos"));
    o = new OutputState { Blue = 255, MotorLeft = 200 };
    dev.SetOutput(o); Row("Vibración motor izquierdo (grave)", Ask("Vibra el lado izquierdo (grave)"));
    o = new OutputState { Blue = 255, MotorRight = 200 };
    dev.SetOutput(o); Row("Vibración motor derecho (agudo)", Ask("Vibra el lado derecho (agudo)"));
    if (dev.IsDualSense)
    {
        (string, TriggerEffect)[] effects =
        [
            ("Resistencia constante en R2", new TriggerEffect { Type = TriggerEffectType.Feedback, Start = 0, Strength = 8 }),
            ("Arma (punto de disparo) en R2", new TriggerEffect { Type = TriggerEffectType.Weapon, Start = 2, End = 5, Strength = 8 }),
            ("Vibración en R2", new TriggerEffect { Type = TriggerEffectType.Vibration, Start = 2, Strength = 8, Frequency = 20 }),
            ("Arco en R2", new TriggerEffect { Type = TriggerEffectType.Bow, Start = 1, End = 6, Strength = 6, Strength2 = 8 }),
            ("Galope en R2", new TriggerEffect { Type = TriggerEffectType.Galloping, Start = 0, End = 9, FirstFoot = 1, SecondFoot = 5, Frequency = 4 }),
            ("Máquina en R2", new TriggerEffect { Type = TriggerEffectType.Machine, Start = 2, End = 9, Strength = 7, Strength2 = 3, Frequency = 14, Period = 2 }),
        ];
        foreach (var (name, e) in effects)
        {
            dev.SetOutput(new OutputState { Blue = 255, RightTrigger = TriggerEffectEncoder.Encode(e) });
            Row(name, Ask($"Aprieta R2: notas el efecto «{name}» (y L2 queda libre)"));
        }
        dev.SetOutput(new OutputState { Blue = 255, LeftTrigger = TriggerEffectEncoder.Encode(new TriggerEffect { Type = TriggerEffectType.Feedback, Strength = 8 }) });
        Row("Resistencia constante en L2", Ask("Aprieta L2: está duro (y R2 libre)"));
    }
    dev.SetOutput(new OutputState());
    Console.WriteLine("  Pulsa CRUZ, mueve el joystick izquierdo a tope a la derecha y toca el touchpad (tienes 8 s)...");
    bool cross = false, stick = false, touch = false;
    for (int i = 0; i < 160; i++)
    {
        var s = dev.State; cross |= s.IsDown(PadButtons.Cross); stick |= s.LeftX > 230; touch |= s.Touch0.Active;
        Thread.Sleep(50);
    }
    Row("Botón Cruz", cross); Row("Joystick izquierdo", stick); Row("Touchpad", touch);
    dev.Dispose();
    Thread.Sleep(300);
    Row("Liberación al cerrar (efectos detenidos, LEDs devueltos al firmware)", Ask("Se han detenido vibración/gatillos y la barra ha vuelto a su estado normal"));
    report.AppendLine();
}

string file = Path.Combine(Environment.CurrentDirectory, $"hardware-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");
File.WriteAllText(file, report.ToString());
Console.WriteLine($"\nInforme guardado en {file}. Fallos: {failures}");
return failures == 0 ? 0 : 1;
