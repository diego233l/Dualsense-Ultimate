# Pruebas

## Resumen

| Tipo | Dónde | Resultado |
|---|---|---|
| Pruebas automatizadas (135) | `dotnet test`, Linux local | 135 superadas, 0 fallidas |
| Pruebas automatizadas (135) | GitHub Actions `windows-latest` y `ubuntu-latest` | Última compilación del programa superada en el [commit `9029d91`](https://github.com/diego233l/Dualsense-Ultimate/actions/runs/42875783452) |
| Render de la interfaz (13 páginas) | `tools/Screenshots`, Linux local y CI Windows/Linux | Sin errores |
| Publicación autocontenida `win-x64` | Linux local (compilación cruzada) | Genera `DualSenseUltimate.exe` (PE32+ GUI) |
| Instalador y desinstalador (Inno Setup) | CI Windows + hardware real | Compilación correcta; ejecución y validación completa comunicadas como superadas |
| **Hardware físico** | Usuario, 2026-11-10 | **Validación completa comunicada como superada** |
| Primera prueba física (usuario) | Antes de corregir dos defectos | La app detectaba el mando, pero otro emisor sobrescribía luces y gatillos; se corrigieron ambos defectos y se añadió aislamiento. La validación posterior completa pasó. |
| Ejecución interactiva en Windows | Usuario, 2026-11-10 | Validación completa comunicada como superada |

## Qué cubren las pruebas automatizadas

- **Protocolo** (`ProtocolTests`, `TriggerEffectTests`): CRC32 con vector estándar; análisis de
  informes USB con offsets escritos a mano según `hid-playstation.c`; ida y vuelta USB/Bluetooth;
  rechazo de CRC incorrecto y del informe corto de Bluetooth; botones del Edge; disposición exacta
  del informe de salida USB y Bluetooth; informe de liberación; MAC y firmware; DualShock 4;
  bytes de cada efecto de gatillo comparados con los de dualsensectl; saturación de valores inválidos.
- **Motores** (`EngineTests`): iluminación (todos los modos), patrones hápticos, mezclador, DSP de
  audio, sticks (zonas, curvas, calibración), remapeo (toggle, turbo, corta/larga/doble, combinaciones,
  shift, macros, regiones del touchpad, ratón, gestos, giroscopio, agitar), aplicador de salida.
- **Perfiles y automatización** (`ProfileAndAutomationTests`): persistencia, exportar/importar,
  paquetes inválidos, **perfil hostil neutralizado**, recuperación desde `.bak`, copias de seguridad,
  reglas de batería/procesos/enfriamiento, vigilancia de procesos.
- **Integración extremo a extremo** (`IntegrationTests`), con mandos simulados que hablan el
  protocolo real: detección e identificación USB y Bluetooth; desconexión y reconexión; mismo mando
  por dos transportes; varios mandos con perfiles independientes; perfil por juego y restauración;
  remapeo hasta el inyector y liberación de teclas al desconectar; modo híbrido; regla de batería;
  liberación del mando al cerrar; **20 000 informes aleatorios sin errores**; API (token, control,
  errores); rutas de HidHide.
- **Catálogo y plugins** (`CatalogAndPluginTests`): todos los paquetes de `community/` se importan
  sin avisos; el cliente rechaza orígenes no HTTPS; un plugin real se carga solo si está activado,
  recibe eventos y controla el mando; se le deniega una llamada sin permiso declarado; manifiestos
  maliciosos ignorados.

## Alcance y registro

El simulador valida la lógica en CI; el usuario confirma que la validación completa en hardware
real también se superó el 10 de noviembre de 2026. No se adjuntó el detalle de los modelos, firmware,
conexiones ni entornos concretos, así que esos datos no se atribuyen a la prueba.

## Validación en hardware

`docs/HARDWARE_TEST_PLAN.md` conserva los pasos para repetir la validación. `tools/HardwareCheck`
automatiza la parte de protocolo y genera un informe; los detalles de esta sesión no se adjuntaron.

| Fecha | Mando | Conexión | Firmware | Windows | Resultado | Informe |
|---|---|---|---|---|---|---|
| 2026-11-10 | Funciones implementadas | No registrado | No registrado | No registrado | Validación completa superada (usuario) | No adjunto |
