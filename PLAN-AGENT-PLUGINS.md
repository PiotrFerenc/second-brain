# PLAN-AGENT-PLUGINS — strategia pluginów i refaktoryzacja agenta

Dokument roboczy dla agentów kodujących, uzupełnienie `PLAN.md` (sekcje 3 i 5 tamtego
dokumentu nadal obowiązują). Cel: dać aplikacji system pluginów bez rozbudowy własnego
formatu, a wcześniej uporządkować kod agenta tak, żeby pluginy miały się do czego podpiąć.

## Stan realizacji (2026-09-30)

Krok 0, P1 (wspólny z `PLAN-PLUGINS.md`), P2, P3 i P4 wykonane i scalone na `master`.

- `Agent.cs` 874 → 150 linii; 43 narzędzia (42 stare + nowe `list_skills`) jako klasy w
  `Infrastructure/AgentTools/`; złoty plik `tools` przed/po identyczny, 26 mutujących.
- `McpToolSource` na `ModelContextProtocol.Core` 2.2.0 (typy: `StdioClientTransport`,
  `McpClient.CreateAsync/ListToolsAsync/CallToolAsync`, `ToolAnnotations.ReadOnlyHint`),
  konfiguracja `Plugins:Mcp` (`PluginsOptions.Mcp` jako lista), CLI `mcp-tools`/`mcp-call`,
  odebrane na `@modelcontextprotocol/server-filesystem`.
- Odstępstwa: skan asemblera rejestruje tylko publiczne klasy `IAgentTool` (narzędzia MCP są
  prywatne, tworzone w runtime); przykład w `appsettings.Example.json` ma `Enabled: false`,
  żeby świeża kopia szablonu nie odpalała `npx`.
- Krok dodatkowy P5 (poza tym planem): narzędzia i magazyny luk/słownika/faktów/tagów
  przechodzą do swoich pluginów, `INoteStore` chudnie. To domyka ogony fazy 2 z
  `PLAN-PLUGINS.md`.

## 0. W skrócie

- Pluginy w trzech warstwach, od najtańszej: **skille `.md`** (już są, zostają bez zmian),
  **narzędzia wbudowane `IAgentTool`** (jedna klasa = jedno narzędzie, kompilowane w repo),
  **zewnętrzne serwery MCP** (Model Context Protocol, konfigurowane w `appsettings.json`,
  bez pisania kodu). Nic więcej: bez ładowania DLL z dysku, bez pluginów UI, bez własnego
  formatu opisu narzędzi.
- Zanim MCP ma sens, trzeba rozbić `FabrykaAgent` (874 linie, 42 narzędzia w jednym
  `switch`) i wyciągnąć zduplikowany 7 razy pipeline zapisu notatki do jednej klasy.
- Kolejność: krok 0 (kontrakt, jeden mały commit) → P1 `NotePipeline` (solo) →
  P2 rozbicie narzędzi ∥ P3 klient MCP (równolegle, osobne worktree) → P4 dokumentacja.

## 1. Diagnoza — co dziś blokuje pluginy

Stan kodu na commit `467e64d`:

1. **`SecondBrain.Infrastructure/Agent.cs` (`FabrykaAgent`) ma 874 linie i 42 narzędzia.**
   Dodanie jednego narzędzia wymaga edycji czterech miejsc w tej samej klasie: tablica
   `ToolDefinitions` (JSON jako string), zbiór `MutatingTools`, `switch` w `DescribeAction`
   (tekst pytania Tak/Nie) i `switch` w `ExecuteToolAsync`. Zewnętrzny plugin nie ma
   żadnego punktu, w który mógłby się wpiąć — musiałby modyfikować tę klasę.
   Liczby do kontroli po refaktorze: 42 narzędzia, w tym 26 mutujących (wymagających
   potwierdzenia) i 16 tylko do odczytu.
