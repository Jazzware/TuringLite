Primera versión de TuringLite: monitor de hardware para la pantalla USB Turing Smart Screen de 3,5". Los cambios están en [CHANGELOG.md](https://github.com/Jazzware/TuringLite/blob/main/CHANGELOG.md).

**Requisito:** [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0). El instalador comprueba que esté.

**Instalador sin firmar.** Esta versión no tiene firma digital:
- Windows SmartScreen avisará al ejecutarlo: **Más información → Ejecutar de todas formas**.
- Si tienes **Smart App Control** activado, Windows lo bloquea y no ofrece ejecutarlo. Estamos tramitando un certificado de firma de código para las próximas versiones.

Comprueba la descarga con `SHA256SUMS.txt`:

```
Get-FileHash .\TuringLite-Setup-1.0.0.exe -Algorithm SHA256
```
