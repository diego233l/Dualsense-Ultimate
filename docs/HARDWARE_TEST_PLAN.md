# Plan de validación en hardware

Objetivo: pasar las funciones de **PEND/SIM** a **HW** en `FEATURES.md`. Anota para cada sesión el
modelo del mando, la conexión, el firmware (sección Mandos), la versión de Windows y la versión de
la app, y registra el resultado en `TESTING.md`.

## A. Protocolo (automatizado)

```powershell
dotnet run --project tools/HardwareCheck
```

Ejecuta lectura de informes, identificación, batería, barra de luz, LEDs, vibración por canal, los
siete efectos de gatillo y la liberación al cerrar, preguntando qué ves y sientes. Genera
`hardware-report-*.md`. Repetir con: DualSense USB, DualSense Bluetooth, DualSense Edge y
DualShock 4 (USB y Bluetooth).

## B. Aplicación

| # | Prueba | Resultado esperado |
|---|---|---|
| B1 | Conectar por USB con la app abierta | Aparece en menos de 2 s con modelo, MAC, firmware y batería |
| B2 | Desconectar y reconectar 10 veces | Sin bloqueos; el perfil se reaplica |
| B3 | Emparejar por Bluetooth | Conexión «Bluetooth»; entrada y luces funcionan |
| B4 | Dos mandos a la vez con perfiles distintos | Colores y gatillos independientes |
| B5 | Cada modo de iluminación | El mando coincide con la vista previa |
| B6 | Cada efecto de gatillo en L2 y R2 | Sensación acorde a la gráfica |
| B7 | Patrones hápticos y vibración por canal | Izquierda grave, derecha aguda |
| B8 | Audio háptico por USB: izquierdo, derecho, barrido | Vibra solo el lado indicado |
| B9 | Reactivo al sonido con música | Vibración y luz siguen los graves |
| B10 | Remapear Cruz → Espacio (Bloc de notas) | Escribe espacios; sin teclas pegadas al desconectar |
| B11 | Turbo, toggle, larga, doble, combinación, macro | Según lo descrito en el manual |
| B12 | Touchpad como ratón; 2 y 4 regiones; gestos | Cursor fluido; cada región dispara su acción |
| B13 | Giroscopio como ratón | Derecha = cursor a la derecha (si no, anotar el eje) |
| B14 | Calibración de joystick con drift | Reposo estable en el centro |
| B15 | Perfil por juego: abrir y cerrar el ejecutable | Se aplica y se restaura |
| B16 | Regla «batería < 20 % → rojo» | Se dispara una vez |
| B17 | Cerrar la app; finalizarla desde el Administrador de tareas | El mando no queda vibrando; tras reabrir, ningún mando oculto |
| B18 | Sesión de 4 horas | Sin crecimiento de memoria ni errores en el registro; CPU en reposo < 2 % |

## C. Emulación

| # | Prueba | Resultado esperado |
|---|---|---|
| C1 | Xbox 360 virtual con ViGEmBus | Aparece en `joy.cpl` y en un juego XInput |
| C2 | HidHide activado | El juego ve un solo mando; sin doble entrada |
| C3 | Vibración del juego en el mando virtual | Llega al mando real |
| C4 | DualShock 4 virtual | Reconocido; touchpad y giroscopio llegan al juego |
| C5 | Modo híbrido en un juego con soporte DualSense, con «gatillos al juego» | Efectos nativos intactos; teclas añadidas funcionan |
| C6 | Steam con Steam Input activado y desactivado | Documentar comportamiento y conflictos |
| C7 | Sunshine + Moonlight | El mando virtual llega al cliente |

## D. Instalador

Instalar sin administrador; accesos directos; inicio automático; instalación opcional de drivers;
actualizar sobre una versión anterior; desinstalar conservando y eliminando la configuración;
comprobar que no quedan mandos ocultos ni entradas de inicio.
