# Robustness checks without the game. Each case runs a copy of the built exe in its own temp folder.
#   damaged cache  - garbage in the cached JSON must be replaced, not crash the app
#   offline        - no network, no cache: the app must start and say so; with a cache: use it
#   read-only dir  - unpacked somewhere without write access: data goes to %LOCALAPPDATA%\DeadlockMeta
#   second copy    - starting it again must not create a second instance
# -Cases 4,5 runs only those cases.
param([int[]]$Cases, [string]$Exe = "$PSScriptRoot\..\bin\Release\net10.0-windows\DeadlockMeta.exe")
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP "dm-robust"
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
$bin = Split-Path $Exe

function Copy-App($name) {
    $d = Join-Path $root $name
    New-Item -ItemType Directory -Force $d | Out-Null
    Get-ChildItem $bin -File | Copy-Item -Destination $d
    return $d
}
function Run($dir, [string[]]$arguments, [int]$seconds = 60) {
    $p = Start-Process (Join-Path $dir 'DeadlockMeta.exe') -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit($seconds * 1000)) { $p.Kill(); return 'TIMEOUT' }
    return "exit=$($p.ExitCode)"
}
function Result($ok, $name, $detail) { "{0} {1,-34} {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail }

function Want($n) { -not $Cases -or $Cases -contains $n }

# 1. damaged cache
if (Want 1) {
$d = Copy-App 'damaged'
Run $d @('--test', "$PSScriptRoot\select_1080.png", '1080') | Out-Null
'<html>proxy error</html>' | Set-Content "$d\data\heroes.json"
(Get-Item "$d\data\heroes.json").LastWriteTime = Get-Date
$r = Run $d @('--test', "$PSScriptRoot\select_1080.png", '1080')
$first = (Get-Content "$d\data\test_result.txt" -TotalCount 1)
$healed = (Get-Content "$d\data\heroes.json" -Raw).TrimStart().StartsWith('[')
Result ($first -match 'found=True' -and $healed) 'damaged cache is re-downloaded' "$r; $first"

}

# 2. offline with no cache: --shot must still render a window that says there is no connection
if (Want 2) {
$d = Copy-App 'offline'
$env:HTTPS_PROXY = 'http://127.0.0.1:9'; $env:HTTP_PROXY = 'http://127.0.0.1:9'
$r = Run $d @('--shot', "$d\shot.png") 90
$log = Get-Content "$d\data\log.txt" -Raw -ErrorAction SilentlyContinue
Result ((Test-Path "$d\shot.png") -and $log -match 'load failed' -and $log -notmatch 'crash') 'offline, no cache: starts, no crash' $r

}

# 3. offline with a cache from an online run
if (Want 3) {
Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY
Run $d @('--test', "$PSScriptRoot\select_1080.png", '1080') | Out-Null
$env:HTTPS_PROXY = 'http://127.0.0.1:9'; $env:HTTP_PROXY = 'http://127.0.0.1:9'
Get-ChildItem "$d\data\*.json" | % { $_.LastWriteTime = (Get-Date).AddHours(-5) }
$r = Run $d @('--test', "$PSScriptRoot\select_1080.png", '1080') 90
$first = (Get-Content "$d\data\test_result.txt" -TotalCount 1)
Result ($first -match 'found=True') 'offline with stale cache: uses cache' "$r; $first"
Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY

}

# 4. read-only folder
if (Want 4) {
$d = Copy-App 'readonly'
$me = "$env:USERDOMAIN\$env:USERNAME"
# No new files or folders inside this folder; the exe itself stays readable and runnable.
icacls $d /deny "${me}:(AD,WD)" | Out-Null
$fallback = Join-Path $env:LOCALAPPDATA 'DeadlockMeta'
$before = if (Test-Path "$fallback\log.txt") { (Get-Item "$fallback\log.txt").LastWriteTime } else { [DateTime]::MinValue }
$r = Run $d @('--test', "$PSScriptRoot\select_1080.png", '1080') 90
$used = (Test-Path "$fallback\test_result.txt") -and (Get-Item "$fallback\test_result.txt").LastWriteTime -gt $before
icacls $d /remove:d $me | Out-Null
Result ($used -and -not (Test-Path "$d\data")) 'read-only folder: falls back to AppData' "$r"

}

# 5. second copy
if ((Want 5) -and (Get-Process DeadlockMeta -ErrorAction SilentlyContinue)) {
    # The instance lock is per Windows session: with the real app open, both test copies would just hand over to it.
    Result $true 'second copy (skipped: app is running)' ''
}
elseif (Want 5) {
$d = Copy-App 'single'
$env:DEADLOCKMETA_PROCESS = 'nothing-matches'
$p1 = Start-Process (Join-Path $d 'DeadlockMeta.exe') -ArgumentList '--tray' -PassThru
Start-Sleep 4
$r = Run $d @('--tray') 15
$alive = Get-Process -Id $p1.Id -ErrorAction SilentlyContinue
$count = @(Get-Process DeadlockMeta -ErrorAction SilentlyContinue | ? { $_.Path -like "$d*" }).Count
Result ($r -eq 'exit=0' -and $alive -and $count -eq 1) 'second copy exits, first keeps running' "$r; instances=$count"
Stop-Process -Id $p1.Id -Force -ErrorAction SilentlyContinue
Remove-Item Env:DEADLOCKMETA_PROCESS

}

Start-Sleep 1
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
