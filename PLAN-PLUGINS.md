# PLAN-PLUGINS — system pluginów dla całej aplikacji

Dokument roboczy dla agenta implementującego. Uzupełnienie `PLAN.md` (sekcje 3 i 5 tamtego
dokumentu obowiązują, chyba że ten plik mówi inaczej) i `PLAN-AGENT-PLUGINS.md` (agent
czatowy — **poza zakresem** tego planu, patrz sekcja 1.3).

Cel: każda funkcjonalność aplikacji poza rdzeniem jest pluginem, który ma warstwę
backendową (serwisy, magazyn, reakcje na zdarzenia) i wizualną (zakładka, przyciski
w istniejących widokach, pozycje w menu tray). Istniejące funkcje przepisujemy na pluginy
w pierwszej kolejności; nowe funkcje powstają wyłącznie jako pluginy. Menedżer pluginów
pozwala włączyć/wyłączyć każdy z nich.

## Stan realizacji (2026-09-30)

Fazy 0 i 1 wykonane i scalone na `master`; faza 2 częściowo (sprzątanie zależne od agenta odłożone).

- Faza 0: `NotePipeline`, `NoteSearch`, `NotesRoot`, szyna zdarzeń, SDK, `PluginManager`,
  powłoka ze slotami, zakładka „Wtyczki" — bez zmiany zachowania.
- Faza 1: 14 pluginów (`search`, `trash`, `gaps`, `glossary`, `conflicts`, `timeline`,
  `history`, `ocr`, `import`, `rewrite`, `templates`, `tags`, `backlinks`, `quicknote`).
  Każdy sprawdzony: build 0/0, Desktop bez wyjątków z pluginem i bez, CLI tam, gdzie
  funkcja ma backend. Test „wszystkie wyłączone": aplikacja startuje bez wyjątków.
- `MainViewModel.cs` 1320 → 710 linii, `MainWindow.axaml` 641 → 360, `App.axaml.cs` 185 → 117.

Odstępstwa od planu (świadome):

1. ~~`duplicates` nie jest pluginem~~ — zrobione po P2 agenta: plugin `duplicates`
   (backend-only: `DuplicateScanner` + narzędzie `find_duplicate_notes`), `TagCleaner`/
   `TagMerger` w pluginie `tags`. W Infrastructure nie ma już serwisów funkcji.
2. **`INoteStore` nie schudł** (luki, słownik, fakty, szablony, scalanie tagów zostają) —
   ten sam powód: agent i CLI wołają te metody. Magazyny pluginów (`GapStore` itd.)
   powstaną przy P2; do tego czasu pluginy używają `INoteStore`.
3. `PluginManager` żyje w Sdk (CLI nie może referencować WinExe); `TabPlacement` →
   `TabArea` (kolizja z `Avalonia.Controls.TabPlacement`).
4. Dodane do kontraktu w trakcie migracji: `IShell.Search`, `IShell.SelectedSearchResult`,
   `IShell.SelectedNoteChanged`, `ISearchTab`, `IQuickNoteHost`, `IEditorContext :
   INotifyPropertyChanged` + `SaveAsync`/`SetFolder`, `NoteCompressed.TagsFromUser`,
   `NullShell`/`NullEditorContext` w CLI.
5. Kolejność przycisków w pasku i pozycji tray = `Order` kontrybucji / kolejność Id
   pluginów; `ITrayNewNoteContribution` nie ma `Order`.
6. Komunikaty sprzeczności/luk ujednolicone do wersji bez diakrytyków (jedno źródło
   w `Notices`); status importu to jeden „Importuje..." zamiast per linia.

Reguły odkryte w trakcie (obowiązują dla nowych pluginów): host (`ShellAdapter`) nie
wstrzykuje kontrybucji w konstruktorze — zakładki wstrzykują `IShell`, a cykl przez fabrykę
DI zawieszał wątek UI przy starcie (okno nigdy się nie mapowało, zależnie od liczby
pluginów); handler zdarzenia nigdy nie
czeka na wątek UI; okna/kontrolki pluginu z bezparametrowym konstruktorem; `CreateControl`
to fabryka; komendy CLI w testach agentów z `timeout`.

Pozostało z fazy 2: `README` „Wtyczki", `PLAN.md`, `hello` usunięty, `duplicates` — zrobione;
odchudzenie `INoteStore` — w toku (P5, po P2 agenta).

## 0. W skrócie

- Plugin = klasa `IPlugin` + folder w projekcie `SecondBrain.Plugins`. Kompilowany razem
  z aplikacją, odkrywany refleksją, bez ładowania DLL z dysku.
- Plugin rejestruje w DI swoje serwisy, handlery zdarzeń i kontrybucje UI: zakładki
  (`ITabContribution`), fragmenty wstawiane w sloty istniejących widoków
  (`ISlotContribution`), pozycje menu tray (`ITrayNewNoteContribution`).
- Rdzeń (host) daje pluginom: `NotePipeline` (jedyne miejsce zapisu notatek), szynę
  zdarzeń (`IEventBus`), stan powłoki i nawigację (`IShell`), kontekst edytora
  (`IEditorContext`), ścieżkę katalogu notatek (`NotesRoot`), sloty w widokach.
- Menedżer: zakładka „Wtyczki", lista z przełącznikami, stan w `~/SecondBrain/plugins.json`.
  Zmiana działa po restarcie aplikacji (świadomie, patrz decyzje).
- Kolejność: faza 0 (fundament: `NotePipeline`, zdarzenia, SDK, powłoka, menedżer) →
  faza 1 (migracja 15 funkcji w czterech partiach, równolegle w worktree) → faza 2
  (sprzątanie `INoteStore`, `MainViewModel`, dokumentacja).

## 1. Diagnoza — stan kodu (commit `467e64d`)

### 1.1. Gdzie dziś żyją funkcje

Wszystko jest w trzech miejscach, przemieszane:

