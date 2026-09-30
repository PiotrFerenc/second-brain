---
name: create-plugin
description: Tworzenie nowej wtyczki (plugin) w SecondBrain - folder w SecondBrain.Plugins z klasą IPlugin, zakładka/slot/tray/handler zdarzeń/narzędzie agenta, weryfikacja buildem, CLI i Desktopem. Użyj, gdy użytkownik prosi o nową funkcję, wtyczkę, plugin lub zmianę istniejącej wtyczki.
---

# Tworzenie wtyczki SecondBrain

Każda funkcja poza rdzeniem (drzewo, foldery, edytor, widok notatki, agent) jest pluginem. Nowe funkcje powstają **wyłącznie** jako pluginy. Pełny opis: `README.md` sekcja „Wtyczki", `PLAN-PLUGINS.md`.

## Zanim zaczniesz

1. Przeczytaj 1-2 istniejące pluginy o podobnym kształcie w `SecondBrain.Plugins/`:
   - tylko slot: `Backlinks/BacklinksPlugin.cs`
   - zakładka + magazyn + handlery + narzędzia agenta: `Gaps/`, `Glossary/`
   - backend bez UI: `Duplicates/`
   - własny `HttpClient` i sekcja konfiguracji: `Ocr/`, `Rewrite/`
2. Sprawdź `git status` – pracuj na osobnej gałęzi (`feature/<nazwa>`), nie na dirty `master`.
3. Nie dotykaj hosta (`MainViewModel`, `MainWindow.axaml`). Jedyny dozwolony powód: nowy slot = jedna linia `<sdk:SlotHost SlotId="..."/>` w XAML.

## Kroki

1. **Folder** `SecondBrain.Plugins/<Nazwa>/` (PascalCase), namespace `SecondBrain.Plugins.<Nazwa>`. Projekt `SecondBrain.Plugins.csproj` bierze pliki sam (nie trzeba nic rejestrować; `.axaml` też).
2. **Klasa `<Nazwa>Plugin : IPlugin`** z bezparametrowym konstruktorem:
   - `Id` – stałe, małe litery (klucz w `~/SecondBrain/plugins.json`), `Name`, `Description` po polsku,
   - `ConfigureServices(IServiceCollection, IConfiguration)` – zwykłe rejestracje DI, wołane tylko dla włączonych pluginów,
   - opcjonalnie `StartAsync(IServiceProvider, CancellationToken)` – stan początkowy po starcie UI (np. licznik w tytule zakładki).
   Menedżer znajduje klasę refleksją – nic więcej nie rejestruj.
3. **Wybierz kontrybucje** (rejestruj w `ConfigureServices`):

   | Potrzeba | Rejestracja |
   | --- | --- |
   | zakładka | `services.AddSingleton<MojaTab>(); services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<MojaTab>());` (`Id`, `Title` bindowalny, `Placement` = `TabArea.SidebarToolbar/ContentHeader/Hidden`, `Order`, `Shortcut`, `CreateView()`, `OnActivatedAsync`) |
   | fragment widoku hosta | `services.AddSingleton<ISlotContribution, MojSlot>()` – `SlotId` jeden z: `Sidebar.AboveTree`, `Editor.Templates`, `Editor.Toolbar`, `Editor.Footer`, `Note.Header`, `Note.Actions`, `Note.Footer`, `Search.Toolbar`, `Search.ResultActions` |
   | pozycja tray „Nowa notatka > folder" | `ITrayNewNoteContribution` (`Build(folder)`) |
   | reakcja na zdarzenie | `services.AddSingleton<IEventHandler<NoteAdded>, MojHandler>()` – zdarzenia w `SecondBrain.Core/Events.cs` |
   | narzędzie agenta czatowego | klasa po `AgentTool` + `services.AddSingleton<IAgentTool, MojeNarzedzie>()`; `IsMutating` dla zapisów (pytanie Tak/Nie) |
   | własny magazyn | klasa `<Nazwa>Store` jako singleton w pluginie (nie dopisuj do `INoteStore`) |
   | zewnętrzne API / LLM | osobna sekcja w `appsettings.json` (Desktop **i** PipelineTest), osobny nazwany `HttpClient`, własny model i prompt – patrz skill `dotnet-llm-http-config` |
