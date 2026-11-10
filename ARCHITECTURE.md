# Arquitectura

## Decisiones tecnológicas

| Decisión | Elección | Motivo |
|---|---|---|
| Lenguaje / runtime | C# 14, .NET 10 (LTS) | Requisito; build autocontenida (el usuario no instala .NET) |
| Interfaz | **Avalonia UI 11** | Aplicación de escritorio Windows nativa (.NET, renderizado por GPU con Skia, DPI por monitor). Frente a WPF/WinUI 3 permite **ejecutar y verificar la interfaz real en CI sin pantalla** (render headless), que es lo que ha hecho posible revisar las 13 secciones sin un escritorio Windows. No cambia nada para el usuario final: es un `.exe` de Windows. |
| HID | HidSharp (Apache-2.0) | Abre los dispositivos en modo compartido; evita reimplementar SetupAPI/hid.dll |
| Mandos virtuales | ViGEmBus + Nefarius.ViGEm.Client | Único bus virtual firmado y extendido (ver docs/RESEARCH.md §8) |
| Ocultación | HidHide mediante `HidHideCLI.exe` | Interfaz estable y soportada; sin IOCTL propios |
| Audio | NAudio (WASAPI) | Captura loopback y salida de 4 canales |
| API local | Kestrel (ASP.NET Core minimal API) | Solo `127.0.0.1`, token obligatorio |
| Persistencia | **JSON** (un archivo por perfil) en vez de SQLite | Los datos son pocos y jerárquicos; JSON es legible, exportable sin conversión y permite escritura atómica + copia `.bak`. SQLite añadiría una dependencia nativa sin beneficio. |
| Instalador | Inno Setup 6 | Instalación por usuario sin administrador, descarga opcional de drivers |

## Proyectos

```
src/
  DualSenseUltimate.Core                 Toda la lógica, sin dependencias de SO (probada en CI)
    Protocol/     Informes HID DualSense/Edge/DS4, CRC32, efectos de gatillo, calibración IMU
    Devices/      IHidTransport, ControllerDevice (hilos de lectura/escritura), DeviceManager, simulador
    Lighting/     LightingEngine (función pura config+tiempo → color)
    Haptics/      Patrones, mezclador de vibración, DSP de audio háptico
    Mapping/      MappingEngine (botones, capas, macros, touchpad, giroscopio), StickProcessor
    Virtual/      Interfaces IVirtualPad / IInputInjector / IDeviceHider, OutputApplier
    Profiles/     Profile, ProfileStore, ProfileValidator, formato de paquete compartible
    Automation/   Motor de reglas
    Plugins/      PluginManager + PluginHost (permisos)
    Community/    Cliente del catálogo y comprobador de actualizaciones
    Runtime/      AppRuntime (raíz de composición), ControllerSession, EventBus, ProcessWatcher
    Storage/      Rutas, JSON atómico con recuperación, copias de seguridad
  DualSenseUltimate.Plugin.Abstractions  Contrato público para plugins (IDsuPlugin, IDsuHost)
  DualSenseUltimate.Platform             Implementaciones Windows: HidSharp, SendInput, ViGEm, HidHide, WASAPI
  DualSenseUltimate.Api                  API REST local
  DualSenseUltimate.App                  Interfaz (Avalonia): 13 páginas y controles dibujados a medida
tests/DualSenseUltimate.Tests            112 pruebas (unitarias + integración extremo a extremo)
tools/Screenshots                        Renderiza la UI real sin pantalla (prueba de humo + capturas)
tools/HardwareCheck                      Validación guiada con hardware físico
samples/plugins                          Plugins de ejemplo
```

## Flujo de datos

```
 HID (USB/BT) ──► ControllerDevice ──► InputState ──► ControllerSession
   ▲  hilo de lectura                                   │
   │                                                    ├─► MappingEngine ─► OutputFrame ─► OutputApplier (teclado/ratón)
   │                                                    │                               └─► IVirtualPad (ViGEm) ─► rumble ─┐
   │  hilo de escritura (coalescente)                   │                                                                   │
   └── OutputState ◄── Tick 60 Hz: LightingEngine + TriggerEffectEncoder + RumbleMixer ◄────────────────────────────────────┘
```

- **Un único dueño del informe de salida.** `ControllerSession.Tick` compone cada 16 ms *un*
  `OutputState` a partir de iluminación, gatillos y un mezclador de vibración con fuentes con nombre
  (patrón, audio, mando virtual, API). Ningún módulo escribe en el dispositivo por su cuenta, así que
  no pueden pisarse. `ControllerDevice` solo escribe cuando el estado cambia (más un refresco cada 2 s).
- **Propiedad parcial (modo híbrido).** `OutputOwnership` decide qué *valid flags* se activan en el
  informe. Lo que la aplicación no «posee» no se escribe y el juego conserva su control.
- **Motor de remapeo determinista.** `MappingEngine.Process(estado, dt)` no usa relojes ni hilos: el
  tiempo es un parámetro, por lo que turbo, pulsación larga, doble pulsación y macros se prueban al
  milisegundo.
- **Eventos.** `EventBus` desacopla módulos: el motor de automatización, la API (SSE) y los plugins
  se suscriben a los mismos eventos.
- **Resolución de perfil** (`AppRuntime.ResolveProfile`): juego en ejecución → asignación manual →
  predeterminado. Al cerrarse el juego se vuelve a resolver, lo que restaura el perfil anterior.

## Seguridad y robustez

- Perfiles y paquetes son **solo datos**; `ProfileValidator` descarta acciones desconocidas y acota
  todos los valores. No existe ninguna acción «ejecutar programa».
- Combinación de recuperación fija (PS + Options + Create) para desactivar un remapeo erróneo.
- Al cerrar (también tras excepción): se sueltan teclas, se detienen efectos, se devuelven los LEDs
  al firmware y se vuelven a mostrar los mandos ocultos. La lista de mandos ocultos se guarda en
  disco *antes* de ocultar, para deshacerlo tras un cierre brusco.
- Nunca se escribe en el firmware ni en memoria no volátil del mando.
- API: solo loopback, token con comparación en tiempo constante, sin CORS.
- Plugins: desactivados por defecto, contexto de carga propio y permisos declarados (ver
  docs/PLUGINS.md para el alcance real de ese aislamiento).

## Hilos

| Hilo | Función |
|---|---|
| `dsu-read-<id>` (uno por mando) | Lectura HID, remapeo, salida de teclado/ratón/mando virtual |
| `dsu-write-<id>` | Escritura HID coalescente |
| Timer 60 Hz | Composición del informe de salida |
| Timer 2 s | Procesos, conflictos |
| UI | Refresco a 30 Hz solo con la ventana visible |
