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

## Wtyczki

Każda funkcja poza rdzeniem (drzewo, foldery, edytor, widok notatki, agent) jest wtyczką: folder w `SecondBrain.Plugins/` z klasą `IPlugin`. Wtyczki są kompilowane razem z aplikacją i odkrywane automatycznie – nie ma ładowania DLL z dysku.

### Menedżer wtyczek

Zakładka **Wtyczki** w pasku bocznym pokazuje listę z przełącznikami. Zmiana działa po ponownym uruchomieniu aplikacji (przycisk „Uruchom ponownie" na dole listy). Stan jest w `~/SecondBrain/plugins.json` jako lista wyłączonych, np. `{"disabled":["ocr","rewrite"]}` – brak pliku oznacza wszystkie włączone. Wyłączona wtyczka nie rejestruje niczego: znika jej zakładka, przyciski w slotach, pozycje w menu tray i reakcje na zdarzenia.

Wbudowane wtyczki: `search`, `trash`, `gaps`, `glossary`, `conflicts`, `timeline`, `history`, `ocr`, `import`, `rewrite`, `templates`, `tags`, `backlinks`, `quicknote`.

### Jak napisać wtyczkę

1. Nowy folder `SecondBrain.Plugins/<Nazwa>/` z klasą implementującą `SecondBrain.Plugins.Sdk.IPlugin` (`Id` małymi literami, `Name`, `Description`, `ConfigureServices`). Bezparametrowy konstruktor – tyle wystarczy, żeby menedżer ją znalazł.
2. W `ConfigureServices` zarejestruj to, z czego wtyczka się składa (zwykłe rejestracje DI):
   - **zakładka** – `ITabContribution` (tytuł, miejsce w pasku, skrót, `CreateView()`, `OnActivatedAsync`);
   - **fragment istniejącego widoku** – `ISlotContribution` z `SlotId` (`Sidebar.AboveTree`, `Editor.Templates`, `Editor.Toolbar`, `Editor.Footer`, `Note.Header`, `Note.Actions`, `Note.Footer`, `Search.Toolbar`, `Search.ResultActions`);
   - **pozycja w menu tray** „Nowa notatka > folder" – `ITrayNewNoteContribution`;
   - **reakcja na zdarzenie** – `IEventHandler<T>` dla zdarzeń z `SecondBrain.Core/Events.cs` (`NoteAdded`, `NoteCompressed`, `SearchCompleted`, `StorageChanged`, ...);
   - **własne serwisy, opcje i `HttpClient`** – jak w `Plugins/Ocr` lub `Plugins/Rewrite` (osobna sekcja w `appsettings.json` na wtyczkę).
3. Stan aplikacji czytaj przez `IShell` (wybrany folder/notatka, nawigacja, filtr drzewa, `TopLevel` do schowka i pickerów) i `IEditorContext` (tekst i status edytora). Zapis notatek wyłącznie przez `NotePipeline` – on publikuje zdarzenia, po których host odświeża drzewo.

Reguły, które kosztowały nas czas:

- Handler zdarzenia nigdy nie czeka na wątek UI (`await Dispatcher.UIThread.InvokeAsync`) – w CLI nie ma pętli Avalonii i wywołanie wisi w nieskończoność. Zakładka ładuje dane przy aktywacji albo używa `Dispatcher.UIThread.Post`.
- Okna i kontrolki wtyczki mają bezparametrowy konstruktor; zależności idą przez `DataContext`.
- `CreateControl()` jest wołane osobno dla każdego `SlotHost` – kontrybucja to fabryka, nie pojedyncza kontrolka.
- Host nie może zależeć od kontrybucji w konstruktorze (`IShell` bierze zakładki leniwie z `IServiceProvider`). Zakładki wstrzykują `IShell`, więc zależność w obie strony to cykl DI, którego kontener nie wykrywa przez fabryki – wątek UI wisi przy starcie i okno się nie pokazuje.
- Wtyczka nie zna `MainViewModel` ani innych wtyczek; jedyne kanały to `IShell`, `IEditorContext`, zdarzenia i sloty.

Backend wtyczek działa też w CLI (`SecondBrain.PipelineTest`), więc `add` z konsoli robi to samo, co zapis w oknie.
