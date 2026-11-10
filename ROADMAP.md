# Roadmap

| Fase | Contenido | Estado |
|---|---|---|
| 1. Investigación y arquitectura | Protocolos, viabilidad, tecnologías (`docs/RESEARCH.md`, `ARCHITECTURE.md`) | Completada |
| 2. Núcleo y detección | Dispositivos, USB/BT, batería, identificación, interfaz base | Completada (SIM + HW) |
| 3. Personalización | RGB, LEDs, gatillos, vibración, háptica | Completada (SIM + HW) |
| 4. Input avanzado | Remapeo, macros, touchpad, sticks, giroscopio, calibración | Completada (SIM + HW) |
| 5. Emulación | ViGEm, HidHide, modo híbrido | Completada (SIM + HW) |
| 6. Automatización | Perfiles por juego, reglas | Completada (SIM + HW) |
| 7. Ecosistema | API, plugins, biblioteca, importación/exportación | Completada (SIM + HW; con las acotaciones de KNOWN_ISSUES) |
| 8. Estabilización | Pruebas, instalador, CI, documentación y hardware | Completada |

## Validación completada

El usuario confirma que las funciones implementadas se validaron correctamente en hardware el 10 de
noviembre de 2026. El informe no incluye la matriz de dispositivos y entornos; ver `TESTING.md`.

## Después

- Notificaciones de Windows mediante empaquetado MSIX opcional.
- Háptica por Bluetooth cuando el formato esté documentado de forma fiable.
- Perfiles del DualSense Edge guardados en el mando.
- Firma de código del instalador.
- Traducción al inglés de la interfaz.
