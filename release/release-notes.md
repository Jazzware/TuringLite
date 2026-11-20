TuringLite: monitor de hardware para la pantalla USB Turing Smart Screen de 3,5". Los cambios de cada versión están en [CHANGELOG.md](https://github.com/Jazzware/TuringLite/blob/main/CHANGELOG.md).

**Requisito:** [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0). El instalador comprueba que esté.

**Instalador sin firmar.** No tiene firma digital:
- Windows SmartScreen avisará al ejecutarlo: **Más información → Ejecutar de todas formas**.
- Si tienes **Smart App Control** activado, Windows lo bloquea y no ofrece ejecutarlo.
- Antivirus que puntúan por reputación (p. ej. Norton) pueden marcarlo por ser nuevo; si confías en él, exclúyelo.

Para actualizar, ejecuta el instalador nuevo: se instala encima y conserva tu configuración.

Comprueba la descarga con `SHA256SUMS.txt`:

```
Get-FileHash .\TuringLite-Setup-<versión>.exe -Algorithm SHA256
```
