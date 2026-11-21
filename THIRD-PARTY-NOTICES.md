# Componentes de terceros

TuringLite distribuye estas bibliotecas junto a su ejecutable. Cada una conserva su licencia; el código fuente de las
que usan MPL-2.0 está disponible en su página.

| Componente | Versión | Licencia | Código fuente |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | 2.6.4 | Apache-2.0 (Copyright 2010-2025 James F. Bellinger) | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.CodeDom, System.IO.Ports, System.Management, System.Threading.AccessControl | 10.0.x | MIT (Microsoft) | https://github.com/dotnet/runtime |

Textos de las licencias:

- MPL-2.0: https://www.mozilla.org/MPL/2.0/
- Apache-2.0: https://www.apache.org/licenses/LICENSE-2.0
- MIT: https://opensource.org/license/mit

TuringLite no modifica ninguno de estos componentes.

## Protocolo de la pantalla

El cliente del protocolo serie de la pantalla (`src/RevADisplay.cs`) se basa en
[turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python) de Matthieu Houdebine,
publicado con licencia GPL-3.0. Por eso TuringLite se publica también con GPL-3.0 (ver `LICENSE`).

## Baterías de periféricos

La lectura de batería de la base ASTRO A50 Gen 5 (`src/Batteries.cs`) sigue el protocolo documentado por
[HeadsetControl](https://github.com/Sapd/HeadsetControl) (GPL-3.0). La de los dispositivos Logitech usa el protocolo
HID++ 2.0 de Logitech, tal como lo describen proyectos libres como [Solaar](https://github.com/pwr-Solaar/Solaar).
