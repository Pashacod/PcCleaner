# РЎР±РѕСЂРєР° РѕРґРЅРѕРіРѕ PcCleaner.exe СЃРѕ РІСЃС‚СЂРѕРµРЅРЅС‹Рј .NET РІ РїР°РїРєСѓ dist.
$ErrorActionPreference = "Stop"
$dotnet = "D:\Program Files\dotnet\dotnet.exe"
$env:NUGET_PACKAGES = "D:\Program Files\NuGet\packages"
& $dotnet publish "$PSScriptRoot\src\Cleaner.App" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:EnableCompressionInSingleFile=true `
    -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-Item "$PSScriptRoot\dist\PcCleaner.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
