# API local

- Dirección: `http://127.0.0.1:26760/api/v1` (puerto configurable en la sección API).
- Autenticación: cabecera `Authorization: Bearer <token>` (o `?token=` para clientes SSE que no
  permiten cabeceras). El token se muestra en la aplicación. Sin token: `401`.
- Solo escucha en la interfaz de bucle local y no envía cabeceras CORS.
- Cuerpos y respuestas en JSON. `{id}` puede ser la MAC del mando, `first` o un índice (`0`, `1`…).

| Método y ruta | Cuerpo | Descripción |
|---|---|---|
| `GET /ping` | — | Comprobación (no requiere token) |
| `GET /controllers` | — | Lista de mandos |
| `GET /controllers/{id}` | — | Modelo, conexión, firmware, batería, perfil, emulación, color actual |
| `GET /controllers/{id}/battery` | — | `{ percent, status }` |
| `GET /controllers/{id}/input` | — | Botones, sticks, gatillos, giroscopio, acelerómetro, touchpad |
| `POST /controllers/{id}/lightbar` | `{ "color": "#RRGGBB" }` o `{ "r", "g", "b" }`, `durationMs` opcional (0 = hasta `reset`) | Color temporal de la barra |
| `POST /controllers/{id}/rumble` | `{ "left": 0-255, "right": 0-255, "durationMs": 500 }` | Vibración |
| `POST /controllers/{id}/triggers` | `{ "side": "L2"\|"R2"\|"both", "preset": "Pistola" }` o `{ "side", "effect": {…} }` | Efecto de gatillo temporal |
| `POST /controllers/{id}/haptics` | `{ "pattern": "Latido" }` o `{ "custom": { "steps": [ { "durationMs", "left", "right" } ], "repeat": 1 } }` | Patrón háptico |
| `POST /controllers/{id}/profile` | `{ "profile": "nombre o id" }` | Asigna un perfil |
| `POST /controllers/{id}/reset` | `{}` | Detiene efectos y elimina ajustes temporales |
| `GET /profiles` | — | Perfiles |
| `GET /trigger-presets`, `GET /haptic-patterns` | — | Nombres disponibles |
| `POST /notify` | `{ "title": "texto" }` | Evento de notificación (destello, reglas) |
| `GET /events` | — | Server-Sent Events |

Objeto `effect` (ver `docs/PROFILE_FORMAT.md`):
`{ "type": "Weapon", "start": 2, "end": 5, "strength": 8 }`. Tipos: `Off`, `Feedback`,
`SlopeFeedback`, `MultiZoneFeedback`, `Weapon`, `Bow`, `Vibration`, `MultiZoneVibration`,
`Galloping`, `Machine`, `Raw`. Los valores fuera de rango se ajustan al límite más cercano.

Eventos (`GET /events`), uno por línea `data:`:
`controller.connected`, `controller.disconnected`, `controller.health`, `battery.changed`
(`detail`: `"45/Discharging"`), `profile.applied`, `process.started`, `process.exited`, `notification`.

Errores: `400` (petición inválida, con `error`), `401`, `404` (mando no encontrado).

Clientes de ejemplo: `examples/python/dsu_client.py` y `examples/javascript/dsu-client.mjs`.

## Integración con juegos

Para efectos dinámicos (munición, daño, velocidad), un mod o una herramienta de telemetría puede
llamar a `/triggers`, `/rumble` y `/lightbar` con cada cambio de estado. Los ajustes enviados por
API son temporales: se superponen al perfil y desaparecen con `/reset` o al cambiar de perfil.
