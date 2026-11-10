# Manual de usuario

## 1. Primeros pasos

1. Instala la aplicación (ver README). Se inicia desde el menú Inicio como cualquier programa.
2. Conecta el mando por **USB**, o por **Bluetooth**: mantén PS + Create hasta que la barra parpadee
   y emparéjalo en *Configuración → Bluetooth y dispositivos*.
3. El mando aparece en el **Dashboard**. Con varios mandos, elige en el selector superior derecho
   cuál estás configurando.

Al cerrar la ventana la aplicación sigue en la **bandeja del sistema** para mantener activos tus
ajustes. Para salir del todo: clic derecho en el icono → *Salir*, o *Ajustes → Salir*.

Todos los cambios se aplican al instante y se guardan solos en el perfil activo.

## 2. Dashboard

Tarjeta por mando (modelo, conexión, batería, perfil, gatillos, luz, calidad de la comunicación),
dibujo interactivo que refleja lo que pulsas y actividad reciente. Pulsar una parte del dibujo abre
su configuración en *Remapeo*.

## 3. Mandos conectados

Datos de identificación (MAC, firmware), nombre personalizado, perfil asignado y el botón
*Identificar*, que hace vibrar y parpadear ese mando.

## 4. Iluminación RGB

- **Efecto**: estático, respiración, arcoíris, ciclo de colores, pulso, reactivo a la música,
  nivel de batería o personalizado (editor de colores clave).
- **LEDs de jugador**: jugador 1-4, todos, nivel de batería o máscara personalizada.
- **LED del micrófono**: apagado, encendido o parpadeo.
- **Presets**: guarda el ajuste actual, expórtalo a un archivo o importa uno.

## 5. Adaptive Triggers

Cada gatillo tiene su editor. Elige un **preset** o un **tipo de efecto** y ajusta sus parámetros;
la gráfica muestra la resistencia en cada una de las 10 zonas del recorrido y una línea naranja con
la posición actual del gatillo. Aprieta el gatillo para probarlo.

| Tipo | Sensación |
|---|---|
| Resistencia constante | Dureza uniforme desde una posición |
| Resistencia progresiva | Dureza que crece o decrece |
| Resistencia por zonas | Una fuerza distinta en cada zona |
| Arma de fuego | Resistencia y «clic» de disparo |
| Arco / tensión | Tensión con retroceso al soltar |
| Vibración | El gatillo vibra a la frecuencia elegida |
| Pulsación (galope) | Dos golpes rítmicos |
| Máquina | Vibración que alterna dos intensidades |
| Bajo nivel | Los 11 bytes del informe, para experimentar |

*Dejar los gatillos al juego* hace que la app no los toque (juegos con soporte nativo).

## 6. Háptica

Intensidad global, prueba de cada motor, **patrones** (predefinidos y propios, con editor de pasos),
**reactivo al sonido** (ganancia, filtro y ecualizador) y **audio háptico nativo** por USB con
pruebas de canal izquierdo, derecho y barrido de frecuencias.

## 7. Remapeo

1. Pulsa un botón en el dibujo (o elige un gesto o zona en la lista).
2. *Personalizar* y añade acciones: botón de mando, tecla, botón de ratón, rueda, macro o acción
   especial (perfil siguiente/anterior, activar giroscopio, mostrar batería…).
3. Opcional: modo *Alternar* o *Turbo*, y capas de **pulsación larga**, **doble pulsación** y
   **modificador**.

Otras tarjetas: **Emulación y modo híbrido**, **Touchpad** (ratón, 2 o 4 regiones, gestos),
**Modificador y combinaciones**, **Macros**.

**Recuperación:** PS + Options + Create activa o desactiva todo el remapeo en cualquier momento.

### ¿Híbrido o emulación?

- **El DualSense real (híbrido)**: recomendado para juegos con soporte de DualSense. Conservas
  gatillos y háptica nativos y añades teclas, ratón, macros y giroscopio. No permite cambiar un
  botón del mando por otro.
