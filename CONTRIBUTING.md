# Contribuir

## Entorno

- .NET SDK 10. Funciona en Windows y Linux (en Linux solo HID y el simulador; el resto de servicios
  de plataforma se informan como no disponibles).
- `dotnet build && dotnet test` debe quedar en verde antes de abrir un pull request.
- `dotnet run --project tools/Screenshots -- salida/` renderiza todas las páginas; falla si alguna
  lanza una excepción.

## Normas

- La lógica va en `DualSenseUltimate.Core`, sin dependencias del sistema operativo y con pruebas.
  Lo específico de Windows va en `DualSenseUltimate.Platform` detrás de una interfaz del núcleo.
- Todo acceso al hardware pasa por `ControllerSession`; no escribas informes HID desde otros módulos.
- Cualquier dato que venga de fuera (archivo, red, API) se valida con `ProfileValidator`.
- No marques una función como probada en hardware sin un informe de `tools/HardwareCheck` o una
  descripción reproducible (modelo, conexión, firmware, versión de Windows). Actualiza `FEATURES.md`
  y `TESTING.md`.
- Mensajes de interfaz en español; código y comentarios en inglés.

## Validar en hardware

Sigue `docs/HARDWARE_TEST_PLAN.md` y adjunta el informe generado al pull request o a una incidencia.

## Biblioteca comunitaria

Consulta `community/README.md`.
