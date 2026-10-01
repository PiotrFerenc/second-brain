---
name: source-archive
description: Archiwum źródeł SecondBrain budowane na GitHubie (second-brain-source.txt = tar.xz w base64, limit 150 KB), sprawdzanie rozmiaru, zmiana zawartości paczki i rozpakowanie w Windows 11 skryptem z gista. Użyj, gdy użytkownik pyta o rozmiar/zawartość paczki ze źródłami, workflow package.yml, release albo rozpakowanie.
---

# Archiwum źródeł (`.github/workflows/package.yml`)

Każdy push buduje release `build-<run_number>` z jednym plikiem `second-brain-source.txt`. To `tar` → `xz -9e` → `base64 -w0`. **Limit: 150 KB** dla tego pliku (base64 dodaje 33%, więc samo `tar.xz` musi mieć ok. 112 KB).

## Co jest w paczce

`git ls-files` bez `.claude/` i bez `.md` z katalogu głównego (`Skills/*.md` zostają – to część aplikacji), plus szablony `appsettings.json` skopiowane z `appsettings.Example.json` w `SecondBrain.Desktop` i `SecondBrain.PipelineTest`. Bez nich `dotnet build SecondBrain.slnx` pada na `MSB3030` (csproj kopiuje `appsettings.json`, który jest w `.gitignore`).

Paczka musi dać się zbudować: `SecondBrain.slnx` wymaga wszystkich projektów (w tym `SecondBrain.Tests` i `SecondBrain.PipelineTest`) oraz `SecondBrain.Desktop/Assets/avalonia-logo.ico` (ładowane przez `App.axaml.cs` i `MainWindow.axaml`). Wykluczając cokolwiek z tego, popraw też `.slnx`.

## Sprawdzenie rozmiaru (lokalnie, przed pushem)

```sh
{ git ls-files | grep -v -E '^(\.claude/|[^/]+\.md$)'; } | tar -cf - -T - | xz -9e | base64 -w0 | wc -c
```

Wynik < 150000. Stan na 2026-10-01: ok. 148,3 KB (zapas ok. 1,7 KB); bez `SecondBrain.Tests/` ok. 109,5 KB. Każde 1 KB skompresowanego kodu kosztuje 1,3 KB limitu.

Przy zmianie workflow sprawdź pełny obieg na czystym eksporcie: `git archive HEAD | tar -x -C <tmp>`, uruchom kroki z `run:`, rozpakuj wynik i zbuduj `dotnet build SecondBrain.slnx` (0 błędów). Zip nie wchodzi w grę – kompresuje pliki osobno i ma ok. 228 KB.

## Gdy brakuje miejsca

W kolejności: wyklucz `SecondBrain.Tests/` (ok. 109 KB; usuń projekt z `.slnx` w paczce) → wyklucz `PipelineTest` (usuń z `.slnx`) → zapytaj użytkownika. Nie usuwaj kodu aplikacji ani ikony bez zgody.

## Rozpakowanie

- Linux/macOS: `base64 -d second-brain-source.txt | tar -xJ`.
- Windows 11: skrypt `Expand-Base64Archive.ps1` (uniwersalny: xz/gz/bz2/zst/zip/7z/tar po bajtach początkowych, plik lub URL, PowerShell 5.1 i 7), publiczny gist: https://gist.github.com/PiotrFerenc/ffd37babebb3475e2120f43a2d0d59a5
  ```powershell
  .\Expand-Base64Archive.ps1 -Source second-brain-source.txt -Destination C:\Projekty\SecondBrain
  ```
  Zmiany w skrypcie: testuj pod `pwsh` na wszystkich formatach i aktualizuj gist przez `gh gist edit <id> -a <plik>`.

## Commit

Zmiany workflow commituj osobno, komunikat po angielsku (`Publish source as ...`). Release powstaje dopiero po przebiegu workflow na GitHubie – rozmiar z CI sprawdź w assetach release'a po pushu.