- **Mando Xbox virtual**: para juegos que solo aceptan XInput. Requiere ViGEmBus; con HidHide se
  oculta el mando real y se evita la doble entrada.
- **DualShock 4 virtual**: para juegos que reconocen DS4 pero no DualSense; conserva touchpad y
  giroscopio.

## 8. Joysticks y giroscopio

Gráfica en vivo de cada joystick con zona muerta, anti-zona muerta, saturación, sensibilidad, curva
e inversión. *Calibrar centro* mide la posición de reposo durante 1,5 s y compensa el drift.
El giroscopio puede actuar como ratón o como joystick, con botón de activación, filtrado y
calibración.

## 9. Perfiles

Un perfil guarda todo lo anterior. Puede ser **predeterminado** (global), asignarse a un **mando**
o activarse solo al detectar un **ejecutable** (por ejemplo `eldenring.exe`); al cerrar el juego
vuelve el perfil anterior. Se pueden duplicar, exportar e importar.

## 10. Biblioteca comunitaria

*Actualizar catálogo* descarga la lista; *Descargar e instalar* añade el elemento tras validarlo.
Para compartir los tuyos, consulta `community/README.md`.

## 11. Automatizaciones

Reglas con un desencadenante (batería baja, carga, conexión, desconexión, inicio o cierre de una
aplicación, notificación) y una o varias acciones (color, destello, perfil, patrón háptico, preset
de gatillos, detener efectos).

## 12. Plugins y API

Ver `docs/PLUGINS.md` y `docs/API.md`.

## 13. Ajustes

Tema, inicio con Windows, bandeja, estado de los controladores opcionales, actualizaciones, copias
de seguridad (crear y restaurar) y registros de diagnóstico.


### Batería

El mando comunica su batería en pasos del 10 %. La aplicación estabiliza ese dato y estima el valor
intermedio y la autonomía restante; la estimación se ajusta sola a tu mando tras varias horas de uso
(se ve en *Mandos → Batería*).

### Actualizar

Cuando hay una versión nueva aparece el botón **Actualizar a x.y.z** arriba a la derecha. En
*Ajustes → Actualizaciones* puedes leer las novedades y pulsar **Descargar e instalar**: la app
descarga el instalador oficial, lo verifica, se cierra, se actualiza conservando tus perfiles y se
vuelve a abrir. La versión portable se actualiza sustituyendo la carpeta.

## 14. Solución de problemas

| Problema | Qué hacer |
|---|---|
| El color, el LED del micrófono o los gatillos solo se aplican un instante | Otro programa está escribiendo en el mando. Ciérralo (Steam, DSX, DS4Windows, software RGB; mira también la bandeja), o activa *Ajustes → Aislamiento del mando* (requiere HidHide) y reconecta el mando. Sin HidHide: *Ajustes → Prioridad de salida* |
| El mando no aparece | Prueba otro cable/puerto; por Bluetooth, elimínalo y vuelve a emparejar. *Mandos → Buscar ahora* |
| La luz o los gatillos cambian solos | Otro programa controla el mando (aviso superior). Cierra DS4Windows/DSX o desactiva Steam Input para PlayStation |
| Entradas duplicadas en un juego | Con emulación, instala HidHide y activa *Ocultar el mando físico* |
| «ViGEmBus no está instalado» | *Ajustes → Descargar ViGEmBus* |
| Un perfil me ha dejado sin control | PS + Options + Create; o *Ajustes → Copias de seguridad → Restaurar* |
| El teclado emulado no funciona en un juego | Si el juego se ejecuta como administrador, inicia la app también como administrador |
| Sin audio háptico | Solo por USB; pulsa *Comprobar* en Háptica para ver el motivo |
| Un mando quedó oculto | *Ajustes → Mostrar todos los mandos ocultos* |

Datos y registros: `%APPDATA%\DualSenseUltimate`.
