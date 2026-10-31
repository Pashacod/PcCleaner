# Сборка: dist\PcCleaner.exe (.NET внутри) и установщик dist\PcCleaner-Setup-<версия>.exe.
$ErrorActionPreference = "Stop"
$dotnet = "D:\Program Files\dotnet\dotnet.exe"
$iscc = "D:\Program Files\Inno Setup 7\ISCC.exe"
$env:NUGET_PACKAGES = "D:\Program Files\NuGet\packages"

$version = ([xml](Get-Content "$PSScriptRoot\src\Cleaner.App\Cleaner.App.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

& $dotnet publish "$PSScriptRoot\src\Cleaner.App" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:EnableCompressionInSingleFile=true `
    -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

& $iscc /Q "/DAppVersion=$version" "$PSScriptRoot\installer\PcCleaner.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-ChildItem "$PSScriptRoot\dist\PcCleaner*.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