4. **Widok**: `.axaml` + `.axaml.cs` z bezparametrowym konstruktorem; zakładka to `ObservableObject` (CommunityToolkit), host ustawia `DataContext = zakładka`. Slot: `CreateControl()` to **fabryka** (wołana osobno dla każdego `SlotHost`), własny ViewModel/stan, brak DataContextu hosta.
5. **Stan aplikacji** tylko przez `IShell` (folder, notatka, `ShowTab`, `ShowNote`, `Search`, `TreeFilter`, `TopLevel`) i `IEditorContext` (tekst, status, `SaveAsync`). **Zapis notatek wyłącznie przez `NotePipeline`** (publikuje zdarzenia, po których host odświeża drzewo). Plugin nie zna `MainViewModel` ani innych pluginów – kanały: `IShell`, `IEditorContext`, zdarzenia, sloty.
6. Kod przenoszony z hosta/Infrastructure usuń z oryginału – plugin ma być jedynym właścicielem funkcji.

## Reguły, które kosztowały czas (łamanie = zawieszone okno)

- **Cykl DI**: host nie może zależeć od kontrybucji w konstruktorze; zakładki wstrzykują `IShell`. Nie wstrzykuj do pluginu niczego, co bierze zakładki w konstruktorze. Objaw: okno się nie mapuje, 0% CPU, brak wyjątku.
- **Handler zdarzenia nigdy nie czeka na wątek UI** (`await Dispatcher.UIThread.InvokeAsync`) – w CLI nie ma pętli Avalonii i wisi w nieskończoność. Użyj `Dispatcher.UIThread.Post` albo ładuj dane w `OnActivatedAsync`.
- `IShell` może być `null` w CLI (`IShell? shell = null`), jeśli klasę tworzą też handlery zdarzeń.
- Okna i kontrolki pluginu: bezparametrowy konstruktor, zależności przez `DataContext`.
- Wyłączony plugin nie dostaje `ConfigureServices` – nigdzie nie sprawdzaj „czy włączony". Kod, który używa cudzej funkcji, bierze ją przez `GetService` / interfejs hosta i znosi brak (no-op z logiem lub komunikat).
- Teksty komunikatów bez diakrytyków tylko tam, gdzie tak robi reszta (`Notices`); nazwy i opisy pluginu z diakrytykami.
- Zmiana włączenia działa po restarcie – to świadoma decyzja.

## Weryfikacja (obowiązkowa)

1. `dotnet build SecondBrain.slnx` → 0 błędów, 0 ostrzeżeń.
2. Backend z CLI (jeśli plugin ma backend): `cd SecondBrain.PipelineTest && timeout 60 dotnet run -- <komenda>`. Zawsze z `timeout`. `dotnet run -- tools` sprawdza, że narzędzia agenta są na liście.
3. Desktop **z** pluginem i **bez** (`~/SecondBrain/plugins.json` → `{"disabled":["<id>"]}`): start bez wyjątków w logu.
4. **Okno musi być zmapowane** – brak wyjątków nie wystarcza:
   `xwininfo -root -tree | grep '"Second Brain"'`, potem `xwininfo -id <id> | grep 'Map State'` → `IsViewable`. Sesja to Wayland: nie rób zrzutów okna (`import`, `xwd`, `gnome-screenshot -w` łapią cudze okno). Przy zawieszeniu: `~/.dotnet/tools/dotnet-stack report -p <pid>`.
5. Po testach przywróć `plugins.json`.

## Dokumentacja

- Dopisz `<id>` do listy „Wbudowane wtyczki" w `README.md` i (jeśli dodałeś slot/zdarzenie/narzędzie) do odpowiednich list w README oraz tabeli w `PLAN-PLUGINS.md`.
- Nowa nietrywialna zasada odkryta w trakcie → dopisz do „Reguł" w README i do tego skilla.

## Commit

Osobny commit na gałęzi `feature/<nazwa>`, komunikat po angielsku w stylu historii repo (np. `Add <name> plugin: <co robi>`). Scalanie do `master` tylko na prośbę użytkownika.
