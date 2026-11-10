# Plugins

Un plugin es una biblioteca .NET que implementa `IDsuPlugin` y recibe un `IDsuHost`.

## Estructura

```
%APPDATA%\DualSenseUltimate\plugins\MiPlugin\
  plugin.json
  MiPlugin.dll
```

```json
{
  "id": "autor.mi-plugin",
  "name": "Mi plugin",
  "version": "1.0",
  "author": "Autor",
  "description": "Qué hace",
  "assembly": "MiPlugin.dll",
  "permissions": ["ReadState", "ControlOutput", "Profiles", "Events"]
}
```

## Código mínimo

```csharp
using DualSenseUltimate.Plugins;

public sealed class MiPlugin : IDsuPlugin
{
    private IDisposable? _sub;
    public void Start(IDsuHost host) =>
        _sub = host.Subscribe(e => { if (e.Type == "controller.connected") host.PlayHaptic(e.ControllerId!, "Clic"); });
    public void Stop() => _sub?.Dispose();
}
```

Proyecto: referencia `DualSenseUltimate.Plugin.Abstractions` con `Private="false"` y activa
`EnableDynamicLoading` (ver `samples/plugins`).

## Permisos

| Permiso | Permite |
|---|---|
| `ReadState` | `Controllers` |
| `ControlOutput` | `SetLightbar`, `ClearOverrides`, `PlayHaptic`, `Rumble`, `SetTriggerPreset` |
| `Profiles` | `ApplyProfile` |
| `Events` | `Subscribe`, `Notify` |

Una llamada sin el permiso declarado lanza `UnauthorizedAccessException`. `Log` y `DataDirectory`
están siempre disponibles.

## Modelo de seguridad (importante)

- Los plugins **no se cargan** hasta que el usuario los activa en la sección Plugins, donde ve los
  permisos solicitados.
- Cada plugin se carga en su propio `AssemblyLoadContext` y se descarga al desactivarlo.
- .NET no ofrece aislamiento de seguridad dentro de un proceso: los permisos restringen el acceso a
  la API de DualSense Ultimate, **no** impiden que el código del plugin use el sistema de archivos
  o la red con los permisos del usuario. Trata un plugin como cualquier programa que instalas.
- Para integraciones que no deban ejecutarse dentro de la aplicación, usa la API local.

## Ejemplos incluidos

- `BatteryLoggerPlugin`: registra la batería en CSV y avisa con un destello por debajo del 10 %.
- `HomeAssistantPlugin`: reenvía los eventos a un webhook de Home Assistant.

Integraciones como Discord o Steam siguen el mismo patrón: suscribirse a eventos y llamar al host.
