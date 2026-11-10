# Limitaciones y problemas conocidos

## Validación

- El usuario informa que la validación completa de las funciones implementadas y el plan de hardware
  se superaron el 10 de noviembre de 2026. No se adjuntó el informe ni el detalle de modelo, firmware,
  conexión o entorno; la cobertura de otras combinaciones de hardware y Windows puede variar.

## Limitaciones del hardware, de Windows o del protocolo

| Limitación | Motivo | Alternativa |
|---|---|---|
| No hay temperatura de batería | El DualSense no la incluye en ningún informe conocido | — |
| Audio háptico solo por USB | Por Bluetooth Windows no crea un dispositivo de audio para los actuadores; el formato propietario de Sony sobre HID no está implementado | Vibración clásica por Bluetooth |
| No existe mando virtual DualSense | ViGEmBus solo emula Xbox 360 y DualShock 4 | Modo híbrido: el juego usa el DualSense real |
| «Xbox One» virtual | No hay driver público; XInput no distingue 360 de One | Mando Xbox 360 virtual |
| En modo híbrido no se puede cambiar un botón por otro botón de mando ni anular el original | El juego lee el dispositivo físico; solo un driver de filtro podría alterarlo | Emulación completa (se pierden los efectos nativos del juego) |
| Con emulación el juego pierde gatillos/háptica nativos | El juego ya no ve un DualSense | Los efectos del perfil siguen activos y la vibración se reenvía |
| Efectos de gatillo dinámicos | Ningún programa puede leer el estado interno de un juego de forma general | API local, plugins o soporte nativo del juego |
| Lectura de notificaciones de Windows | La API de escucha exige una app empaquetada (MSIX) con consentimiento | Enviar notificaciones por API/plugin (`POST /notify`) |
| El teclado/ratón emulado no llega a ventanas elevadas | UIPI de Windows | Ejecutar la app como administrador en ese caso |
| Luz/gatillos sobrescritos por otros programas | El último informe de salida gana | Aviso de conflictos; cerrar DS4Windows/DSX o desactivar Steam Input para PlayStation |
| Los LEDs de jugador y el LED del micrófono no existen en DualShock 4 | Hardware | — |

## Funciones deliberadamente acotadas

- **Aislamiento**: Windows no permite restringir qué procesos escriben en un dispositivo HID; solo
  es posible con el controlador HidHide. Sin HidHide la única mitigación es «Prioridad de salida».
  HidHide impide *abrir* el mando: un programa que ya lo tenía abierto lo conserva hasta reiniciarse
  o hasta reconectar el mando. Los juegos elevados o protegidos por anti-trampas no se pueden
  añadir automáticamente a la lista de permitidos; hay que añadirlos a mano.

- **Biblioteca comunitaria**: catálogo estático en Git. La descarga e instalación se validaron; no
  hay publicación desde la app ni valoraciones porque exigirían un servidor con cuentas.
- **Plugins**: se ejecutan dentro del proceso. Los permisos limitan la API que el plugin puede
  usar, no lo que su código puede hacer en el sistema. Solo deben activarse plugins de confianza.
- **Actualizaciones**: funcionan desde las versiones estables publicadas en GitHub Releases (etiqueta `vX.Y.Z`); nunca se instalan sin confirmación. El instalador no está firmado digitalmente, por lo que Windows SmartScreen puede mostrar un aviso.
- **Batería**: el hardware solo da 11 niveles. El porcentaje intermedio y la autonomía son estimaciones; las primeras horas usan valores típicos hasta que la app observa varios cambios de nivel de ese mando.
- **Acciones condicionadas**: se ofrecen capa shift, combinaciones y capas de pulsación, no un
  lenguaje de condiciones.
- **Detección de juego**: por nombre de ejecutable. Dos juegos con el mismo nombre de proceso no se
  distinguen.

## Compatibilidad entre entornos

La validación comunicada pasó en el entorno usado. Como no se adjuntó la matriz de dispositivos,
versiones de Windows, drivers ni idiomas, conviene vigilar estas variaciones al recibir informes de
otros equipos:

- Ejes del giroscopio y offsets del estado del actuador.
- Escritura Bluetooth mediante `WriteFile` con búfer de 547 bytes.
- Informe extendido del DualShock 4 virtual (touchpad y movimiento).
- Nombre del dispositivo de audio en sistemas no ingleses.
- Elevación requerida por `HidHideCLI` según su versión.
