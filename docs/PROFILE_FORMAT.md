# Formato de perfiles y paquetes (esquema v1)

Todo lo que se exporta o comparte va dentro de un sobre versionado:

```json
{
  "format": "dualsense-ultimate",
  "schemaVersion": 1,
  "kind": "profile",
  "name": "Shooter genérico",
  "author": "…",
  "description": "…",
  "version": "1.0",
  "payload": { }
}
```

`kind`: `profile`, `trigger`, `lighting`, `haptic`, `macro`, `gyro`. El `payload` es el objeto
correspondiente. Propiedades en *camelCase*; enumeraciones como texto.

## Compatibilidad

- Un paquete con `schemaVersion` mayor que el admitido se rechaza con un mensaje que pide actualizar.
- Las propiedades desconocidas se ignoran y las ausentes toman su valor por defecto, de modo que las
  versiones futuras pueden añadir campos sin romper los archivos antiguos.
- Los cambios incompatibles incrementarán `schemaVersion` e incluirán una migración al cargar.

## Validación al importar

Tamaño máximo 1 MB; identificadores regenerados; textos recortados y sin caracteres de control;
números acotados a su rango; orígenes, teclas, botones y acciones especiales comprobados contra
listas cerradas (lo desconocido se descarta con un aviso); macros sin recursión; nombres de
ejecutable sin ruta; máximo de 64 macros, 256 pasos, 32 combinaciones y 16 acciones por asignación.
No existe ninguna acción capaz de ejecutar programas o código.

## Perfil (`kind: profile`)

| Campo | Contenido |
|---|---|
| `name`, `description`, `author` | Texto |
| `lighting` | `mode`, `color`, `color2`, `periodSeconds`, `brightness`, `palette[]`, `keyframes[]`, `playerLeds`, `playerLedMask`, `playerLedBrightness`, `micLed`, `notificationFlash`, `notificationColor` |
| `l2`, `r2` | Efecto de gatillo |
| `haptics` | `intensity`, `audioReactive`, `audioReactiveGain`, `audioCutoffHz`, `nativeAudioHaptics`, `bassGainDb`, `midGainDb`, `trebleGainDb` |
| `mapping` | `bindings{}`, `chords[]`, `shiftButton`, `leftStick`, `rightStick`, `l2`, `r2`, `gyro`, `touchpad`, `macros[]`, `swapSticks` |
| `emulation` | `mode` (`None`, `Xbox360`, `DualShock4`), `hideOriginal`, `forwardRumble`, `nativeTriggers`, `nativeRumble`, `nativeLighting` |
| `processes[]` | Ejecutables que activan el perfil |

## Efecto de gatillo (`kind: trigger`)

`type`, `name`, `start` (0-9), `end` (0-9), `strength` (1-8), `strength2`, `frequency` (Hz),
`period`, `firstFoot`, `secondFoot`, `zones[10]` (0-8), `rawBytes` (11 bytes en Base64).

## Asignación

```json
"Cross": {
  "press": [ { "type": "Key", "target": "Space" } ],
  "hold": [ { "type": "PadButton", "target": "Circle" } ],
  "doubleTap": [ { "type": "Macro", "target": "Doble salto" } ],
  "shift": [ { "type": "MouseButton", "target": "Left" } ],
  "mode": "Normal", "turboHz": 10, "holdMs": 400, "doubleTapMs": 250
}
```

Orígenes: botones (`Cross`, `Circle`, `Square`, `Triangle`, `L1`, `R1`, `L2`, `R2`, `Create`,
`Options`, `L3`, `R3`, `PS`, `Touchpad`, `Mute`, `DpadUp/Down/Left/Right`, `FnLeft`, `FnRight`,
`PaddleLeft`, `PaddleRight`), zonas (`TouchLeft`, `TouchRight`, `TouchTopLeft`, `TouchTopRight`,
`TouchBottomLeft`, `TouchBottomRight`) y gestos (`SwipeUp/Down/Left/Right`, `GyroShake`).

Tipos de acción: `PadButton`, `Key`, `MouseButton` (`Left`, `Right`, `Middle`, `X1`, `X2`),
`MouseWheel` (`Up`, `Down`), `Macro`, `Special` (`ProfileNext`, `ProfilePrevious`, `ToggleGyro`,
`BatteryFlash`, `ToggleMapping`, `StopEffects`).

Ejemplos reales: carpeta `community/packages/`.
