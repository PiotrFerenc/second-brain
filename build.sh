#!/usr/bin/env bash
# Buduje aplikacje (Skills/*.md trafiaja do katalogu wyjsciowego przez csproj) i usuwa
# z .skills/ w katalogu notatek kopie identyczne ze skillami z repo - inaczej przykrywalyby
# wersje z repo i zmiany w Skills/ nie dzialalyby.
# Uzycie: ./build.sh   (NOTES_ROOT=/inna/sciezka ./build.sh gdy Storage:NotesRootPath jest ustawione)
set -euo pipefail
cd "$(dirname "$0")"

dotnet build SecondBrain.slnx -c Release

user_skills="${NOTES_ROOT:-$HOME/SecondBrain/notes}/.skills"
[ -d "$user_skills" ] || exit 0

for repo_skill in SecondBrain.Desktop/Skills/*.md; do
    user_skill="$user_skills/$(basename "$repo_skill")"
    [ -f "$user_skill" ] || continue
    if cmp -s "$repo_skill" "$user_skill"; then
        rm "$user_skill"
        echo "Usunieto duplikat: $user_skill"
    else
        echo "UWAGA: $user_skill rozni sie od wersji z repo i ja nadpisuje - usun recznie, jesli chcesz wersje z repo."
    fi
done
