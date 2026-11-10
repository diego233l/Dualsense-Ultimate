# Avisos de terceros

DualSense Ultimate no está afiliado a Sony Interactive Entertainment. «PlayStation», «DualSense» y
«DualShock» son marcas registradas de Sony Interactive Entertainment Inc.

## Componentes incluidos en el binario

| Componente | Licencia | Uso |
|---|---|---|
| .NET 10 runtime | MIT | Plataforma |
| Avalonia UI 11 (+ Skia, HarfBuzz, Inter font) | MIT (Inter: SIL OFL 1.1) | Interfaz gráfica |
| HidSharp 2.6 | Apache-2.0 | Acceso HID |
| Nefarius.ViGEm.Client 1.21 | MIT | Cliente del bus ViGEm (mandos virtuales) |
| NAudio 2.2 | MIT | WASAPI (captura y audio háptico) |
| ASP.NET Core (Kestrel) | MIT | API local |

## Controladores opcionales (no incluidos; el instalador puede descargarlos de su fuente oficial)

| Componente | Licencia | Fuente |
|---|---|---|
| ViGEmBus 1.22 | BSD-3-Clause | https://github.com/nefarius/ViGEmBus |
| HidHide 1.5 | MIT | https://github.com/nefarius/HidHide |

## Documentación de protocolo consultada (no se ha copiado código)

- Driver `hid-playstation` del kernel Linux (GPL-2.0): estructura de los informes HID. Solo se han
  usado los hechos del protocolo (offsets, tamaños, semillas CRC); la implementación es propia.
- `dualsensectl` (GPL-2.0) y la «DualSense trigger effect factory» de Nielk1 (MIT): codificación de
  los efectos de gatillo.
- DS4Windows (GPL-3.0), pydualsense (MIT): referencia de comportamiento.
