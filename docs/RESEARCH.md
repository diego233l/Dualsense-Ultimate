# Investigación técnica y análisis de viabilidad

Fuentes primarias consultadas durante el desarrollo: driver `hid-playstation.c` del kernel Linux
(mantenido por Sony), `dualsensectl`, y documentación de ViGEmBus, HidHide, HidSharp y NAudio.
Referencias de comportamiento: DS4Windows, pydualsense, DSX, Steam Input.

## 1. Identificación

| Mando | VID:PID |
|---|---|
| DualSense | 054C:0CE6 |
| DualSense Edge | 054C:0DF2 |
| DualShock 4 v1 / v2 / adaptador | 054C:05C4 / 09CC / 0BA0 |

## 2. Informes de entrada del DualSense

| | USB | Bluetooth |
|---|---|---|
| Id de informe | 0x01 | 0x31 (0x01 reducido hasta leer el informe de características 0x05) |
| Tamaño | 64 | 78 (últimos 4 bytes: CRC32, semilla 0xA1) |
| Inicio de la carga | byte 1 | byte 2 |

Carga común (63 bytes): sticks (0-3), L2/R2 (4-5), contador (6), botones (7-10), giroscopio 3×int16
(15-20), acelerómetro 3×int16 (21-26), marca de tiempo (27-30), dos puntos táctiles de 4 bytes
(32-39), estado (52-54). Batería: `estado[0] & 0x0F` (×10 + 5 %), carga en el nibble alto
(0 descargando, 1 cargando, 2 completa, 0xA/0xB/0xF error). Touchpad: 1920×1080, bit 7 del primer
byte = dedo ausente. DualSense Edge: Fn y paletas traseras en los bits 4-7 de `botones[2]`.
Resolución IMU: 1024 unidades por °/s y 8192 por g, con calibración de fábrica en el informe 0x05.

## 3. Informe de salida del DualSense

USB: id 0x02. Bluetooth: id 0x31, byte de secuencia, etiqueta 0x10, CRC32 (semilla 0xA2) al final;
78 bytes (Windows declara 547 y exige rellenar). Bloque común de 47 bytes:

| Offset | Campo |
|---|---|
| 0, 1, 38 | *valid flags*: qué campos aplica el mando |
| 2, 3 | motor derecho, motor izquierdo |
| 8 | LED del micrófono (0 apagado, 1 encendido, 2 parpadeo) |
| 10-20 | gatillo derecho: modo + 10 parámetros |
| 21-31 | gatillo izquierdo |
| 41-43 | configuración de barra, brillo de LEDs, LEDs de jugador (5 bits) |
| 44-46 | rojo, verde, azul |

Los *valid flags* son la base del **modo híbrido**: si un bit no está activo, el mando conserva el
valor anterior de ese campo, así que dos programas pueden controlar partes distintas.

## 4. Gatillos adaptativos

Modos: 0x05 apagado, 0x21 resistencia, 0x22 arco, 0x23 galope, 0x25 arma, 0x26 vibración,
0x27 máquina. El recorrido se divide en 10 zonas; resistencia y vibración empaquetan una máscara de
zonas activas (16 bits) y una fuerza de 3 bits por zona (30 bits). Funciona igual por USB y Bluetooth.
No requiere drivers. El DualShock 4 no tiene este hardware.

## 5. Háptica

- **Vibración compatible**: dos bytes de motor; el DualSense la reproduce con sus actuadores de
  bobina. USB y Bluetooth.
- **Audio háptico**: por USB el mando es un dispositivo de audio USB de 4 canales; los canales 3 y 4
  alimentan los actuadores. Se implementa con WASAPI en modo compartido. Por Bluetooth el audio
  háptico viaja en informes HID propietarios y Windows no crea dispositivo de audio: no se implementa.

## 6. Matriz de viabilidad

