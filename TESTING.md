# Pruebas

## Resumen

| Tipo | Dónde | Resultado |
|---|---|---|
| Pruebas automatizadas (135) | `dotnet test`, Linux local | 135 superadas, 0 fallidas |
| Pruebas automatizadas (135) | GitHub Actions `windows-latest` y `ubuntu-latest` | Superadas (ejecuciones 1 a 4 del flujo `build`) |
| Render de la interfaz (13 páginas) | `tools/Screenshots`, Linux local y CI Windows/Linux | Sin errores |
| Publicación autocontenida `win-x64` | Linux local (compilación cruzada) | Genera `DualSenseUltimate.exe` (PE32+ GUI) |
| Compilación del instalador (Inno Setup) | CI Windows | Correcta (ejecuciones 3 y 4 del flujo `build`); el instalador **no se ha ejecutado** |
| **Hardware físico** | — | **No realizado: no se dispone de mando** |
| Hardware físico (usuario, DualSense) | Primera prueba | La app detecta el mando y escribe luces/gatillos, pero otro emisor los sobrescribía; corregidos dos defectos propios y añadido aislamiento. Tras las correcciones el usuario confirma que luces, LED de micrófono y gatillos funcionan (sin informe detallado: modelo, conexión y firmware no registrados) |
| Ejecución interactiva en Windows | — | **No realizada** |

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

## Qué NO cubren

El simulador valida el *software*, no el hardware. Queda sin verificar: que un mando real interprete
los informes como se espera, el comportamiento de HidSharp sobre la pila Bluetooth de Windows,
SendInput, ViGEmBus, HidHide, WASAPI, la bandeja del sistema y el instalador. Tampoco hay pruebas
con Steam ni con juegos.

## Validación en hardware

`docs/HARDWARE_TEST_PLAN.md` describe el procedimiento. `tools/HardwareCheck` automatiza la parte
de protocolo y genera un informe. Los resultados se registrarán aquí:

| Fecha | Mando | Conexión | Firmware | Windows | Resultado | Informe |
|---|---|---|---|---|---|---|
| — | — | — | — | — | Sin ejecutar | — |