| Miejsce | Rozmiar | Co zawiera |
| --- | --- | --- |
| `Desktop/ViewModels/MainViewModel.cs` | 1320 linii | drzewo, foldery, edytor, notatka, szukaj, kosz, luki, słownik, historia, oś czasu, agent, sesje, zakładki, start |
| `Desktop/Views/MainWindow.axaml` | 641 linii | sidebar (pasek 7 przycisków, drzewo, foldery) + 9 paneli `IsVisible="{Binding IsXxxTabActive}"` |
| `Desktop/Views/MainWindow.axaml.cs` + `App.axaml.cs` | 435 + 185 linii | szablony i chipy tagów budowane w code-behind, import pliku/folderu, OCR ze schowka (×2), drag&drop drzewa, @-wzmianki, schowek, tray z podmenu „Nowa notatka" |
| `Core/Interfaces.cs` → `INoteStore` | 19 metod | notatki **oraz** kosz, szablony, skille, luki, słownik, scalanie tagów, wersje faktów, historia gita |
| `Infrastructure/*` | | po jednym pliku na provider LLM (dobrze), plus `GitBackedNoteStore` jako dekorator całego `INoteStore` |

Konsekwencje dla pluginów:

1. **Nie ma punktu wpięcia UI.** Zakładka to stała `TabXxx`, flaga `IsXxxTabActive`,
   komenda `ShowXxxTab`, `DockPanel` w oknie i przycisk w `WrapPanel` — pięć miejsc
   w dwóch plikach na jedną zakładkę. Przyciski funkcji w edytorze („Wklej obrazek",
   „Importuj plik", „Przepisz") są wpisane na sztywno w XAML edytora.
2. **`INoteStore` jest workiem na wszystko.** Luki (`.gaps`), słownik (`.glossary`), fakty
   (`.facts`), szablony (`.templates`), historia gita — każda z tych funkcji ma swoje
   metody w interfejsie notatek. Plugin „Luki" nie może dziś być wyłączony, bo jego
   magazyn jest częścią rdzenia. `GitBackedNoteStore` musi znać wszystkie 19 metod, żeby
   zdecydować, po której robić commit.
3. **Pipeline zapisu notatki (kompresja → zapis → słownik → embedding → upsert →
   powiązane → sprzeczność → fakty → domykanie luk) jest skopiowany 7 razy** (szczegóły
   w `PLAN-AGENT-PLUGINS.md`, sekcja 1). Reakcje funkcji na „dodano notatkę" (słownik,
   sprzeczności, luki) są wplecione w każdą kopię zamiast być osobnymi krokami.
4. **Funkcje komunikują się przez pola jednego ViewModelu**: luka → „Szukaj ponownie"
   ustawia `SearchQuery` i `SelectedTabIndex`; chip tagu → `ActiveTagFilter` +
   `LoadTreeAsync`; oś czasu → `SelectedNote`. Po rozbiciu potrzebny jest jawny kanał
   (nawigacja + zdarzenia).
5. **Odświeżanie jest ręczne i rozproszone**: po każdej mutacji wywołujący sam woła
   `LoadTreeAsync`/`LoadTrashAsync`/`LoadGapsAsync`/`LoadGlossaryAsync`. Plugin nie może
   wiedzieć, co ma odświeżyć inny plugin — potrzebne zdarzenia.

### 1.2. Inwentarz funkcji → pluginy

Rdzeń (nie plugin, nie do wyłączenia): okno i powłoka (sidebar, drzewo folderów i
notatek, drag&drop zagnieżdżania, tworzenie/usuwanie folderów), edytor nowej notatki
(tekst, tagi ręczne, wybór rodzica, Ctrl+S), widok notatki (podgląd Markdown, edycja,
pin/odepnij, kopiuj), zakładki i skróty, zapamiętywanie rozmiaru okna, tray z „Pokaż"
i „Zamknij", menedżer pluginów, `NotePipeline`, szyna zdarzeń, providerzy z `Core`
(`ICompressor`, `IEmbedder`, `IReranker`, `IAnswerSynthesizer`, `INoteStore`,
`IVectorIndex`), wyszukiwanie hybrydowe jako serwis (`NoteSearch`, bo używa go też agent
i domykanie luk).

Pluginy (15), z tym, co dziś mają, i co muszą dostać od hosta:

