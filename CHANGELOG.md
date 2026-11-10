# Changelog

## 1.0.0 — 2026-11-10

Primera versión estable. El usuario confirma que todas las funciones implementadas se validaron
correctamente en hardware real el 10 de noviembre de 2026; consulta [FEATURES.md](FEATURES.md) para
el alcance y las limitaciones conocidas.

Primera versión.

Luces del juego:

- Nuevo: **Desactivar efectos de luz del juego** (Iluminación y Remapeo → modo híbrido). Sustituye a
  «Dejar luces al juego». Reenvía la iluminación del perfil cada pocos milisegundos para tapar las
  luces que impone el juego (p. ej. policía en GTA V) sin tocar sus gatillos ni su vibración.
- Sin esa opción, el refresco periódico ya no reescribe las luces, de modo que la app no provoca
  parpadeos al competir con el juego.
- La prueba guiada de hardware se incluye en la app instalada (Ajustes → Diagnóstico).

Batería y actualizaciones:

- **Batería calibrada**: el mando solo informa en pasos del 10 % y el valor bruto oscila. Ahora se
  filtra (un cambio debe mantenerse unos segundos; no sube mientras descarga ni baja mientras carga),
  se interpola dentro de cada paso y la velocidad de descarga/carga se aprende por mando y se guarda.
  Se muestra la autonomía restante estimada.
- **Actualizaciones desde la app**: aviso en la cabecera, notas de la versión, descarga con progreso,
  verificación de tamaño y SHA-256, instalación silenciosa y reapertura automática. Opción de omitir
  una versión; comprobación al iniciar y cada 12 horas.

Correcciones tras la primera prueba con un mando real (luces, LED de micrófono y gatillos se aplicaban solo un instante):

- El ajuste «lightbar setup» se enviaba en todos los informes; ahora solo en el primero tras conectar.
- Un mando visible por dos rutas HID (Bluetooth + cable) recibía un informe de liberación cada pocos
  segundos al descartar la ruta duplicada; ahora se cierra sin escribir nada.
- Nuevo: **Aislamiento del mando** (Ajustes): oculta el mando con HidHide a todo programa salvo la
  app y los permitidos; los juegos nativos asociados a un perfil se permiten automáticamente.
- Nuevo: **Prioridad de salida** (Ajustes): reenvía el estado ~30 veces por segundo para prevalecer
  sobre otro programa cuando HidHide no está instalado.
- Contador de escrituras por mando en la sección Mandos; más programas en la detección de conflictos.

- Núcleo: protocolo HID de DualSense, DualSense Edge y DualShock 4 (USB y Bluetooth, CRC32),
  gestión de dispositivos con conexión en caliente y varios mandos.
- Iluminación: 9 modos, editor por fotogramas clave, LEDs de jugador, LED de micrófono, presets.
- Gatillos adaptativos: 10 tipos de efecto + modo en bruto, editor visual, biblioteca.
- Háptica: vibración, patrones con editor, reactivo al audio, audio háptico por USB.
- Remapeo: capas (corta/larga/doble/shift), turbo, toggle, combinaciones, macros, touchpad
  (ratón/regiones/gestos), sticks (zonas, curvas, calibración), giroscopio (ratón/stick/gesto).
- Emulación: Xbox 360 y DualShock 4 virtuales (ViGEmBus), HidHide, modo híbrido.
- Perfiles por juego con restauración automática, automatizaciones, plugins, API REST local.
- Biblioteca comunitaria (catálogo estático), importación/exportación validada.
- Interfaz de 13 secciones, tema oscuro y claro, icono de bandeja.
- Instalador Inno Setup, CI en Windows y Linux, 137 pruebas automatizadas.

Validación completa en hardware real confirmada por el usuario el 10 de noviembre de 2026. Las
funciones no implementadas y las limitaciones conocidas se mantienen identificadas en
[FEATURES.md](FEATURES.md) y [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
