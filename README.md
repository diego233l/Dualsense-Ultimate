# DualSense Ultimate

Aplicación de escritorio para **Windows 10 y Windows 11** que reúne en un solo programa lo que hoy
obliga a combinar Steam Input, DS4Windows, DSX y pydualsense: iluminación, gatillos adaptativos,
háptica, remapeo, macros, giroscopio, emulación de mandos, perfiles por juego, automatizaciones,
plugins y una API local. Compatible con **DualSense**, **DualSense Edge** y **DualShock 4**, por USB y
Bluetooth, con varios mandos a la vez.

![Dashboard](docs/screenshots/01-dashboard.png)

> **Estado de validación (léelo antes de usarla).** El proyecto se ha desarrollado sin acceso a un
> mando físico ni a un escritorio Windows interactivo. Toda la lógica está cubierta por 137 pruebas
> automatizadas que se ejecutan en Windows y Linux contra mandos simulados que hablan el protocolo
> real, y un usuario ha confirmado en un DualSense real que iluminación, LED de micrófono y gatillos adaptativos funcionan. El resto de funciones (emulación, audio háptico, aislamiento, instalación de actualizaciones…) sigue sin validar en hardware. `FEATURES.md` indica el
> estado exacto de cada función y `docs/HARDWARE_TEST_PLAN.md` cómo validarla en tu equipo.

## Instalación

1. Descarga `DualSenseUltimate-Setup-x.y.z.exe` de *Releases* (o del artefacto `DualSenseUltimate-Setup`
   de la última ejecución de GitHub Actions).
2. Ejecútalo. La instalación es por usuario y **no necesita permisos de administrador**.
3. Opcional, solo si quieres emular un mando Xbox o DualShock 4: marca **ViGEmBus** y **HidHide** en
   el instalador (se descargan de sus repositorios oficiales y sí piden permisos de administrador).
4. Conecta el mando por USB o emparéjalo por Bluetooth en Windows. Aparecerá solo en el Dashboard.

Versión portable: descomprime el artefacto `DualSenseUltimate-portable` y ejecuta `DualSenseUltimate.exe`.
No requiere instalar .NET.

Desinstalación: desde «Aplicaciones instaladas». El desinstalador vuelve a mostrar cualquier mando
ocultado, quita el inicio automático y pregunta si quieres conservar tus perfiles.

## Uso rápido

| Sección | Para qué |
|---|---|
| Dashboard | Mandos, batería, perfil activo y dibujo interactivo del mando |
| Iluminación RGB | Barra de luz (9 efectos + editor), LEDs de jugador, LED del micrófono |
| Adaptive Triggers | Editor visual de L2/R2 con todos los efectos del protocolo |
| Háptica | Vibración, patrones, reactivo al sonido, audio háptico por USB |
| Remapeo | Botones, macros, combinaciones, touchpad, emulación y modo híbrido |
| Joysticks y giroscopio | Zonas muertas, curvas, calibración, giroscopio como ratón o stick |
| Perfiles | Globales, por mando y automáticos por ejecutable |
| Biblioteca comunitaria | Descarga perfiles y efectos compartidos |
| Automatizaciones | Reglas «si… entonces…» |
| Plugins / API | Integraciones (Home Assistant, scripts, juegos) |

Manual completo: [docs/USER_MANUAL.md](docs/USER_MANUAL.md).

## Compilar desde el código

Requisitos: .NET SDK 10. Para el instalador, Inno Setup 6 (solo Windows).

```powershell
dotnet build
dotnet test
dotnet run --project src/DualSenseUltimate.App      # ejecutar
pwsh scripts/publish.ps1 -Version 1.0.0             # build autocontenida + instalador
dotnet run --project tools/HardwareCheck            # validación guiada con un mando real
```

## Documentación

- [ARCHITECTURE.md](ARCHITECTURE.md) · [FEATURES.md](FEATURES.md) · [TESTING.md](TESTING.md) ·
  [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · [ROADMAP.md](ROADMAP.md) · [CHANGELOG.md](CHANGELOG.md) ·
  [CONTRIBUTING.md](CONTRIBUTING.md)
- [docs/RESEARCH.md](docs/RESEARCH.md): investigación de protocolos y viabilidad
- [docs/API.md](docs/API.md) · [docs/PLUGINS.md](docs/PLUGINS.md) · [docs/PROFILE_FORMAT.md](docs/PROFILE_FORMAT.md)

## Licencia

MIT. Componentes de terceros en [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Proyecto
independiente, sin relación con Sony Interactive Entertainment.
