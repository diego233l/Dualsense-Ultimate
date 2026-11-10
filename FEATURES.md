# Funcionalidades y estado de verificación

Leyenda:

- **HW** — implementada y probada en hardware real.
- **SIM** — implementada y probada con pruebas automatizadas contra el simulador de protocolo.
- **PEND** — implementada y compila en Windows, pero **nunca se ha ejecutado contra hardware o
  drivers reales**; pendiente de validación física.
- **PARCIAL** — implementada en parte (se indica qué falta).
- **NO** — no implementada por una limitación técnica (ver KNOWN_ISSUES.md).

> A día de hoy **ninguna función tiene estado HW**: no se ha dispuesto de un mando físico. Las
> marcadas SIM tienen su lógica verificada; que el mando real responda igual depende de que el
> protocolo documentado sea correcto, lo cual es probable (coincide con el driver de Linux) pero no
> está comprobado aquí.

## Gestión de mandos

| Función | Estado | Notas |
|---|---|---|
| Detección DualSense / Edge / DualShock 4 (VID/PID) | SIM | |
| USB y Bluetooth, distinción automática | SIM | Por longitud de informe |
| Conexión y desconexión en caliente, reconexión | SIM | |
| Varios mandos, identificación por MAC | SIM | Mismo mando por USB+BT no se duplica |
| Modelo, id, conexión, firmware | SIM | Firmware: informe 0x20 (DualSense), 0xA3 (DS4) |
| Batería y estado de carga | SIM | |
| Batería calibrada (filtrado, interpolación, aprendizaje por mando, autonomía restante) | SIM | Precisión real pendiente de contrastar con el uso |
| Temperatura de batería | NO | El mando no la expone |
| Detección de problemas de comunicación | SIM | Tasa de informes, errores CRC, estados Ok/Degradado/Perdido |
| Nombres personalizados | SIM | |
| Acceso HID real en Windows (HidSharp) | PEND | Incluye relleno a 547 bytes en Bluetooth |

## Iluminación

| Función | Estado |
|---|---|
| Color RGB, brillo | SIM |
| LEDs de jugador (modos, máscara, brillo), LED de micrófono | SIM |
| Estático, respiración, arcoíris, ciclo, pulso, batería, editor personalizado | SIM |
| Reactivo a notificaciones | SIM (las notificaciones entran por API/plugins; ver KNOWN_ISSUES) |
| Reactivo a la música | PEND (captura WASAPI) |
| Presets: guardar, importar, exportar | SIM |

## Gatillos adaptativos

| Función | Estado |
|---|---|
| Resistencia constante, progresiva, por zonas | SIM (bytes verificados contra dualsensectl) |
| Arma, arco, vibración, vibración por zonas, galope, máquina | SIM |
| Parámetros de bajo nivel (bloque en bruto) | SIM |
| Editor visual, zona de pruebas, L2/R2 independientes | SIM (UI) / PEND (sensación física) |
| Biblioteca, importación, exportación | SIM |
| Cambio automático por juego | SIM |
| Efectos dinámicos según el estado del juego | PARCIAL: solo mediante API/plugin o soporte nativo del juego; no existe detección universal |
| Lectura del estado del actuador | PEND (offsets no confirmados por una fuente primaria) |

## Háptica

| Función | Estado |
|---|---|
| Vibración por canal, intensidad global | SIM |
| Patrones predefinidos, editor, importación/exportación | SIM |
| Mezcla de fuentes sin conflictos | SIM |
| Reactivo al sonido (DSP: EQ, paso bajo, envolvente) | SIM (DSP) / PEND (captura) |
| Audio háptico nativo por USB, prueba de canales, barrido | PEND |
| Audio háptico por Bluetooth | NO |

## Remapeo

| Función | Estado |
|---|---|
| Todos los botones, cruceta, L3/R3, PS, Create, Options, Mute, paletas y Fn del Edge | SIM |
| Varias acciones, toggle, turbo, corta/larga/doble, modificador, combinaciones | SIM |
| Macros y secuencias temporizadas | SIM |
| Touchpad: ratón, toque, desplazamiento, 2 y 4 regiones, gestos | SIM |
| Sticks: zona muerta, anti-zona, curvas, sensibilidad, saturación, inversión, calibración/drift | SIM |
| Giroscopio: ratón, stick, activación, filtrado, calibración, gesto de agitar | SIM (sentido de los ejes PEND) |
| Inyección real de teclado y ratón (SendInput) | PEND |
| Acciones condicionadas | PARCIAL: capa shift y combinaciones; no hay condiciones arbitrarias |

## Emulación

| Función | Estado |
|---|---|
| Xbox 360 virtual (XInput) | PEND |
| Xbox One | Equivale al anterior para los juegos (ver KNOWN_ISSUES) |
| DualShock 4 virtual con touchpad y movimiento | PEND |
| DualSense virtual | NO (ViGEmBus no lo ofrece); alternativa: modo híbrido |
| HidHide: ocultar, mostrar, recuperación tras cierre brusco | SIM (conversión de rutas) / PEND (driver) |
| Reenvío de vibración del mando virtual | PEND |
| Modo híbrido (propiedad parcial del informe de salida) | SIM |
| Desactivar efectos de luz del juego (reenvío continuo solo de luces) | SIM / PEND (resultado visual en juegos reales) |
| Detección de programas en conflicto | SIM (por nombre de proceso) |
| Aislamiento del mando (solo la app y programas permitidos) | SIM (lógica) / PEND (HidHide real) |
| Prioridad de salida (reenvío continuo) | SIM |
| Sunshine / Moonlight | PEND: debería funcionar al usar el mismo bus ViGEm; sin probar |

## Perfiles, automatización y ecosistema

| Función | Estado |
|---|---|
| Perfiles globales, por mando y por ejecutable; restauración al cerrar | SIM |
| Importación/exportación con validación | SIM |
| Motor de reglas (batería, conexión, procesos, notificaciones) | SIM |
| Plugins con permisos, plugins de ejemplo | SIM |
| API REST local + SSE, ejemplos Python/JavaScript | SIM |
| Biblioteca comunitaria: descarga e instalación | SIM (validación del catálogo) / PEND (descarga real: el catálogo existe al fusionar en `main`) |
| Biblioteca comunitaria: publicar desde la app, valoraciones | NO (requiere un servicio en línea); alternativa: pull requests |

## Aplicación

| Función | Estado |
|---|---|
| Interfaz de 13 secciones, tema oscuro/claro | SIM (renderizada en CI en Windows y Linux) |
| Bandeja del sistema, inicio con Windows, instancia única | PEND |
| Copias de seguridad y recuperación de configuración | SIM |
| Actualizaciones: aviso, descarga verificada e instalación desde la app | SIM (lógica y verificación) / PEND (instalación real: requiere una versión publicada) |
| Instalador y desinstalador | PEND (se compila en CI; no se ha ejecutado) |