| Id | Nazwa | Backend | UI dziś | Wymaga od hosta |
| --- | --- | --- | --- | --- |
| `search` | Szukaj | odpowiedź RAG (`IAnswerSynthesizer`), publikuje `SearchCompleted` | zakładka, Ctrl+F, przełącznik „tylko ten folder", karta odpowiedzi, „Usuń" przy wyniku | `NoteSearch`, `IShell` (folder, nawigacja do notatki), slot `Search.Toolbar` |
| `trash` | Kosz | `NotePipeline.TrashAsync/RestoreAsync`, `INoteStore` (kosz) | zakładka, „Usuń" w widoku notatki i w wynikach szukania | sloty `Note.Actions`, `Search.ResultActions`; zdarzenia `NoteTrashed/NoteRestored` |
| `gaps` | Luki w wiedzy | `GapStore` (`.gaps`), handler `SearchCompleted` (loguje lukę), handler `NoteAdded` (auto-domykanie, dziś `GapAutoCloser`) | zakładka z licznikiem na przycisku, „Odrzuć", „Szukaj ponownie" | `NoteSearch`, `IAnswerSynthesizer`, nawigacja do `search` z zapytaniem |
| `glossary` | Słownik | `GlossaryStore` (`.glossary`), handler `NoteAdded/NoteEdited` (definicje z `CompressionResult`) | zakładka | zdarzenia niosące `CompressionResult` |
| `conflicts` | Sprzeczności i wersje faktów | `IConflictDetector` + HttpClient „ConflictDetection", `FactStore` (`.facts`), handler `NoteAdded` (tylko pojedyncze dodanie, nie import) | brak (komunikat w statusie edytora) | zdarzenie z listą powiązanych notatek i kolektorem komunikatów |
| `timeline` | Oś czasu | brak | zakładka (grupy po dacie), klik → notatka | `INoteStore`/`IVectorIndex` (lista), `IShell.ShowNote` |
| `history` | Historia (git) | auto-commit po każdej mutacji (dziś `GitBackedNoteStore`), `GitRepository` (lista commitów, diff) | zakładka z listą commitów i diffem | zdarzenia mutacji notatek **i** `StorageChanged` z pluginów, `NotesRoot` |
| `ocr` | OCR | `IOcrExtractor` + HttpClient „Ocr" | „Wklej obrazek ze schowka" w edytorze, „Szukaj obrazem" w Szukaj, „Obrazek ze schowka (OCR)" w tray | `IEditorContext`, sloty `Editor.Toolbar`, `Search.Toolbar`, tray |
| `import` | Import | `NotePipeline.ImportAsync` | „Importuj plik", „Importuj folder" w edytorze | `IEditorContext` (folder, status), `Editor.Toolbar`, `StorageProvider` z okna |
| `rewrite` | Przepisz wg instrukcji | `INoteRewriter` + HttpClient „NoteRewrite" | pole instrukcji + przycisk w edytorze | `IEditorContext` (tekst), slot `Editor.Footer` |
| `templates` | Szablony | `TemplateStore` (`.templates` + domyślne) | rząd przycisków nad edytorem | `IEditorContext`, slot `Editor.Templates` |
| `tags` | Tagi | auto-tagowanie z sąsiadów (dziś `SuggestTags` w VM), `ITagCleaner` + `TagMerger` + HttpClient „TagCleaning" (dziś tylko agent) | chipy tagów w widoku notatki, baner filtra nad drzewem, filtrowanie drzewa | slot `Note.Header`, `Sidebar.AboveTree`, `IShell.TreeFilter`, zdarzenie `NoteCompressed` (przed zapisem, tagi do zmiany) |
| `backlinks` | Odnośniki | skan `[[Tytuł]]` w folderze | sekcja pod notatką | slot `Note.Footer`, `IShell.SelectedNote` |
| `quicknote` | Szybka notatka | brak | tray: „Wpisz...", „Ze schowka" per folder; `QuickNoteWindow` | `IEditorContext`, tray, lista folderów |
| `duplicates` | Duplikaty | `DuplicateScanner` (dziś agent + CLI) | brak | `IVectorIndex`, `IEmbedder`, `INoteStore` |

Poza tabelą, zostaje w rdzeniu: „Może powiązane" (top-3 po zapisie — to wynik
`NotePipeline.AddAsync`, nie osobna funkcja), pin (pole modelu + sortowanie w
`FileNoteStore.ListAsync`).

### 1.3. Agent — poza zakresem, ale styk istnieje

Zakładka Agent, sesje, @-wzmianki, `FabrykaAgent` i jego narzędzia zostają w hoście
dokładnie tak, jak są. Jedyny styk: po refaktorze z `PLAN-AGENT-PLUGINS.md` (P2) narzędzia
agenta to klasy `IAgentTool` rejestrowane w DI. Plugin, który ma narzędzia (np. `gaps`:
`list_gaps`, `resolve_gap`), rejestruje je w swoim `ConfigureServices` — agent widzi je
automatycznie, a wyłączony plugin ich nie rejestruje. Ten plan tego nie wykonuje; tylko
nie może tego utrudnić (nie utrudnia — DI jest wspólne).

## 2. Architektura

### 2.1. Projekty

```
SecondBrain.Core                 bez zmian w zależnościach; dochodzą: zdarzenia + IEventBus/IEventHandler
SecondBrain.Infrastructure       dochodzą: NotePipeline, NoteSearch, EventBus, NotesRoot; ubywa: GitBackedNoteStore,
                                 GapAutoCloser, TagCleaning, DuplicateScanner, Ocr, NoteRewrite, ConflictDetection
                                 (przenoszą się do pluginów); INoteStore chudnie (sekcja 2.6)
SecondBrain.Plugins.Sdk  (nowy)  kontrakty UI: IPlugin, ITabContribution, ISlotContribution, ITrayNewNoteContribution,
                                 IShell, IEditorContext, NoteItem (dziś SearchResultItem), Converters/, kontrolka SlotHost,
                                 PluginManager (tu, nie w Desktop - CLI też go używa, a nie może referencować WinExe)
                                 refs: Core, Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection.Abstractions,
                                 Microsoft.Extensions.Configuration.Abstractions
SecondBrain.Plugins      (nowy)  jeden projekt, folder na plugin: Search/, Trash/, Gaps/, Glossary/, Conflicts/, Timeline/,
                                 History/, Ocr/, Import/, Rewrite/, Templates/, Tags/, Backlinks/, QuickNote/, Duplicates/
                                 refs: Sdk, Infrastructure, Markdown.Avalonia.Tight, CliWrap (History)
SecondBrain.Desktop              host: powłoka, rdzeń UI, adaptery IShell/IEditorContext, zakładka Wtyczki; refs: Plugins
SecondBrain.PipelineTest         CLI: też ładuje pluginy (backend), żeby `add` zachowywał się jak w Desktopie; refs: Plugins
```

Jeden projekt na wszystkie pluginy, folder na plugin. Osobny `.csproj` dopiero dla pluginu,
który potrzebuje własnej zależności NuGet nieużywanej przez pozostałe albo powstaje poza
tym repo — mechanizm odkrywania (sekcja 2.4) przyjmuje listę asemblerów, więc to
jedna linia więcej w `PluginManager`, nie zmiana architektury.

Folder pluginu (przykład, `Plugins/Gaps/`):

