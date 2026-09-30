# Buduje aplikacje (Skills/*.md trafiaja do katalogu wyjsciowego przez csproj) i usuwa
# z .skills/ w katalogu notatek kopie identyczne ze skillami z repo - inaczej przykrywalyby
# wersje z repo i zmiany w Skills/ nie dzialalyby.
# Uzycie: .\build.ps1   (.\build.ps1 -NotesRoot D:\inna\sciezka gdy Storage:NotesRootPath jest ustawione)
# Gdy Windows blokuje skrypty: powershell -ExecutionPolicy Bypass -File .\build.ps1
param([string]$NotesRoot = (Join-Path $env:USERPROFILE 'SecondBrain\notes'))
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet build SecondBrain.slnx -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$userSkills = Join-Path $NotesRoot '.skills'
if (-not (Test-Path $userSkills)) { exit 0 }

foreach ($repoSkill in Get-ChildItem 'SecondBrain.Desktop\Skills\*.md') {
    $userSkill = Join-Path $userSkills $repoSkill.Name
    if (-not (Test-Path $userSkill)) { continue }
    if ((Get-FileHash $repoSkill.FullName).Hash -eq (Get-FileHash $userSkill).Hash) {
        Remove-Item $userSkill
        Write-Host "Usunieto duplikat: $userSkill"
    } else {
        Write-Host "UWAGA: $userSkill rozni sie od wersji z repo i ja nadpisuje - usun recznie, jesli chcesz wersje z repo."
    }
}
