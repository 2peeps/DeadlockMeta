# Builds the app into app\ (one self-contained exe: no .NET install needed to run it)
# (or into -Out <folder>, e.g. while app\ is running) and packs release\DeadlockMeta-<version>.zip to share.
param([string]$Out = 'app')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet publish -c Release -r win-x64 --self-contained true -o $Out --nologo -v q `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$version = ([xml](Get-Content DeadlockMeta.csproj)).Project.PropertyGroup.Version
$stage = "release\DeadlockMeta"
Remove-Item release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "$Out\DeadlockMeta.exe" $stage
Copy-Item docs\INSTRUCTION_RU.txt "$stage\ИНСТРУКЦИЯ.txt"
$zip = "release\DeadlockMeta-$version.zip"
Compress-Archive -Path $stage -DestinationPath $zip
"{0}: {1:N0} MB" -f $zip, ((Get-Item $zip).Length / 1MB)