```
GapsPlugin.cs          IPlugin: Id/Name/Description, ConfigureServices, StartAsync
GapStore.cs            magazyn .gaps/ (przeniesione z FileNoteStore)
GapHandlers.cs         IEventHandler<SearchCompleted>, IEventHandler<NoteAdded> (auto-domykanie)
GapsTab.cs             ITabContribution + ViewModel (ObservableObject, [RelayCommand])
GapsView.axaml(.cs)    UserControl, x:DataType="local:GapsTab"
```

### 2.2. Kontrakt pluginu (`SecondBrain.Plugins.Sdk`)

```csharp
public interface IPlugin
{
    string Id { get; }            // stałe, małe litery, np. "gaps"; klucz w plugins.json
    string Name { get; }          // "Luki w wiedzy"
    string Description { get; }   // jedno zdanie do menedżera

    // Backend: serwisy, opcje, HttpClienty, magazyny, handlery zdarzeń, kontrybucje UI.
    // Wołane TYLKO dla włączonych pluginów, przed BuildServiceProvider.
    void ConfigureServices(IServiceCollection services, IConfiguration config);

    // Po starcie UI (okno otwarte, provider zbudowany): wczytanie stanu początkowego,
    // np. licznik luk na przycisku. Domyślnie nic.
    Task StartAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
```

Kontrybucje UI to zwykłe rejestracje DI (`services.AddSingleton<ITabContribution, GapsTab>()`),
host zbiera je przez `IEnumerable<...>`:

```csharp
public enum TabArea { SidebarToolbar, ContentHeader, Hidden }

public interface ITabContribution : INotifyPropertyChanged   // ObservableObject w praktyce
{
    string Id { get; }                  // "gaps" — cel dla IShell.ShowTab
    string Title { get; }               // bindowalne: "Luki (3)" aktualizuje przycisk
    TabArea Placement { get; }
    int Order { get; }
    KeyGesture? Shortcut { get; }       // np. Ctrl+F dla search
    Control CreateView();               // raz, host cache'uje; DataContext = this
    Task OnActivatedAsync(CancellationToken ct) => Task.CompletedTask;  // History ładuje commity dopiero tu
}

public interface ISlotContribution
{
    string SlotId { get; }              // patrz 2.5
    int Order { get; }
    Control CreateControl();            // własny DataContext (VM pluginu); stan hosta przez IEditorContext/IShell
}

public interface ITrayNewNoteContribution
{
    NativeMenuItem Build(string folder); // pozycja w podmenu "Nowa notatka > <folder>"
}
```

Host udostępnia pluginom (interfejsy w Sdk, implementacje w Desktop):

```csharp
public interface IShell
{
    string? SelectedFolder { get; }
    NoteItem? SelectedNote { get; }
    IReadOnlyList<string> Folders { get; }
    void ShowTab(string tabId);
    void ShowNote(NoteItem note);               // przełącza na widok notatki
    Func<Note, bool>? TreeFilter { get; set; }  // tags: zawężenie drzewa; null = bez filtra
    Task RefreshTreeAsync();                    // tylko dla TreeFilter; mutacje idą zdarzeniami
    TopLevel TopLevel { get; }                  // schowek, StorageProvider (import, OCR)
}

public interface IEditorContext        // stan edytora nowej notatki (ObservableObject w hoście)
{
    string? Folder { get; }
    string Text { get; set; }
    string Status { set; }
    bool IsBusy { get; set; }
    Task OpenQuickNoteAsync(string folder);    // quicknote: małe okno z tym samym kontekstem
}
```

`NoteItem` to dzisiejszy `SearchResultItem` przeniesiony do Sdk (używają go drzewo,
szukaj, oś czasu, kosz — musi być wspólny). `TreeItem` zostaje w hoście.

### 2.3. Szyna zdarzeń (`Core` + `Infrastructure`)

