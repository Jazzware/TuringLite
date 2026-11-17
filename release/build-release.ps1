# Genera el instalador de TuringLite para publicar.
#   dist\TuringLite-Setup-<versión>.exe   instalador de un solo archivo (.NET Framework 4.8, incluido en Windows;
#                                          comprueba que el equipo tenga el .NET 8 Desktop Runtime que usa la app)
#   dist\SHA256SUMS.txt                   sumas de comprobación del instalador
#
# Uso local (todo de una vez):
#   powershell -ExecutionPolicy Bypass -File release\build-release.ps1
# Por etapas, para firmar entre medias (lo hace .github\workflows\release.yml):
#   -Stage App     publica la app en dist\app (aquí se firman TuringLite.exe y TuringLite.dll)
#   -Stage Setup   empaqueta dist\app tal como esté y compila el instalador (después se firma el instalador)
param([ValidateSet("All", "App", "Setup")][string]$Stage = "All")
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$version = ([xml](Get-Content "TuringLite.csproj")).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $root "dist"
$app = Join-Path $dist "app"
$payload = Join-Path $dist "payload.zip"

if ($Stage -in "All", "App") {
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist | Out-Null

    Write-Host "1/4 Publicando la aplicación $version..."
    dotnet publish TuringLite.csproj -c Release -r win-x64 --self-contained false -o $app
    if ($LASTEXITCODE -ne 0) { throw "La publicación de la aplicación falló." }
    # El paquete no lleva símbolos de depuración ni datos del equipo de desarrollo.
    Remove-Item (Join-Path $app "*.pdb") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $app "status.txt") -ErrorAction SilentlyContinue
}

if ($Stage -in "All", "Setup") {
    if (-not (Test-Path (Join-Path $app "TuringLite.exe"))) { throw "Falta dist\app: ejecuta antes la etapa App." }

    Write-Host "2/4 Empaquetando..."
    Remove-Item $payload -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($app, $payload)

    Write-Host "3/4 Compilando el instalador (incluye el paquete)..."
    # .NET Framework: el .exe compilado ya es un solo archivo (el paquete va dentro como recurso).
    $setupBuild = Join-Path $dist "setup-build"
    dotnet build setup\TuringLite.Setup.csproj -c Release -o $setupBuild
    if ($LASTEXITCODE -ne 0) { throw "La compilación del instalador falló." }
    $setup = Join-Path $dist "TuringLite-Setup-$version.exe"
    Move-Item (Join-Path $setupBuild "TuringLite-Setup.exe") $setup -Force
    Remove-Item $setupBuild -Recurse -Force

    Write-Host "4/4 Limpiando y calculando sumas..."
    Remove-Item $app -Recurse -Force
    Remove-Item $payload -Force
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $setup)" | Set-Content -Path (Join-Path $dist "SHA256SUMS.txt") -Encoding ascii
    Write-Host ("Listo: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
    Write-Host "SHA-256: $hash"
}
