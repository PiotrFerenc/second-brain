# SecondBrain

## Build

W katalogu głównym repo są dwa skrypty, które robią to samo:

| System | Polecenie |
|---|---|
| Linux / macOS | `./build.sh` |
| Windows 11 | `.\build.ps1` |

Każdy z nich:

1. Buduje solucję w konfiguracji Release (`dotnet build SecondBrain.slnx -c Release`). Skille z `SecondBrain.Desktop/Skills/` są kopiowane do katalogu aplikacji (`SecondBrain.Desktop/bin/Release/net10.0/Skills/`).
2. Sprząta folder `.skills/` w katalogu notatek. Kopie identyczne z wersją w repo są usuwane, bo przykrywałyby skille z repo. Jeśli kopia się różni, skrypt tylko wypisuje ostrzeżenie i niczego nie usuwa.

Domyślny katalog notatek to `~/SecondBrain/notes` (Windows: `%USERPROFILE%\SecondBrain\notes`). Jeśli w `appsettings.json` ustawiono `Storage:NotesRootPath`, podaj tę ścieżkę skryptowi:

```sh
NOTES_ROOT=/sciezka/do/notatek ./build.sh
```

```powershell
.\build.ps1 -NotesRoot D:\sciezka\do\notatek
```

Jeśli Windows blokuje uruchamianie skryptów:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## Skille agenta

Skill to plik `.md`. Pierwsza linia to opis, który agent widzi na liście skilli. Reszta pliku to instrukcja, którą agent wczytuje narzędziem `use_skill`.

- `SecondBrain.Desktop/Skills/` – skille wbudowane, trzymane w repo.
- `<katalog notatek>/.skills/` – skille lokalne użytkownika. Skill lokalny o tej samej nazwie nadpisuje wbudowany.

Nowy lub zmieniony plik w `.skills/` działa od razu, bez restartu. Zmiany w `SecondBrain.Desktop/Skills/` wymagają przebudowania aplikacji.