Jedyny kanał backendowy między rdzeniem a pluginami i między pluginami. Interfejsy w
`Core` (czyste C#), implementacja w `Infrastructure` (potrzebuje `IServiceProvider`):

```csharp
public interface IEventHandler<in TEvent> { Task HandleAsync(TEvent e, CancellationToken ct); }

public interface IEventBus
{
    // Sekwencyjnie, w kolejności rejestracji; wyjątek handlera logowany, nie przerywa
    // pozostałych ani operacji, która zdarzenie opublikowała.
    Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default);
}
```

Zdarzenia (rekordy w `Core/Events.cs`), publikowane **wyłącznie** przez `NotePipeline`
(mutacje notatek), `NoteSearch`/plugin `search` (wyszukiwanie) i magazyny pluginów
(`StorageChanged`):

| Zdarzenie | Kiedy | Ładunek | Kto słucha |
| --- | --- | --- | --- |
| `NoteCompressed` | po kompresji, **przed** zapisem | `Folder`, `RawText`, `CompressionResult`, `IList<string> Tags` (do zmiany), `IReadOnlyList<Note> Related` | `tags` (auto-tagowanie z sąsiadów) |
| `NoteAdded` | po upsercie nowej notatki | `Folder`, `Note`, `CompressionResult`, `Related`, `bool FromImport`, `ICollection<string> Notices` | `glossary`, `conflicts` (pomija `FromImport`), `gaps` (auto-domykanie, przy imporcie raz — patrz niżej), `history` |
| `NoteEdited` | po ponownej kompresji i upsercie | jak wyżej bez `FromImport` | `glossary`, `history` |
| `NoteReindexed` | zapis + upsert bez kompresji (pin, tagi, rodzic, przeniesienie) | `Folder`, `Note` | `history` |
| `NoteTrashed` / `NoteRestored` / `NotePurged` | kosz | `Folder`, `Note` / `TrashedNote` | `trash` (odświeżenie), `history` |
| `FolderCreated` / `FolderDeleted` | foldery | `Folder` | `history` |
| `ImportCompleted` | po całym imporcie | `Folder`, `int Count`, `Notices` | `gaps` (auto-domykanie raz), `history` |
| `NotesChanged` | po każdym z powyższych (host publikuje zbiorczo) | — | host (drzewo), `timeline`, `backlinks` |
| `SearchCompleted` | po wyszukiwaniu z odpowiedzią RAG | `Query`, `bool Answered` | `gaps` (loguje lukę gdy `!Answered`) |
| `StorageChanged` | plugin zmienił własny magazyn (`.gaps`, `.glossary`, `.facts`, `.templates`) | `string Message` | `history` (commit) |

`Notices` to kolektor komunikatów: handler dopisuje („Możliwa sprzeczność z …",
„Zamknięto 2 luki"), a wywołujący (`edytor`, agent, CLI) pokazuje je po swojemu — dokładnie
to, co dziś robi każda z 7 kopii pipeline'u, tylko że każdy komunikat powstaje w jednym
miejscu.

### 2.4. Menedżer pluginów (`PluginManager` w Sdk, UI w hoście)

```csharp
public sealed class PluginManager
{
    // Odkrywanie: refleksja po stałej liście asemblerów (dziś jeden: SecondBrain.Plugins).
    // Instancja każdego IPlugin (bezparametrowy konstruktor), sortowanie po Id.
    public static PluginManager Discover(params Assembly[] assemblies);

    public IReadOnlyList<PluginEntry> Plugins { get; }   // Id, Name, Description, IsEnabled (bindowalne)

    // Wczytuje ~/SecondBrain/plugins.json ({"disabled": ["ocr", "rewrite"]}; brak pliku = wszystko włączone),
    // woła ConfigureServices TYLKO włączonych. Rejestruje też siebie w DI.
    public void ConfigureServices(IServiceCollection services, IConfiguration config);

    public Task StartAsync(IServiceProvider sp, CancellationToken ct);   // StartAsync włączonych
    public void SetEnabled(string id, bool enabled);   // zapis plugins.json; skutek po restarcie
}
```

- Stan w `~/SecondBrain/plugins.json` obok `window.json` — to lokalny stan użytkownika,
  nie konfiguracja providerów (`appsettings.json`). Lista **wyłączonych**, nie włączonych:
  nowy plugin po aktualizacji jest domyślnie włączony bez dotykania pliku.
- Zakładka „Wtyczki" (rdzeń, `TabArea.SidebarToolbar`, ostatnia): lista
  Name / Description / `ToggleSwitch`; pod listą tekst „Zmiany zadziałają po ponownym
  uruchomieniu" i przycisk „Uruchom ponownie" (`Process.Start(Environment.ProcessPath)` +
  `CloseFromTray`, best-effort).
- Wyłączony plugin: brak `ConfigureServices` → brak jego serwisów, handlerów, zakładek,
  slotów, pozycji tray, narzędzi agenta. Nic w hoście nie sprawdza „czy plugin X jest
  włączony" — nieobecność w DI załatwia wszystko.
- Konfiguracja pluginu (`appsettings.json`) zostaje sekcją o nazwie funkcji, jak dziś
  (`Ocr`, `ConflictDetection`, `NoteRewrite`, `TagCleaning`) — plugin sam ją binduje w
  `ConfigureServices`. Bez UI ustawień pluginów.

Start (`Program.Main`):

```
config → services.AddSecondBrainInfrastructure(config)
       → services.AddSecondBrainShell()                 // IShell, IEditorContext, MainViewModel, zakładka Wtyczki
       → PluginManager.Discover(typeof(SearchPlugin).Assembly).ConfigureServices(services, config)
       → App.Services = services.BuildServiceProvider()
       → Avalonia; po Opened: MainViewModel.InitializeAsync, potem pluginManager.StartAsync
```

CLI (`PipelineTest`) robi te same trzy pierwsze kroki bez `AddSecondBrainShell` — dzięki
temu `add` w CLI uruchamia te same handlery (słownik, sprzeczności, luki) co Desktop.
`SecondBrain.Plugins` ciągnie Avalonię do CLI jako zależność; to tylko załadowany
asembler, bez inicjalizacji — akceptowalne, tańsze niż dzielenie każdego pluginu na
dwa projekty.

### 2.5. Powłoka i sloty (host)

`MainWindow.axaml` po zmianie:

- Pasek narzędzi w sidebarze: `ItemsControl ItemsSource="{Binding ToolbarTabs}"`
  (kontrybucje z `Placement=SidebarToolbar` po `Order`) — `Button Content="{Binding Title}"`.
  Nagłówek prawego panelu („Notatka", „+"): rdzeń, plus kontrybucje `ContentHeader`.
- Treść: `Panel` z widokami rdzenia (edytor, notatka) + `ContentControl
  Content="{Binding ActiveTabView}"` dla zakładek pluginów. Host trzyma `ActiveTabId`;
  `IShell.ShowTab(id)` ustawia go i woła `OnActivatedAsync`.
- `<sdk:SlotHost SlotId="Editor.Toolbar" />` w miejscach, gdzie dziś są przyciski
  funkcji. `SlotHost` to `ItemsControl`, który po dołączeniu do drzewa wizualnego pobiera
  `IEnumerable<ISlotContribution>` z `PluginRuntime.Services` (statyczny provider ustawiany
  przez hosta — kontrolka nie ma DI), filtruje po `SlotId`, sortuje po `Order`, woła
  `CreateControl()` raz. Orientacja (`WrapPanel`/`StackPanel`) jako właściwość.
- Skróty: host rejestruje `KeyBinding` dla każdej zakładki z `Shortcut`.
- Tray: podmenu „Nowa notatka > <folder>" budowane z `IEnumerable<ITrayNewNoteContribution>`;
  „Pokaż"/„Zamknij" zostają w hoście.

Sloty początkowe — tworzone tylko te, których migrowane funkcje potrzebują (nowy slot =
jedna linia `SlotHost` w XAML hosta; to jedyny dozwolony powód dotknięcia hosta przy
dodawaniu pluginu):

| SlotId | Gdzie | Dziś wypełnia |
| --- | --- | --- |
| `Sidebar.AboveTree` | nad drzewem | `tags` (baner aktywnego filtra) |
| `Editor.Templates` | nad polem tekstu | `templates` |
| `Editor.Toolbar` | rząd przycisków obok „Zapisz" | `ocr`, `import` |
| `Editor.Footer` | pod polem tekstu | `rewrite` |
| `Note.Header` | pod tytułem notatki | `tags` (chipy) |
| `Note.Actions` | obok pin/edytuj/kopiuj | `trash` („Usuń") |
| `Note.Footer` | pod treścią | `backlinks` |
| `Search.Toolbar` | obok pola zapytania | `ocr` („Szukaj obrazem") |
| `Search.ResultActions` | przy wybranym wyniku | `trash` („Usuń") |

Kontrybucja slotu nie dostaje DataContextu hosta — tworzy własny ViewModel i czyta stan
przez `IShell`/`IEditorContext`. Dzięki temu widok hosta nie zna pluginu, a plugin nie
zna `MainViewModel`.

### 2.6. Backend hosta po zmianie

- **`NotePipeline`** (Infrastructure) — jak w `PLAN-AGENT-PLUGINS.md` P1, z jedną różnicą:
  zamiast wołać słownik/sprzeczności/luki wprost, publikuje zdarzenia z 2.3 i zwraca
  `Notices`. Zależności: `ICompressor`, `IEmbedder`, `IVectorIndex`, `INoteStore`, `IEventBus`.
- **`NoteSearch`** (Infrastructure) — dzisiejsze `HybridNoteSearch` + reranker + doczytanie
  pełnych notatek, jako serwis (używany przez plugin `search`, `gaps`, agenta).
- **`NotesRoot`** (Infrastructure) — jedna klasa liczy ścieżkę katalogu notatek (dziś
  skopiowane w `FileNoteStore`, `GitBackedNoteStore`, `FileAgentSessionStore`); magazyny
  pluginów dostają ją w konstruktorze.
- **`INoteStore` chudnie** do operacji na notatkach: `SaveAsync`, `LoadAsync`, `ListAsync`,
  `DeleteFolderAsync`, `MoveAsync`, kosz (`MoveToTrashAsync`, `ListTrashAsync`,
  `RestoreFromTrashAsync`, `PurgeTrashAsync`), `MergeTagsAsync`, `ListSkillsAsync` (agent).
  Wychodzą do pluginów: szablony → `TemplateStore`, luki → `GapStore`, słownik →
  `GlossaryStore`, fakty → `FactStore`, `ListCommitsAsync`/`GetCommitDiffAsync` →
  `GitRepository`. `GitBackedNoteStore` znika — plugin `history` ma jeden handler
  reagujący na wszystkie zdarzenia mutacji i `StorageChanged` (ta sama kolejka
  commitów w tle, co dziś).
- Foldery: `CreateFolder/DeleteFolder` przechodzą przez `NotePipeline` (publikują zdarzenia).

## 3. Przykład przepływu: „Usuń" w widoku notatki (plugin `trash`)

1. `TrashPlugin.ConfigureServices`: `AddSingleton<ITabContribution, TrashTab>()`,
   `AddSingleton<ISlotContribution, TrashNoteAction>()` (`SlotId="Note.Actions"`),
   `AddSingleton<ISlotContribution, TrashSearchResultAction>()`,
   `AddSingleton<IEventHandler<NoteTrashed>, TrashTab>()` (ta sama instancja — `AddSingleton(sp => sp.GetRequiredService<TrashTab>())`).
2. Host renderuje widok notatki; `SlotHost SlotId="Note.Actions"` tworzy przycisk „Usuń"
   z `TrashNoteAction.CreateControl()`.
3. Klik: `TrashNoteAction` bierze `IShell.SelectedNote`, woła `NotePipeline.TrashAsync(folder, note)`.
4. `NotePipeline`: usuwa z indeksu, przenosi plik do `.trash`, publikuje `NoteTrashed`,
   potem `NotesChanged`.
5. Handlery: `TrashTab` przeładowuje listę kosza; `history` robi commit; host na
   `NotesChanged` odświeża drzewo (`LoadTreeAsync`, jak dziś).
6. Plugin wyłączony: brak przycisku, brak zakładki, brak handlera. Notatki nie da się
   usunąć z UI — to oczekiwane, a nie błąd (agent/CLI dalej mogą przez `NotePipeline`).

## 4. Decyzje i ich powody

| Decyzja | Powód |
| --- | --- |
| Pluginy kompilowane w repo, jeden projekt `SecondBrain.Plugins`, folder na plugin; odkrywanie refleksją po stałej liście asemblerów | Jeden użytkownik z dostępem do repo; izolacja na poziomie asemblera nic tu nie daje, a 15 `.csproj` kosztuje przy każdej zmianie SDK. Lista asemblerów pozwala dołożyć osobny projekt bez zmiany mechanizmu |
| Bez ładowania DLL z dysku (`AssemblyLoadContext`) | Wymaga wersjonowanego kontraktu, izolacji zależności i kanału dystrybucji — nie ma odbiorcy. Ceiling: jeśli pojawi się drugi użytkownik bez repo |
| Włączanie/wyłączanie działa po restarcie, nie na żywo | Kontener DI jest niezmienny po zbudowaniu; wyłączony plugin = brak rejestracji, więc nigdzie nie trzeba filtrować „czy włączony". Toggle na żywo wymagałby filtrowania w szynie, slotach, zakładkach, tray i rejestrze narzędzi agenta — pięć miejsc, każde z własnym błędem do popełnienia. Restart osobistej aplikacji kosztuje 2 sekundy |
| `plugins.json` trzyma listę **wyłączonych** | Nowy plugin po aktualizacji jest od razu widoczny; użytkownik wyłącza to, czego nie chce, zamiast włączać to, o czym nie wie |
| Bez grafu zależności między pluginami | Wszystkie dzisiejsze powiązania są miękkie: slot bez hosta się nie renderuje, zdarzenie bez odbiorcy nie robi nic, `ShowTab` na nieistniejącą zakładkę jest no-op (logowany). Dodać `DependsOn`, gdy pojawi się pierwsza twarda zależność |
| Zdarzenia zamiast bezpośrednich wywołań między funkcjami; handlery sekwencyjne, błąd handlera nie przerywa operacji | Jedyny sposób, żeby wyłączenie `glossary` nie wymagało `if` w pipeline. Sekwencyjność zachowuje dzisiejszą kolejność (słownik → sprzeczność → luki) i unika równoległych zapisów w jednym repo gita |
| `Notices` w zdarzeniu zamiast wyniku z handlera | Trzech wywołujących (edytor, agent, CLI) pokazuje komunikaty po swojemu; handler nie może wiedzieć, jak — dopisuje tekst, wywołujący decyduje |
| Kontrybucje UI jako rejestracje DI (`IEnumerable<ITabContribution>`), nie osobny rejestr | Jeden mechanizm dla backendu i UI; host nie ma listy pluginów w kodzie |
| `SlotHost` z `PluginRuntime.Services` (statyczny provider) | Kontrolki Avalonii nie mają DI; `App.Services` już jest statyczne w tym projekcie. Jedno miejsce użycia lokalizatora, w Sdk |
| Widoki pluginów przez `CreateView()`, nie `ViewLocator` | `ViewLocator` używa `Type.GetType(nazwa)` — nie znajduje typów z innego asemblera. `ViewLocator` do usunięcia (nieużywany) |
| Edytor nowej notatki i widok notatki zostają w rdzeniu | Bez nich aplikacja nie ma sensu; „plugin, którego nie można wyłączyć" to fikcja. Za to oba mają sloty, więc ich rozszerzanie idzie przez pluginy |
| Konfiguracja pluginu w `appsettings.json`, sekcja per plugin, bez UI ustawień | Zgodne z decyzją o per-provider HttpClient (`PLAN.md` sekcja 3); UI ustawień to osobna funkcja, jeśli w ogóle |
| CLI ładuje backend pluginów | Inaczej `add` w CLI i w Desktopie dawałyby różne wyniki (bez słownika, bez sprzeczności) — a CLI służy właśnie do testowania tego pipeline'u |
| `SecondBrain.Plugins` referencuje `Infrastructure` | Pluginy potrzebują `NotePipeline`, `NoteSearch`, `NotesRoot`, opcji HttpClient — to serwisy hosta, nie „wewnętrzne szczegóły". Osobny projekt „SecondBrain.Application" byłby pustą warstwą |

## 5. Plan wykonania

Każde zadanie kończy się commitem; zadania w jednej partii mogą iść równolegle w osobnych
`git worktree` (wzorzec z `PLAN.md` sekcja 3: kopia gitignorowanego `appsettings.json`,
własny prefiks folderu testowego). Regiony `// ---- Nazwa ----` w `MainViewModel.cs`
i `DockPanel IsVisible="{Binding IsXxxTabActive}"` w XAML są rozłączne, więc równoległe
usuwanie różnych funkcji scala się bez konfliktów; konflikty zostają w konstruktorze VM,
`InitializeAsync` i `OnSelectedTabIndexChanged` — mechaniczne.

### Faza 0 — fundament (sekwencyjnie, solo)

**F0.1 `NotePipeline` + `NoteSearch` + `NotesRoot`.** Dokładnie P1 z
`PLAN-AGENT-PLUGINS.md` (wspólny krok obu planów — wykonać raz). Na tym etapie pipeline
jeszcze woła słownik/sprzeczności/luki wprost.
*Odbiór:* jak w P1 tamtego dokumentu.

**F0.2 Szyna zdarzeń.** `Core/Events.cs` (zdarzenia z 2.3), `IEventBus`, `IEventHandler<T>`;
`Infrastructure/EventBus.cs`; `NotePipeline` publikuje zdarzenia i zwraca `Notices`.
Dzisiejsze reakcje (słownik, sprzeczności+fakty, `GapAutoCloser`, commit gita) stają się
handlerami **w Infrastructure** (tymczasowo, do migracji w fazie 1), rejestrowanymi
w `AddSecondBrainInfrastructure`. `GitBackedNoteStore` → handler `GitCommitOnChange`
nasłuchujący wszystkich zdarzeń mutacji; `FileNoteStore` rejestrowany wprost.
*Odbiór:* CLI `add` z definicją, ze sprzecznością, z otwartą luką — te same komunikaty
co przed zmianą; `git log` w katalogu notatek pokazuje commit po `add`/`delete-note`/
`restore`; Desktop: zapis notatki pokazuje te same linie statusu.

**F0.3 SDK + menedżer + powłoka.** Projekt `SecondBrain.Plugins.Sdk` (kontrakty z 2.2,
`NoteItem`, `Converters/` przeniesione z Desktop, `SlotHost`, `PluginRuntime`); pusty
projekt `SecondBrain.Plugins` (z jednym pluginem-próbką `hello`, usuwanym w F2);
`PluginManager` + `plugins.json`; `MainWindow.axaml`: pasek z `ItemsControl`,
`ContentControl` dla zakładek, wszystkie `SlotHost` z 2.5 (puste); `IShell`/`IEditorContext`
zaimplementowane przez `MainViewModel` (albo małe klasy delegujące do niego); tray z
kontrybucjami; zakładka „Wtyczki"; `Program.Main` i CLI wg 2.4.
*Odbiór:* aplikacja wygląda i działa identycznie (zrzuty ekranu przed/po: sidebar, pasek,
9 zakładek); zakładka „Wtyczki" pokazuje `hello`, wyłączenie + restart ukrywa jego
zakładkę, włączenie przywraca; `plugins.json` zawiera `{"disabled":["hello"]}`.

### Faza 1 — migracja funkcji (cztery partie, w partii równolegle)

Reguła migracji jednego pluginu: (1) utwórz folder w `SecondBrain.Plugins`, (2) przenieś
kod (VM region, XAML panel, code-behind, magazyn z `FileNoteStore`, handler z F0.2,
opcje/HttpClient z `Options.cs`/`ServiceCollectionExtensions.cs`), (3) usuń z hosta,
(4) `INoteStore` traci metody tej funkcji, (5) odbiór. Nie „poprawiaj przy okazji" —
zachowanie 1:1, łącznie z tekstami.

**Partia A — zakładki z własnym magazynem** (rozłączne regiony; równolegle):
`timeline`, `glossary`, `gaps`, `history`, `trash`.
*Odbiór wspólny:* każda zakładka działa jak przed; wyłączenie pluginu usuwa przycisk
i zakładkę; `gaps`: licznik na przycisku aktualizuje się po wyszukiwaniu bez odpowiedzi
(zdarzenie `SearchCompleted` publikowane tymczasowo z regionu Szukaj w VM, do przeniesienia
w partii B) i po auto-domknięciu; `history`: commit po każdej mutacji notatek **i** po
zmianie w `.gaps`/`.glossary` (przez `StorageChanged`); `trash`: „Usuń" w widoku notatki
i w wynikach działa przez sloty.

**Partia B — edytor i szukaj** (sloty edytora + region Szukaj; równolegle, `search` osobno
od reszty, bo rusza `SearchCompleted`): `search`, `templates`, `import`, `ocr`, `rewrite`.
*Odbiór:* Ctrl+F otwiera Szukaj (skrót z kontrybucji); „Szukaj obrazem" widoczne tylko
gdy `ocr` włączony; tray „Obrazek ze schowka (OCR)" znika po wyłączeniu `ocr`; import
pliku i folderu tworzy notatki i publikuje `ImportCompleted` (luki domknięte raz);
`PasteImage_Click`, `ImportFile_Click`, `ImportFolder_Click`, `BuildTemplateButtons`
zniknęły z `MainWindow.axaml.cs`.

**Partia C — widok notatki i drzewo**: `tags`, `backlinks`.
*Odbiór:* chip tagu zawęża drzewo (`IShell.TreeFilter` + `RefreshTreeAsync`), baner „tag: x"
nad drzewem z „×"; auto-tagowanie z sąsiadów działa (handler `NoteCompressed`) i znika po
wyłączeniu `tags` (wtedy tagi = propozycja LLM albo ręczne); `BuildTagChips` zniknęło
z code-behind; backlinki pod notatką jak dziś.

**Partia D — bez UI lub z własnym oknem**: `conflicts`, `duplicates`, `quicknote`.
*Odbiór:* `conflicts` wyłączony → zapis notatki bez komunikatu o sprzeczności, bez
`.facts`, sekcja `ConflictDetection` w `appsettings` nieużywana; `quicknote`: tray
„Wpisz..." i „Ze schowka" per folder, okno działa na `IEditorContext`; `duplicates`:
CLI `find-duplicates` działa przez serwis pluginu.

### Faza 2 — sprzątanie

1. `INoteStore` ma tylko metody z 2.6; `FileNoteStore` bez `.gaps/.glossary/.facts/.templates`;
   `Options.cs` i `ServiceCollectionExtensions.cs` bez sekcji przeniesionych do pluginów;
   `ViewLocator.cs` usunięty; plugin `hello` usunięty.
2. `MainViewModel.cs` ≤ 600 linii (drzewo, foldery, edytor, notatka, zakładki, agent, sesje).
3. `PLAN.md`: sekcja 2 (drzewo projektów), sekcja 3 (wiersze z tabeli w sekcji 4 tego
   dokumentu), sekcja 5 (punkty z 2.4 i sekcji 6 tego dokumentu). `README.md`: „Jak
   napisać plugin" (folder, `IPlugin`, kontrybucje, zdarzenia, sloty, konfiguracja)
   i „Menedżer wtyczek".
4. Test dymny całości: wyłączyć **wszystkie** pluginy → aplikacja startuje, drzewo,
   edytor (zapis notatki bez słownika/sprzeczności/luk), widok notatki, pin, foldery,
   agent działają; włączyć wszystkie → pełna funkcjonalność.

*Odbiór końcowy:* dodanie nowej funkcji (np. zakładki „Przypomnienia") wymaga wyłącznie
nowego folderu w `SecondBrain.Plugins` — zero zmian w `Desktop`, `Infrastructure`, `Core`
(wyjątek: nowy slot = jedna linia `SlotHost` w XAML hosta).

## 6. Czego nie robić

- Nie ładować pluginów z dysku (`AssemblyLoadContext`, folder `plugins/`).
- Nie robić przełączania na żywo — restart to decyzja, nie brak czasu.
- Nie wprowadzać MEF, Prism, ReactiveUI, MediatR ani innej biblioteki do pluginów/zdarzeń —
  kontrakty i szyna to łącznie ~150 linii własnego kodu.
- Nie dzielić pluginu na dwa projekty (backend/UI) ani nie tworzyć projektu na plugin,
  dopóki plugin nie potrzebuje własnej zależności NuGet.
- Nie budować UI ustawień pluginów — konfiguracja w `appsettings.json` jak dotąd.
- Nie dodawać grafu zależności między pluginami, dopóki nie ma twardej zależności.
- Nie dotykać agenta (zakładka, `FabrykaAgent`, sesje, @-wzmianki) — osobny plan.
- Nie „ulepszać" migrowanych funkcji w trakcie migracji — teksty, kolejność, zachowanie 1:1;
  usprawnienia jako osobne zadania po fazie 2.
- Nie przenosić edytora nowej notatki ani widoku notatki do pluginu.
- Nie rozbijać `MainViewModel` na osobne VM poza tym, co wynika z migracji — po fazie 2
  zostaje jeden VM rdzenia (decyzja z `PLAN.md` sekcja 5 nadal obowiązuje).