| Función | Posible | USB | BT | API / librería | Driver | Limitaciones |
|---|---|---|---|---|---|---|
| Lectura de entrada, batería, IMU, touchpad | Sí | Sí | Sí | HID (HidSharp) | No | BT a menor frecuencia |
| Barra RGB, LEDs, LED de micrófono | Sí | Sí | Sí | HID | No | Otro programa puede sobrescribir |
| Gatillos adaptativos | Sí | Sí | Sí | HID | No | Solo DualSense |
| Vibración | Sí | Sí | Sí | HID | No | — |
| Audio háptico | Parcial | Sí | No | WASAPI (NAudio) | No | Ver §5 |
| Firmware, MAC | Sí | Sí | Sí | Informes 0x20, 0x09 | No | — |
| Temperatura de batería | No | — | — | — | — | No se expone |
| Teclado / ratón | Sí | — | — | SendInput | No | UIPI con ventanas elevadas |
| Mando Xbox / DS4 virtual | Sí | — | — | ViGEmBus | **Sí** | Driver sin mantenimiento (§8) |
| Ocultar mando físico | Sí | — | — | HidHide | **Sí** | Instalación con administrador |
| Mando DualSense virtual | No | — | — | — | — | Ningún bus público lo ofrece |
| Detección de juego | Sí | — | — | Lista de procesos | No | Por nombre de ejecutable |
| Notificaciones de Windows | No (sin MSIX) | — | — | UserNotificationListener | No | Exige app empaquetada |

Restricciones legales: se usa únicamente información de protocolo obtenida por interoperabilidad y
librerías con licencias permisivas (MIT, Apache-2.0, BSD-3). No se incluye código GPL. No se
distribuye firmware ni material de Sony.

## 7. Diferencias USB / Bluetooth

Bluetooth añade cabecera, secuencia y CRC32 en ambos sentidos; arranca en modo reducido hasta que
el anfitrión lee el informe 0x05; tiene menos ancho de banda (la app limita la frecuencia de
escritura a ~125 Hz frente a ~250 Hz por USB); no ofrece audio. Si el mismo mando aparece por los
dos transportes, se usa el de USB.

## 8. Mandos virtuales: alternativas a ViGEmBus

| Tecnología | Estado | Valoración |
|---|---|---|
| **ViGEmBus 1.22** | Archivado en 2023; firmado y funcional en Windows 10/11 | **Elegida.** Es la que usan DS4Windows, Sunshine y otros; no hay sustituto publicado con la misma compatibilidad |
| vJoy | Mantenido por forks | Solo DirectInput genérico: los juegos XInput no lo reconocen |
| Virtual HID Framework (VHF) de Microsoft | Oficial | Exige escribir y firmar un driver de núcleo propio |
| Interception / drivers de filtro | — | Mismo problema de firma; riesgo con anti-trampas |
| Steam Input | Activo | No es una API para terceros |

La integración está aislada tras `IVirtualPadFactory`: si aparece un sucesor de ViGEmBus solo hay
que añadir otra implementación.

## 9. HidHide

Driver de filtro que oculta dispositivos HID a todos los procesos salvo una lista blanca. La app se
añade a la lista, oculta el mando y activa el camuflaje con `HidHideCLI.exe`; al cerrar o
desinstalar lo revierte. La ruta de instancia se deriva de la ruta HID
(`\\?\hid#vid_054c&pid_0ce6…#{guid}` → `HID\VID_054C&PID_0CE6…`).

## 10. Coexistencia: qué es realmente posible (modo híbrido)

| Combinación | ¿Posible? |
|---|---|
| Juego con soporte nativo + luces de la app | Sí (propiedad parcial) |
| Juego con soporte nativo + teclado/ratón/macros/giroscopio-ratón añadidos | Sí |
| Juego con soporte nativo + paletas del Edge a teclas | Sí |
| Juego con soporte nativo + cambiar un botón por otro botón de mando | **No** sin mando virtual |
| Mando virtual + gatillos adaptativos definidos por el perfil | Sí |
| Mando virtual + efectos nativos del juego | **No**: el juego no ve un DualSense |
| Steam Input activo + emulación de la app | Doble entrada: desactivar una de las dos |

## 11. Steam Input

Steam abre el mando en modo compartido y, si Steam Input está activo para PlayStation, crea su
propio mando virtual y escribe la barra de luz. Recomendación documentada en la interfaz: usar el
modo híbrido con Steam Input activo, o desactivar Steam Input para el juego cuando se use la
emulación de la app. La app detecta `steam.exe`, DS4Windows, DSX, reWASD, InputMapper y
JoyShockMapper y avisa.
