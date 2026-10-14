# Publishes the project to GitHub under the signed-in gh account:
# creates the public repo on first run, pushes main, and uploads release\DeadlockMeta-<version>.zip as a release.
# Commit author is switched to the account's private noreply address so no personal e-mail ends up public.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$gh = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'
$git = Join-Path $env:ProgramFiles 'Git\cmd\git.exe'
$repo = 'DeadlockMeta'

& $gh auth status
if ($LASTEXITCODE -ne 0) { throw 'Not signed in: run gh auth login first' }
$id = & $gh api user --jq .id
$login = & $gh api user --jq .login
& $gh auth setup-git

& $git config user.name $login
& $git config user.email "$id+$login@users.noreply.github.com"
$remoteExists = (& $git remote) -contains 'origin'
if (-not $remoteExists) {
    # Nothing has been pushed yet, so the local-only author of the existing commits can still be replaced.
    & $git rebase --root --exec 'git commit --amend --no-edit --reset-author --quiet' --quiet
    & $gh repo create $repo --public --source . --remote origin --push `
        --description 'Deadlock hero winrates in a window and on the hero select screen (overlay)'
} else {
    & $git push origin main
}
if ($LASTEXITCODE -ne 0) { throw 'push failed' }

$version = ([xml](Get-Content DeadlockMeta.csproj)).Project.PropertyGroup.Version
$zip = "release\DeadlockMeta-$version.zip"
if (-not (Test-Path $zip)) { throw "$zip not found: run publish.ps1 first" }
& $gh release create "v$version" $zip --title "Deadlock Meta $version" --notes-file "docs\release-notes\$version.md"
if ($LASTEXITCODE -ne 0) { throw 'release failed' }
"Release: https://github.com/2peeps/DeadlockMeta/releases/tag/v$version"
