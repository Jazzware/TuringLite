# TuringLite

Monitor de hardware para la mini pantalla USB Turing Smart Screen de 3,5". Muestra temperaturas, cargas, potencia y tu propio texto, con temas visuales.

## Requisitos

- Windows 10 (1809 o posterior) o Windows 11.
- **.NET 8 Desktop Runtime** (x64) para la app: <https://dotnet.microsoft.com/download/dotnet/8.0>. El instalador no lo necesita (usa .NET Framework 4.8, que ya viene con Windows) y comprueba que esté: si falta, no instala nada y te ofrece esa página. No lo descarga ni lo instala por ti.
- Para la temperatura de CPU: el servicio de sensores (ver abajo).

## Instalación

1. Descarga `TuringLite-Setup-1.0.0.exe` de [Releases](https://github.com/Jazzware/TuringLite/releases) y ejecútalo. Se instala para tu usuario, en `%LOCALAPPDATA%\Programs\TuringLite`.
2. Elige las opciones:
   - **Acceso directo** en el menú Inicio.
   - **Iniciar con Windows**: entrada de tu usuario en el registro, sin privilegios.
   - **Servicio de sensores** (recomendado): lee la temperatura de CPU al arrancar el equipo. Pide permiso de administrador una sola vez.
3. Conecta la pantalla por USB. TuringLite la encuentra sola.

## Servicio de sensores

La temperatura y la potencia de la CPU solo se pueden leer con privilegios de administrador. Para no dar esos privilegios a la app, el servicio `TuringLiteSensors` lee los sensores como sistema y los comparte con la app por una tubería local de solo lectura. La app sigue sin privilegios.

- El servicio se copia a `%ProgramFiles%\TuringLite`, donde solo un administrador puede modificarlo.
- Sin servicio, la app funciona igual, pero la temperatura de CPU aparece como `--`.
- Puedes instalarlo o quitarlo desde la ventana de TuringLite: **Instalar / Quitar servicio de sensores**.

## Desinstalación

Desde **Configuración → Aplicaciones instaladas → TuringLite → Desinstalar**, o ejecutando `TuringLite.exe --uninstall`.

Se quita:
- El programa (`%LOCALAPPDATA%\Programs\TuringLite`) y su acceso directo del menú Inicio.
- El inicio con Windows y la entrada de Aplicaciones instaladas.
- El servicio de sensores y su copia en `Program Files` (si lo instalaste; pide administrador).
- Las huellas de Windows del usuario que apuntan a TuringLite (historial de ejecución, caché de nombres, mosaicos del menú Inicio).
- Las huellas del sistema `Prefetch` de TuringLite (pide administrador en el mismo aviso UAC del servicio).

Tu configuración (`config.json`) se borra salvo que marques **Conservar mi configuración**: en ese caso se guarda en `Documentos\TuringLite`.

Un desinstalador nunca toca una copia de desarrollo ni otra instalación: solo borra lo que instaló el instalador (comprueba `install.marker`).

Para desinstalar sin ventana: `TuringLite.exe --uninstall --silent` (opciones: `--keep-config`, `--keep-service`, `--keep-traces`).

## Privacidad

TuringLite no envía datos a ningún sitio. Lee sensores del equipo y los muestra en la pantalla. Sus archivos quedan en su carpeta de instalación.

## Compilar el instalador

```
powershell -ExecutionPolicy Bypass -File release\build-release.ps1
```

Genera `dist\TuringLite-Setup-<versión>.exe` (sin firmar) y `dist\SHA256SUMS.txt`. Requiere el SDK de .NET 8.
Con `-Stage App` y `-Stage Setup` se hace por partes, para firmar la app antes de meterla en el instalador.

Las versiones publicadas se compilan en GitHub Actions (`.github/workflows/release.yml`) al subir una etiqueta `vX.Y.Z`.

## Licencia

TuringLite es software libre con licencia [GPL-3.0](LICENSE): puedes usarlo, estudiarlo, modificarlo y
redistribuirlo, y quien distribuya una versión modificada debe publicar su código con la misma licencia.
El protocolo de la pantalla se basa en [turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python) (GPL-3.0).
Las bibliotecas de terceros y sus licencias están en `THIRD-PARTY-NOTICES.md`.

## Firma de código

La versión 1.0.0 **no está firmada**. Estamos tramitando un certificado de firma de código para las próximas versiones.

Cuando esté:
- Solo se firmarán los archivos de este proyecto: `TuringLite.exe`, `TuringLite.dll` y el instalador. Las bibliotecas de terceros se distribuyen tal como las publican sus autores (ver `THIRD-PARTY-NOTICES.md`).
- Cada versión publicada incluirá `SHA256SUMS.txt` para comprobar la descarga.

## Limitaciones conocidas (1.0)

- El ejecutable no está firmado digitalmente: Windows SmartScreen puede mostrar un aviso al ejecutarlo por primera vez.
- Por lo mismo, en equipos con **Smart App Control** activado Windows bloquea el instalador y no ofrece ejecutarlo igualmente. Solo se resuelve firmando el instalador y la app.
- Solo se ha probado con un Ryzen 9 7900X3D. Otros procesadores (Intel u otro AMD) pueden mostrar sensores distintos.
- La pantalla física no se ha podido comprobar en todos los equipos.
