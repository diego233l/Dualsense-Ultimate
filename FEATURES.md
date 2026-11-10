# Funcionalidades y estado de verificación

Leyenda:

- **HW** — implementada y probada en hardware real.
- **SIM** — implementada y probada con pruebas automatizadas contra el simulador de protocolo.
- **SIM + HW** — confirmada tanto por las pruebas automatizadas como en hardware real.
- **PEND** — validación física pendiente (no hay funciones implementadas con este estado en 1.0.0).
- **PARCIAL** — implementada en parte (se indica qué falta).
- **NO** — no implementada por una limitación técnica (ver KNOWN_ISSUES.md).

> El usuario informa que todas las funciones implementadas se validaron correctamente en hardware
> real el 10 de noviembre de 2026. No se adjuntó el detalle de los modelos, firmware ni conexiones.
> Las funciones **NO** siguen sin implementar y las **PARCIAL** conservan el alcance indicado.

## Gestión de mandos

| Función | Estado | Notas |
|---|---|---|
| Detección DualSense / Edge / DualShock 4 (VID/PID) | SIM + HW | |
| USB y Bluetooth, distinción automática | SIM + HW | Por longitud de informe |
| Conexión y desconexión en caliente, reconexión | SIM + HW | |
| Varios mandos, identificación por MAC | SIM + HW | Mismo mando por USB+BT no se duplica |
| Modelo, id, conexión, firmware | SIM + HW | Firmware: informe 0x20 (DualSense), 0xA3 (DS4) |
| Batería y estado de carga | SIM + HW | |
| Batería calibrada (filtrado, interpolación, aprendizaje por mando, autonomía restante) | SIM + HW | La autonomía es estimada y depende del uso |
| Temperatura de batería | NO | El mando no la expone |
| Detección de problemas de comunicación | SIM + HW | Tasa de informes, errores CRC, estados Ok/Degradado/Perdido |
| Nombres personalizados | SIM + HW | |
| Acceso HID real en Windows (HidSharp) | SIM + HW | Incluye relleno a 547 bytes en Bluetooth |

## Iluminación

| Función | Estado |
|---|---|
| Color RGB y brillo de la barra de luz | SIM + HW |
| LEDs de jugador (modos, máscara, brillo) | SIM + HW |
| LED del micrófono | SIM + HW |
| Estático, respiración, arcoíris, ciclo, pulso, batería, editor personalizado | SIM + HW |
| Reactivo a notificaciones | SIM + HW (las notificaciones entran por API/plugins; ver KNOWN_ISSUES) |
| Reactivo a la música | SIM + HW (captura WASAPI) |
| Presets: guardar, importar, exportar | SIM + HW |

## Gatillos adaptativos

| Función | Estado |
|---|---|
| Resistencia constante, progresiva, por zonas | SIM + HW (bytes verificados contra dualsensectl) |
| Gatillos adaptativos L2/R2 | SIM + HW |
| Arma, arco, vibración, vibración por zonas, galope, máquina | SIM + HW |
| Parámetros de bajo nivel (bloque en bruto) | SIM + HW |
| Editor visual, zona de pruebas, L2/R2 independientes | SIM + HW |
| Biblioteca, importación, exportación | SIM + HW |
| Cambio automático por juego | SIM + HW |
| Efectos dinámicos según el estado del juego | PARCIAL: solo mediante API/plugin o soporte nativo del juego; no existe detección universal |
| Lectura del estado del actuador | SIM + HW (lectura verificada en hardware) |

## Háptica

| Función | Estado |
|---|---|
| Vibración por canal, intensidad global | SIM + HW |
| Patrones predefinidos, editor, importación/exportación | SIM + HW |
| Mezcla de fuentes sin conflictos | SIM + HW |
| Reactivo al sonido (DSP: EQ, paso bajo, envolvente) | SIM + HW |
| Audio háptico nativo por USB, prueba de canales, barrido | SIM + HW |
| Audio háptico por Bluetooth | NO |

## Remapeo

| Función | Estado |
|---|---|
| Todos los botones, cruceta, L3/R3, PS, Create, Options, Mute, paletas y Fn del Edge | SIM + HW |
| Varias acciones, toggle, turbo, corta/larga/doble, modificador, combinaciones | SIM + HW |
| Macros y secuencias temporizadas | SIM + HW |
| Touchpad: ratón, toque, desplazamiento, 2 y 4 regiones, gestos | SIM + HW |
| Sticks: zona muerta, anti-zona, curvas, sensibilidad, saturación, inversión, calibración/drift | SIM + HW |
| Giroscopio: ratón, stick, activación, filtrado, calibración, gesto de agitar | SIM + HW (ejes validados) |
| Inyección real de teclado y ratón (SendInput) | SIM + HW |
| Acciones condicionadas | PARCIAL: capa shift y combinaciones; no hay condiciones arbitrarias |

## Emulación

| Función | Estado |
|---|---|
| Xbox 360 virtual (XInput) | SIM + HW |
| Xbox One | SIM + HW (equivale a Xbox 360 para los juegos; ver KNOWN_ISSUES) |
| DualShock 4 virtual con touchpad y movimiento | SIM + HW |
| DualSense virtual | NO (ViGEmBus no lo ofrece); alternativa: modo híbrido |
| HidHide: ocultar, mostrar, recuperación tras cierre brusco | SIM + HW (incluye el driver) |
| Reenvío de vibración del mando virtual | SIM + HW |
| Modo híbrido (propiedad parcial del informe de salida) | SIM + HW |
| Desactivar efectos de luz del juego (reenvío continuo solo de luces) | SIM (añadida después de la validación en hardware; pendiente de probar con juegos reales) |
| Detección de programas en conflicto | SIM + HW (por nombre de proceso) |
| Aislamiento del mando (solo la app y programas permitidos) | SIM + HW (incluye HidHide real) |
| Prioridad de salida (reenvío continuo) | SIM + HW |
| Sunshine / Moonlight | SIM + HW (flujo validado con ViGEm) |

## Perfiles, automatización y ecosistema

| Función | Estado |
|---|---|
| Perfiles globales, por mando y por ejecutable; restauración al cerrar | SIM + HW |
| Importación/exportación con validación | SIM + HW |
| Motor de reglas (batería, conexión, procesos, notificaciones) | SIM + HW |
| Plugins con permisos, plugins de ejemplo | SIM + HW |
| API REST local + SSE, ejemplos Python/JavaScript | SIM + HW |
| Biblioteca comunitaria: descarga e instalación | SIM + HW (flujo completo validado) |
| Biblioteca comunitaria: publicar desde la app, valoraciones | NO (requiere un servicio en línea); alternativa: pull requests |

## Aplicación

| Función | Estado |
|---|---|
| Interfaz de 13 secciones, tema oscuro/claro | SIM + HW (renderizada en CI en Windows y Linux) |
| Bandeja del sistema, inicio con Windows, instancia única | SIM + HW |
| Copias de seguridad y recuperación de configuración | SIM + HW |
| Actualizaciones: aviso, descarga verificada e instalación desde la app | SIM + HW |
| Instalador y desinstalador | SIM + HW |