2. **Pipeline zapisu notatki jest skopiowany 7 razy.** Sekwencja kompresja → zapis pliku →
   wpisy do słownika → embedding → upsert do indeksu (→ powiązane notatki → wykrywanie
   sprzeczności → wersje faktów → domykanie luk) żyje w:
   - `MainViewModel.SaveNoteAsync`, `ImportLinesAsync`, `SaveNoteEditAsync`,
   - `FabrykaAgent` `add_note`, `EditNoteCoreAsync`, `BulkCreateNotesAsync`,
   - `PipelineTest/Program.cs` `AddNoteAsync`.

   Kopie już się rozjechały: auto-tagowanie z sąsiadów tylko w UI, sprawdzanie sprzeczności
   w `add_note` tak, ale w `bulk_*` nie, CLI liczy powiązane notatki po upsercie (musi
   filtrować własne Id), UI przed. Mniejsze kopie tego samego wzorca „zapis + embedding +
   upsert" są w drag&drop drzewa, `set_note_pinned`, `bulk_pin`, `bulk_tag`, `move_note`,
   oraz „kosz + usunięcie z indeksu" w UI, agencie i CLI.
3. **UI odświeża się po agencie ręcznie** (`ConfirmAgentActionAsync` woła `LoadTreeAsync`,
   `LoadTrashAsync`, `LoadGapsAsync`, `LoadGlossaryAsync`), bo agent omija komendy VM.
   Zostaje tak — patrz sekcja 4 („czego nie robić").
4. **Skille `.md` to działający plugin promptowy** (`Skills/` w repo + `.skills/` w katalogu
   notatek, nadpisywanie po nazwie, hot reload, `use_skill`). To wzorzec do zachowania:
   plik na dysku, zero kodu, natychmiastowy efekt.
5. **Dryf dokumentacji:** tabela decyzji w `PLAN.md` wymienia 7 narzędzi mutujących,
   kod ma 26. Po refaktorze lista nie może istnieć w dwóch miejscach — flaga ma być na
   narzędziu (patrz P2), dokumentacja ma opisywać regułę, nie listę.

## 2. Strategia pluginów

### 2.1. Trzy warstwy

| Warstwa | Czym jest | Kto pisze | Jak się podpina | Status |
| --- | --- | --- | --- | --- |
| W0 — skill | Plik `.md`: pierwsza linia = opis, reszta = instrukcja | użytkownik | `Skills/` (repo) lub `<notatki>/.skills/` | **jest**, bez zmian |
| W1 — narzędzie wbudowane | Klasa implementująca `IAgentTool` | my, w repo | rejestracja DI przez skan asemblera | do zrobienia (P2) |
| W2 — serwer MCP | Dowolny serwer MCP (stdio) | społeczność / użytkownik | sekcja `Plugins:Mcp` w `appsettings.json` | do zrobienia (P3) |

W0 rozszerza *zachowanie* agenta (jak używać narzędzi), W1 i W2 rozszerzają *zestaw
narzędzi*. Wszystkie trzy trafiają do jednej listy widocznej dla modelu; agent nie
rozróżnia skąd narzędzie pochodzi.

### 2.2. Kontrakt narzędzia (`SecondBrain.Core`)

```csharp
public interface IAgentTool
{
    string Name { get; }                       // [a-zA-Z0-9_-]{1,64} — wymóg API chat/completions
    string Description { get; }
    JsonObject ParametersSchema { get; }       // JSON Schema obiektu "parameters"
    bool IsMutating { get; }                   // true = karta Tak/Nie przed wykonaniem
    string Describe(JsonElement args);         // tekst pytania Tak/Nie (tylko gdy IsMutating)
    Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default);
}

// Źródło narzędzi odkrywanych w runtime (MCP). Wbudowane narzędzia nie potrzebują
// źródła — idą prosto z DI jako IEnumerable<IAgentTool>.
public interface IAgentToolSource
{
    Task<IReadOnlyList<IAgentTool>> ListAsync(CancellationToken ct = default);
}
```

`AgentToolRegistry` (Infrastructure) skleja `IEnumerable<IAgentTool>` z DI i wyniki
wszystkich `IAgentToolSource`, cache'uje wynik źródeł po pierwszym udanym wywołaniu
i **izoluje awarie**: źródło, które rzuci wyjątkiem, jest logowane i pomijane, agent
działa dalej na pozostałych narzędziach. Kolizja nazw → wygrywa narzędzie wbudowane,
duplikat z MCP logowany i pomijany.

`FabrykaAgent` po refaktorze zna tylko rejestr: buduje `tools` dla API z
`ParametersSchema`, sprawdza `IsMutating`, woła `Describe` i `ExecuteAsync`. Pętla,
potwierdzenia, `MaxToolHops`, katalog skilli w prompcie — bez zmian.

### 2.3. MCP jako format pluginów zewnętrznych

Dlaczego MCP zamiast własnego formatu (JSON/YAML opisujący wywołanie HTTP albo komendę):

| | Własny format | MCP |
| --- | --- | --- |
| Kod do napisania | parser + wykonawca + schemat + dokumentacja | klient z gotowego pakietu |
| Gotowe pluginy | zero | filesystem, git, fetch, czas, kalendarze, bazy danych, setki innych |
| Schemat argumentów | trzeba wymyślić | JSON Schema w `tools/list`, 1:1 z tym, co już wysyłamy do API |
| Flaga „tylko odczyt" | trzeba wymyślić | `annotations.readOnlyHint` w specyfikacji |
| Utrzymanie | nasze | Anthropic + Microsoft (pakiet .NET oficjalny) |

Pakiet: `ModelContextProtocol.Core` **2.2.0** (stabilny, NuGet, sprawdzone 2026-09-30).
`.Core` zamiast pełnego `ModelContextProtocol`, bo pełny ciągnie `Microsoft.Extensions.Hosting`
i rejestracje serwera, których Desktop nie używa. Pakiet zależy od
`Microsoft.Extensions.AI.Abstractions` — korzystamy **wyłącznie** z klienta MCP
(`McpClient`, `ListToolsAsync`, `CallToolAsync`), **nie** z `IChatClient` (patrz sekcja 4).

Zasady mapowania serwera MCP na `IAgentTool`:

- Nazwa: `{serwer}__{narzędzie}` (dwa podkreślenia), znaki spoza `[a-zA-Z0-9_-]`
  zamieniane na `_`, przycięcie do 64 znaków. Prefiks jest potrzebny, bo dwa serwery mogą
  mieć narzędzie o tej samej nazwie (`read_file`).
- `IsMutating = !(annotations.readOnlyHint == true)`. **Domyślnie mutujące** — brak
  adnotacji oznacza potwierdzenie Tak/Nie, nie zaufanie. `destructiveHint=false` nie
  zwalnia z potwierdzenia.
- `Describe` generyczne: `"[{serwer}] {narzędzie} z argumentami {json}?"` — serwer nie
  daje polskiego opisu, a tłumaczyć przez LLM nie ma sensu (kolejne wywołanie za jedno
  pytanie).
- Wynik: bloki `text` z `CallToolResult.Content` sklejone `\n`; blok innego typu
  (obraz, zasób) zastępowany `"[{typ}]"`. `isError=true` → treść błędu zwracana modelowi
  jako zwykły wynik (model ma szansę zareagować), nie wyjątek.
- Transport: tylko stdio (proces uruchamiany przez aplikację). HTTP/SSE dopiero na
  wyraźną prośbę — na jednej maszynie dla jednego użytkownika stdio wystarcza.
- Cykl życia: proces startuje przy pierwszym wywołaniu rejestru (pierwsza wiadomość do
  agenta), żyje do zamknięcia aplikacji. Zmiana konfiguracji = restart aplikacji
  (świadomie inaczej niż skille — proces zewnętrzny to nie plik do przeczytania).

Konfiguracja (`appsettings.json`, sekcja nowa; `appsettings.Example.json` z jednym
zakomentowanym przykładem):

```json
"Plugins": {
  "Mcp": [
    {
      "Name": "filesystem",
      "Command": "npx",
      "Args": ["-y", "@modelcontextprotocol/server-filesystem", "/home/piotr/Dokumenty"],
      "Env": {},
      "Enabled": true
    }
  ]
}
```

### 2.4. Czego świadomie nie budujemy

- **Ładowania pluginów jako DLL** (`AssemblyLoadContext`). Wymagałoby stabilnego,
  wersjonowanego kontraktu `Core`, izolacji zależności i kanału dystrybucji — dla jednego
  użytkownika, który ma repo, „plugin kompilowany" to po prostu nowa klasa `IAgentTool`
  w `Infrastructure/AgentTools/`. Ceiling: gdyby pojawił się drugi użytkownik bez dostępu
  do repo, wtedy DLL albo (prościej) serwer MCP napisany w dowolnym języku.
- **Pluginów UI** (własne zakładki Avalonia z zewnątrz). Za drogie, a potrzeba nie istnieje.
- **Hooków zdarzeniowych** (`NoteSaved` itp.), do których pluginy mogłyby się podpinać.
  Jedyne dzisiejsze użycie to ręczne odświeżenie UI po agencie, które działa. Dodać
  dopiero, gdy pojawi się drugi konsument (np. plugin reagujący na zapis notatki).
- **Hot reloadu konfiguracji MCP** — restart aplikacji wystarcza.
- **Routowania wbudowanych narzędzi przez MCP** (aplikacja jako serwer MCP dla samej
  siebie). In-process jest prostsze, szybsze i nie wymaga serializacji stanu. Wystawienie
  Second Brain jako serwera MCP dla innych klientów (np. Claude Desktop) to osobna,
  przyszła funkcja — wtedy narzędzia `IAgentTool` mapują się 1:1 na `tools/list`, więc
  ten refaktor jej nie utrudnia.

## 3. Plan refaktoryzacji — zadania

Każde zadanie ma kryteria odbioru. Kolejność i równoległość opisane w sekcji 3.6.
Wzorzec pracy równoległej jak w `PLAN.md` (sekcja 3, ostatni wiersz): osobne `git worktree`,
skopiowany gitignorowany `appsettings.json`, unikalny prefiks folderu testowego.

### Krok 0 — kontrakt (jeden mały commit na `master`, przed wszystkim)

1. `SecondBrain.Core/AgentTools.cs`: `IAgentTool`, `IAgentToolSource` (jak w 2.2) oraz
   statyczna klasa `ToolArgs` z helperami przeniesionymi z `FabrykaAgent.ExecuteToolAsync`
   (`Req`, `Opt`, `ReqArr`, `OptArr`, `Bool`) jako metody rozszerzające `JsonElement`.
2. `SecondBrain.Infrastructure/AgentTools/AgentToolRegistry.cs`: klasa jak w 2.2
   (sklejanie, cache, izolacja awarii, kolizje nazw). Około 40 linii.
3. `ServiceCollectionExtensions.cs`: rejestracja `AgentToolRegistry` oraz skan asemblera
   Infrastructure rejestrujący każdą niebędącą abstrakcyjną klasę implementującą
   `IAgentTool` jako `IAgentTool` (singleton). Jedna pętla, nie lista 42 linii `AddSingleton`.
   Na tym etapie skan nic nie znajduje — to jest w porządku.

**Odbiór:** `dotnet build SecondBrain.slnx` czysty; zachowanie aplikacji bez zmian
(nic jeszcze nie używa nowych typów).

### P1 — `NotePipeline`: jedno miejsce dla cyklu życia notatki

Nowa klasa `SecondBrain.Infrastructure/NotePipeline.cs` (bez interfejsu — jedna
implementacja, jak `GapAutoCloser`/`TagMerger`), wstrzykiwana do `MainViewModel`,
`FabrykaAgent` (a po P2 do narzędzi) i `PipelineTest`.

```csharp
public record AddNoteResult(Note Note, IReadOnlyList<Note> Related, ConflictResult? Conflict, int ClosedGaps);

public sealed class NotePipeline(ICompressor, IEmbedder, IVectorIndex, INoteStore, IConflictDetector, GapAutoCloser)
{
    // Pełny cykl: kompresja → zapis → słownik → embedding → upsert → powiązane →
    // (opcjonalnie) sprzeczność + wersje faktów → (opcjonalnie) domykanie luk.
    Task<AddNoteResult> AddAsync(string folder, string rawText, string[]? tags = null, Guid? parentId = null,
                                 bool detectConflicts = true, bool closeGaps = true, CancellationToken ct = default);
    Task<AddNoteResult> EditAsync(string folder, Note existing, string rawText, CancellationToken ct = default);
    Task<int> ImportAsync(string folder, IReadOnlyList<string> lines, CancellationToken ct = default); // bez sprzeczności, luki raz na koniec
    Task<Note> ReindexAsync(string folder, Note note, CancellationToken ct = default);   // zapis + embedding + upsert (pin, tagi, rodzic)
    Task<Note> MoveAsync(string fromFolder, string toFolder, Note note, CancellationToken ct = default);
    Task TrashAsync(string folder, Note note, CancellationToken ct = default);          // usunięcie z indeksu + kosz
    Task<TrashedNote> RestoreAsync(string trashPath, CancellationToken ct = default);   // kosz → plik + embedding + upsert
}
```

Zasady:

- Zachowanie każdego wywołującego ma zostać **takie samo jak dziś**, łącznie z różnicami:
  auto-tagowanie z sąsiadów zostaje w `MainViewModel` (VM przekazuje gotowe `tags`,
  `AddNoteResult.Related` daje mu sąsiadów do `SuggestTags` — jeśli potrzebna kolejność
  „najpierw sąsiedzi, potem zapis", `AddAsync` przyjmuje `Func<IReadOnlyList<Note>, string[]>`
  zamiast `tags`; wybrać prostsze po przeczytaniu `SaveNoteAsync`). `bulk_*` i import bez
  wykrywania sprzeczności (`detectConflicts: false`), luki domykane raz na koniec importu.
- Statusy UI („Kompresuje...", „Licze embedding...") nie wchodzą do pipeline'u. VM ustawia
  jeden status przed i jeden po; dokładność co do kroku nie jest warta parametru
  `IProgress<string>`. Jeśli użytkownik zauważy i poprosi — wtedy dodać.
- `MoveNoteCoreAsync` (walidacja rodzica, potomków, przenoszenie między folderami)
  przechodzi z `FabrykaAgent` do `NotePipeline.MoveAsync` w całości.

**Odbiór:**

1. `grep -rn "CompressAsync\|UpsertAsync" --include=*.cs SecondBrain.Desktop SecondBrain.PipelineTest SecondBrain.Infrastructure/Agent.cs`
   zwraca tylko wywołania wewnątrz `NotePipeline.cs`, `GapAutoCloser.cs`, `TagMerger`
   i `DuplicateScanner.cs` (te trzy nie tworzą notatek — zostają). W VM, CLI i agencie zero
   bezpośrednich wywołań `ICompressor`/`IVectorIndex.UpsertAsync`.
2. `MainViewModel` nie ma już w konstruktorze `ICompressor`, `IConflictDetector`,
   `GapAutoCloser` (dostaje `NotePipeline`); `IEmbedder`/`IVectorIndex` zostają tylko do
   wyszukiwania i folderów.
3. CLI end-to-end na żywym providerze: `add` (z definicją → słownik; ze sprzecznością →
   `UWAGA` + `fact-history`), `import`, `delete-note`/`restore`, `search` — identyczne
   wyjścia jak przed refaktorem.
4. Desktop: zapis nowej notatki, edycja, import z pliku, drag&drop zagnieżdżenia,
   pin, kosz/przywróć — wszystko działa (zrzut ekranu drzewa po każdej akcji, jak dotąd).
5. Agent przez `dotnet run -- agent`: `add_note` z „t" zapisuje i indeksuje, `edit_note`,
   `bulk_note_create`, `move_note`, `trash_note` + `restore_note`.

### P2 — rozbicie `FabrykaAgent` na narzędzia `IAgentTool`

1. **Przed zmianami** zrzuć złoty plik: w `PipelineTest` dodaj komendę `tools`, która
   drukuje tablicę `tools` dokładnie w formie wysyłanej do API (JSON, posortowane po
   `function.name`) oraz w trybie `tools --mutating` listę nazw z flagą mutujące/odczyt.
   Zapisz wynik do `/tmp/tools-before.json` i `/tmp/tools-before.txt`. Na tym etapie
   komenda czyta jeszcze stare `ToolDefinitions`/`MutatingTools` (zrób je tymczasowo
   `internal`).
2. Każde z 42 narzędzi → klasa `IAgentTool` w `SecondBrain.Infrastructure/AgentTools/`,
   grupowane po domenie w plikach (kilka małych klas na plik, nie 42 pliki):
   `FolderTools.cs`, `NoteTools.cs`, `SearchTools.cs`, `TrashTools.cs`, `GapTools.cs`,
   `GlossaryTools.cs`, `TagTools.cs`, `SkillTools.cs`, `BulkTools.cs`. `Name`, `Description`
   i `ParametersSchema` przepisane 1:1 z JSON-a, `IsMutating` z `MutatingTools`,
   `Describe` z `DescribeAction`, `ExecuteAsync` z odpowiedniego `case`. Treści komunikatów
   po polsku bez zmian — to część kontraktu z użytkownikiem i ze skillami (`Skills/*.md`
   opisują dokładnie te wyniki).
3. `SearchAcrossAsync` (wspólne dla `search_notes`, `ask_question`) → statyczna metoda w
   `HybridNoteSearch` albo mała klasa `NoteSearch` w Infrastructure, wstrzykiwana do obu
   narzędzi. Nie duplikować.
4. `FabrykaAgent` zostaje z: `SendAsync`, `ConfirmAsync`, `RunLoopAsync`,
   `CallChatCompletionsAsync`, `BuildSystemPromptAsync`, serializacja stanu. Narzędzia
   z `AgentToolRegistry.ListAsync` na początku każdej tury (cache w rejestrze). Tablica
   `tools` dla API budowana z `Name`/`Description`/`ParametersSchema`. Nieznana nazwa
   narzędzia → ten sam komunikat co dziś (`Nieznane narzedzie: ...`).
5. Komenda `tools` w CLI przełączona na rejestr.

**Odbiór:**

1. `Agent.cs` poniżej 200 linii; brak w nim stringów z nazwami narzędzi poza logiką pętli.
2. `dotnet run -- tools > /tmp/tools-after.json` → `diff /tmp/tools-before.json /tmp/tools-after.json`
   pusty. To samo dla `--mutating` (42 narzędzia, 26 mutujących).
3. `dotnet run -- agent`: scenariusze z odbioru P1 pkt 5 plus `use_skill porzadkowanie-tagow`
   (skill wywołuje `find_duplicate_tags` → `merge_tags` z potwierdzeniem) i `search_notes`
   bez folderu.
4. Dodanie nowego narzędzia = jedna nowa klasa, zero zmian w `Agent.cs`, DI i CLI.
   Sprawdzić na próbę narzędziem `list_skills` (odczyt, zwraca nazwy+opisy skilli) — może
   zostać w kodzie, jest użyteczne i nie ma go dziś.

### P3 — klient MCP (`IAgentToolSource`)

1. `SecondBrain.Infrastructure.csproj`: `ModelContextProtocol.Core` 2.2.0.
2. `Options.cs`: `McpServerOptions { Name, Command, Args (string[]), Env (Dictionary), Enabled = true }`,
   rejestracja `services.Configure<List<McpServerOptions>>(config.GetSection("Plugins:Mcp"))`
   (albo klasa `PluginsOptions { List<McpServerOptions> Mcp }` — wybrać to, co
   `Microsoft.Extensions.Options` binduje bez kombinowania).
3. `SecondBrain.Infrastructure/AgentTools/McpToolSource.cs`: jedno `IAgentToolSource` dla
   wszystkich serwerów. Dla każdego `Enabled`: `StdioClientTransport` → `McpClient.CreateAsync`
   → `ListToolsAsync` → mapowanie na `McpAgentTool : IAgentTool` wg zasad z 2.3 (nazwy
   typów wg API pakietu 2.x — zweryfikować w źródłach pakietu, nie zgadywać). Serwer,
   który nie wystartuje lub nie odpowie w `TimeoutSeconds` (domyślnie 30), jest logowany
   (Serilog/stderr, jak `GitBackedNoteStore`) i pomijany; pozostałe działają. Klienci
   trzymani jako pola, zamykani w `IAsyncDisposable` (Desktop: przy zamknięciu `ServiceProvider`).
4. `McpAgentTool.ExecuteAsync`: `CallToolAsync(name, argumentsDictionary)`; argumenty z
   `JsonElement` → `Dictionary<string, object?>` przez deserializację
   (`JsonSerializer.Deserialize<Dictionary<string, object?>>`), wynik wg 2.3.
5. `appsettings.Example.json`: sekcja `Plugins` z jednym przykładem `filesystem`
   (`npx -y @modelcontextprotocol/server-filesystem <katalog>`); `README.md`: sekcja
   „Pluginy MCP" (jak dodać serwer, prefiks nazw, reguła potwierdzeń).

Środowisko: na tej maszynie są `node` v26.8.1, `npx`, `uvx`, `python3` — serwer
`@modelcontextprotocol/server-filesystem` (npx) albo `mcp-server-git` (uvx) nadają się do
odbioru bez instalowania czegokolwiek ręcznie.

**Odbiór:**

1. Z serwerem `filesystem` wskazanym na katalog testowy: `dotnet run -- tools` pokazuje
   narzędzia `filesystem__list_directory`, `filesystem__read_file` jako **odczyt** (serwer
   ustawia `readOnlyHint`) i `filesystem__write_file` jako **mutujące**.
2. `dotnet run -- agent`, „wylistuj pliki w katalogu X" → agent woła
   `filesystem__list_directory` bez pytania i odpowiada listą. „Zapisz plik test.txt z
   treścią ..." → karta Tak/Nie z opisem `[filesystem] write_file ...`; „t" tworzy plik,
   „n" nie.
3. Z celowo błędnym `Command` (np. `npx-nie-ma`) w drugim wpisie: aplikacja i CLI
   startują, w logu ostrzeżenie z nazwą serwera, narzędzia pierwszego serwera i wbudowane
   działają.
4. Bez sekcji `Plugins` w ogóle: zachowanie identyczne jak przed P3 (zero procesów
   potomnych, `tools` = 42 wbudowane + ewentualne nowe z P2).
5. Desktop: zakładka Agent pokazuje te same potwierdzenia dla narzędzi MCP co dla
   wbudowanych (nic w VM nie trzeba zmieniać — `AgentPendingAction.Summary` już to niesie).

### P4 — dokumentacja i porządek po refaktorze

1. `PLAN.md` sekcja 2 (drzewo plików): `Agent.cs` → pętla; `AgentTools/` → narzędzia
   i rejestr; `NotePipeline.cs`; `McpToolSource.cs`. Sekcja 3: wiersz o „liście
   mutujących: create_folder, add_note, …" zastąpić regułą „`IAgentTool.IsMutating`;
   narzędzia MCP mutujące, o ile nie mają `readOnlyHint`". Wiersz „Nie buduj warstwy
   abstrakcji nad providerami LLM" w sekcji 5 uzupełnić o „`Microsoft.Extensions.AI`
   przychodzi jako zależność MCP — nie używać `IChatClient` do własnych wywołań".
2. Sekcja 5 `PLAN.md` — dopisać punkty z 2.4 tego dokumentu (DLL, UI, hooki, hot reload MCP).
3. `README.md`: obok „Skille agenta" sekcje „Narzędzia wbudowane" (jak dodać klasę) i
   „Pluginy MCP".
4. Ten plik: przenieść zrealizowane zadania do sekcji „Stan" albo usunąć plik i zostawić
   tylko wpisy w `PLAN.md` — decyzja użytkownika; domyślnie zostaje jako historia decyzji.

**Odbiór:** nowy agent kodujący, czytając tylko `PLAN.md` + `README.md`, wie gdzie dodać
narzędzie wbudowane i jak podpiąć serwer MCP, bez zaglądania w ten dokument.

### 3.6. Kolejność i równoległość

```
krok 0 (master, 1 commit)
   │
   ▼
P1 NotePipeline (solo — dotyka Agent.cs, MainViewModel.cs, PipelineTest/Program.cs)
   │
   ├──────────────────────────────┐
   ▼                              ▼
P2 rozbicie narzędzi          P3 klient MCP
(worktree feature/agent-tools) (worktree feature/mcp)
   │                              │
   └──────────────┬───────────────┘
                  ▼
        merge do master (P2 najpierw, potem P3; jedyny wspólny plik to
        ServiceCollectionExtensions.cs — po jednej linii z każdej strony)
                  │
                  ▼
                 P4 dokumentacja
```

- P1 przed P2, bo po P1 `case`'y w `Agent.cs` są cienkie i przenoszenie ich do klas jest
  mechaniczne; w odwrotnej kolejności P1 musiałby edytować 9 plików narzędzi zamiast jednego.
- P2 i P3 są rozłączne dzięki krokowi 0: P3 zna tylko `IAgentToolSource` i rejestr,
  P2 tylko `IAgentTool` i pętlę agenta. P3 można odbierać na wbudowanych narzędziach
  jeszcze przed merge'em P2 (rejestr działa z pustą listą wbudowanych).
- Każde zadanie kończy się osobnym commitem z opisem zmiany zachowania (dla P1 i P2:
  „bez zmiany zachowania" plus dowód w postaci odbioru).

## 4. Czego nie robić w ramach tego planu

- Nie rozbijać `MainViewModel` na osobne ViewModels — P1 zmniejszy go o ~150 linii
  bez tego; decyzja z `PLAN.md` sekcja 5 obowiązuje.
- Nie dodawać interfejsu do `NotePipeline`, `AgentToolRegistry`, `McpToolSource` — jedna
  implementacja każdej, nic ich nie mockuje.
- Nie używać `IChatClient`/`Microsoft.Extensions.AI` do własnych wywołań LLM ani nie
  przepisywać `FabrykaCompressor` i reszty na ten model — zależność przychodzi z MCP
  i tam się kończy.
- Nie wprowadzać hooków zdarzeniowych ani auto-odświeżania UI przez zdarzenia — ręczne
  odświeżenie w `ConfirmAgentActionAsync` zostaje.
- Nie tłumaczyć opisów narzędzi MCP na polski przez LLM.
- Nie zmieniać treści komunikatów zwracanych przez narzędzia wbudowane — skille `.md`
  i użytkownik na nich polegają; złoty plik z P2 tego pilnuje dla schematów, dla treści
  pilnuje przegląd diffu.
- Nie zaczynać „Second Brain jako serwer MCP" (sekcja 2.4, ostatni punkt) bez wyraźnej prośby.
