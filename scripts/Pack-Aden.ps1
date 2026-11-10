param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('aden-build-' + [guid]::NewGuid().ToString('N'))
$tools = Join-Path $stage 'tools'
$app = Join-Path $stage 'app'
dotnet tool install vpk --version 1.2.161 --tool-path $tools
if ($LASTEXITCODE -ne 0) { throw 'Velopack kurulamadı.' }
dotnet publish (Join-Path $repo 'muhasebe/Aden.Desktop.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $app
if ($LASTEXITCODE -ne 0) { throw 'Aden derlenemedi.' }
if (Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object { $_.Name -match '\.(db|sqlite|sqlite3)(-|$)' -or $_.Name -eq 'portable.txt' }) {
    throw 'Kurulum paketinde veritabanı veya demo işareti bulundu.'
}

& (Join-Path $tools 'vpk.exe') pack --packId Aden.Windows --packVersion $Version --packTitle Aden --packAuthors Aden `
    --channel win-x64 --runtime win-x64 --mainExe Aden.Desktop.exe --packDir $app --outputDir $destination `
    --delta None --icon (Join-Path $repo 'muhasebe/Assets/aden-icon-logo.ico') --shortcuts 'Desktop,StartMenuRoot'
if ($LASTEXITCODE -ne 0) { throw 'Aden kurulum paketi oluşturulamadı.' }
