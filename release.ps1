# Выпуск новой версии: .\release.ps1 1.1.0 "Что нового"
# Меняет версию, гоняет тесты, собирает exe и установщик, делает коммит и тег,
# выкладывает релиз на GitHub. Программы у пользователей увидят его при запуске.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Notes = "",
    [string]$Label = "бета"   # пометка в названии релиза; пустая строка — без пометки
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$dotnet = "D:\Program Files\dotnet\dotnet.exe"
$env:NUGET_PACKAGES = "D:\Program Files\NuGet\packages"

if (git status --porcelain) { throw "Есть незакоммиченные изменения — сначала закоммитьте их" }
if ((git branch --show-current) -ne "main") { throw "Выпуск только из ветки main" }
if (git tag -l "v$Version") { throw "Тег v$Version уже есть" }

$csproj = "src\Cleaner.App\Cleaner.App.csproj"
$text = [IO.File]::ReadAllText($csproj)
[IO.File]::WriteAllText($csproj, ($text -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"))

& $dotnet test tests\Cleaner.Core.Tests --nologo -v q
if ($LASTEXITCODE -ne 0) { git checkout -- $csproj; throw "Тесты не прошли" }

& "$PSScriptRoot\publish.ps1"

git add $csproj
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { git commit -m "release: v$Version" }  # версия могла уже совпадать
git tag "v$Version"
git push origin main "v$Version"
if ($LASTEXITCODE -ne 0) { throw "git push не удался" }

$setup = "dist\PcCleaner-Setup-$Version.exe"
$title = if ($Label) { "PcCleaner $Version ($Label)" } else { "PcCleaner $Version" }
if (-not $Notes) { $Notes = $title }
if ($Label -eq "бета") { $Notes = "Бета-версия. $Notes" }
# Не --prerelease: /releases/latest пропускает пре-релизы, и программы не увидели бы обновление.
gh release create "v$Version" $setup --title $title --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "gh release не удался" }
Write-Host "Готово: https://github.com/Pashacod/PcCleaner/releases/tag/v$Version"
