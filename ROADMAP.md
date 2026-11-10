# Roadmap

| Fase | Contenido | Estado |
|---|---|---|
| 1. Investigación y arquitectura | Protocolos, viabilidad, tecnologías (`docs/RESEARCH.md`, `ARCHITECTURE.md`) | Completada |
| 2. Núcleo y detección | Dispositivos, USB/BT, batería, identificación, interfaz base | Completada (SIM) |
| 3. Personalización | RGB, LEDs, gatillos, vibración, háptica | Completada (SIM / PEND audio) |
| 4. Input avanzado | Remapeo, macros, touchpad, sticks, giroscopio, calibración | Completada (SIM) |
| 5. Emulación | ViGEm, HidHide, modo híbrido | Implementada (PEND drivers) |
| 6. Automatización | Perfiles por juego, reglas | Completada (SIM) |
| 7. Ecosistema | API, plugins, biblioteca, importación/exportación | Completada (con las acotaciones de KNOWN_ISSUES) |
| 8. Estabilización | Pruebas, instalador, CI, documentación | Completada salvo validación física |

## Siguiente paso obligatorio

**Validación en hardware** (`docs/HARDWARE_TEST_PLAN.md`): DualSense USB, DualSense Bluetooth,
DualSense Edge, DualShock 4, ViGEmBus + HidHide, Steam, instalador. Cada resultado actualiza
`FEATURES.md` (PEND → HW) y `TESTING.md`.

## Después

- Notificaciones de Windows mediante empaquetado MSIX opcional.
- Háptica por Bluetooth cuando el formato esté documentado de forma fiable.
- Perfiles del DualSense Edge guardados en el mando.
- Firma de código del instalador.
- Traducción al inglés de la interfaz.
