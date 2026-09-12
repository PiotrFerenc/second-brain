# PLAN — Second Brain

Dokument roboczy dla agenta pracującego nad tym repozytorium. Zawiera kontekst, stan
faktyczny kodu, podjęte decyzje wraz z uzasadnieniem oraz kolejne zadania z kryteriami
odbioru. Pełna specyfikacja: https://claude.ai/code/artifact/be9590b5-d7a1-495b-9dfd-1e83af9b1106

## 1. Co budujemy

Aplikacja desktopowa (.NET + Avalonia, jeden użytkownik, jedna maszyna) będąca osobistą
bazą wiedzy — "inteligentniejszy OneNote". Użytkownik wpisuje notatki ręcznie; LLM
kompresuje je do zwięzłej, ustrukturyzowanej treści, która jest zamieniana na wektor
i indeksowana w Qdrant. Wyszukiwanie jest semantyczne: pytanie w języku naturalnym
zamiast słów kluczowych.

Dwie ścieżki:

- **Zapis:** wpis użytkownika → kompresja LLM → plik `.md` na dysku + embedding → upsert do Qdrant.
- **Wyszukiwanie:** zapytanie → embedding → Qdrant top-K → reranker → wynik dla użytkownika.

Foldery organizacyjne (odpowiednik notesów w OneNote) realizowane są jako osobne
kolekcje Qdrant — jedna kolekcja to jeden folder, wszystkie o identycznym schemacie wektora.

## 2. Stan obecny

Core zapisu/wyszukiwania działa end-to-end (na mockach embeddingu/rerankera). Do tego
dochodzi druga warstwa funkcji "jak w innych apkach tego typu" (OneNote/Obsidian/Roam):
drzewo notatek z podstronami, tagi, backlinki, powiązane notatki, przypinanie, szablony,
dziennik, kosz, skróty klawiszowe, renderowanie Markdown. Solucja: `SecondBrain.slnx`
(nowy XML-owy format solucji z .NET 10 SDK — nie `.sln`).

```
SecondBrain.slnx
  SecondBrain.Core/            interfejsy domenowe + modele, bez zaleznosci zewnetrznych
    Note.cs                    record Note (Id, Title, RawContent, CompressedContent, Tags,
                                CreatedAt, UpdatedAt, FilePath, ParentId, Pinned) + ScoredNote
    Interfaces.cs               ICompressor (zwraca CompressionResult: Title+CompressedContent
                                 +Tags, wszystko ustala LLM jednym wywolaniem), IEmbedder,
                                 IReranker, IAnswerSynthesizer (RAG: odpowiedz na pytanie na
                                 podstawie znalezionych notatek), INoteStore (ListAsync +
                                 kosz: MoveToTrashAsync/ListTrashAsync/RestoreFromTrashAsync/
                                 PurgeTrashAsync, TrashedNote), IVectorIndex (+ DeleteFolderAsync,
                                 DeleteNoteAsync)

  SecondBrain.Infrastructure/  implementacje, referencja do Core
    Options.cs                  HttpClientOptions (BaseAddress, TimeoutSeconds, ApiKey, Headers)
                                 + OpenAiOptions, RerankerOptions, QdrantOptions (+ApiKey/UseHttps),
                                 StorageOptions
    HttpClientHeaders.cs        wspolne nakladanie naglowkow (w tym {ApiKey}) na nazwany HttpClient
    Embedding.cs                OpenAiEmbedder (realny) + MockEmbedder (aktywny, patrz nizej)
    Compression.cs              OpenAiCompressor (chat/completions, response_format json_object,
                                 zwraca tytul+tresc+tagi jednym wywolaniem) — dziala
    AnswerSynthesis.cs           OpenAiAnswerSynthesizer (chat/completions, json_object,
                                 zwraca AnswerResult{Answered,Answer} - jawny sygnal
                                 "nie wiem" zamiast parsowania wolnego tekstu) — dziala
    Reranking.cs                MockReranker (aktywny) + CohereReranker (v2/rerank, gotowy,
                                 nieprzetestowany — provider dostepny tylko na 2. maszynie)
    NoteFileStore.cs             FileNoteStore — front matter (+parent/pinned), ListAsync
                                 (posortowane: przypiete pierwsze, potem data), kosz jako
                                 <root>/.trash/<folder>___<id>.md (folder zakodowany w nazwie)
    QdrantVectorIndex.cs         implementacja IVectorIndex na Qdrant.Client (+DeleteCollectionAsync,
                                 DeleteAsync po Guid)
    ServiceCollectionExtensions.cs  AddSecondBrainInfrastructure(config) — jedna rejestracja
                                 DI uzywana przez PipelineTest i Desktop

  SecondBrain.Desktop/         Avalonia + CommunityToolkit.Mvvm, referencja do Core+Infrastructure
    Program.cs                  buduje ServiceProvider PRZED startem Avalonii, wystawia App.Services
    App.axaml.cs                 rozwiazuje MainViewModel z App.Services zamiast `new MainViewModel()`
    ViewModels/MainViewModel.cs  jeden ViewModel na cale okno — spory (drzewo, edytor,
                                 wyszukiwanie, notatka, dziennik, kosz), ale nadal jeden plik
                                 bo commandy sie nie duplikuja, tylko rosna liczbowo
    ViewModels/SearchResultItem.cs  DTO notatki do bindowania (Id, Title, Tags, Score,
                                 RawContent, FilePath, ParentId, Pinned)
    ViewModels/TreeItem.cs       wezel drzewa sidebaru (folder lub notatka, z OwningFolder)
    ViewModels/TrashItem.cs      DTO wpisu w koszu
    Converters/FolderAccentConverter.cs  string -> kolorowa kropka folderu (Catppuccin)
    Converters/PinLabelConverter.cs  bool Pinned -> "Przypnij"/"Odepnij"
    Styles/Catppuccin.axaml      paleta Mocha/Latte jako ThemeDictionaries (Light/Dark),
                                 nadpisuje tez SystemAccentColor (mauve) dla FluentTheme
    Styles/AppStyles.axaml       Style dla Window/Button(+.accent/.danger/.subtleAction)/
                                 TextBox(+.editor)/ListBox(Item)/TabItem - FluentTheme baza
    Views/MainWindow.axaml       Grid: sidebar = TreeView (foldery jako korzenie z kolorowa
                                 kropka, notatki jako dzieci, zagniezdzone podstrony jako
                                 dzieci notatek) | "kartka" tresci z TabControl 5 zakladek:
                                 Nowa notatka (szablony, tagi, wybor notatki nadrzednej),
                                 Szukaj (+ karta odpowiedzi LLM, Markdown w wyniku), Notatka
                                 (podglad wybranej w drzewie: pin/kopiuj/usun/backlinki,
                                 Markdown), Kosz (przywroc/usun na zawsze).
                                 Wybor folderu w drzewie czysci pola edytora/wyszukiwania;
                                 wybor notatki przelacza automatycznie na zakladke "Notatka".
                                 Skroty: Ctrl+N/F/D/S.
    Views/MainWindow.axaml.cs    start (InitializeCommand), kopiowanie do schowka (wlasny
                                 IAsyncDataTransfer - Avalonia 12 usunela SetTextAsync)

  SecondBrain.PipelineTest/    cienki CLI nad Core+Infrastructure, do szybkich testow bez UI
    Program.cs                  komendy: list, create, delete-folder, seed, add, notes,
                                 delete-note (do kosza), list-trash, restore, purge, search
                                 (drukuje tez syntezowana odpowiedz LLM pod wynikami)
    SampleNotes.cs               3 przykladowe notatki do `seed`
```

Pakiet zewnętrzny: `Markdown.Avalonia.Tight` (12.0.0-a3, alpha, ale jedyny kompatybilny
z Avalonia 12 + net10.0) — renderuje notatki jako Markdown zamiast zwykłego tekstu w
zakładkach Szukaj/Notatka/Kosz. Xmlns to `.../Markdown.Avalonia.Tight` (nie bez
`.Tight` — tak jest zarejestrowane w tej wersji, sprawdzone przez reflection na dll).

Konfiguracja (`appsettings.json`, gitignorowany, `appsettings.Example.json` jako szablon)
jest zduplikowana per-aplikacja (PipelineTest i Desktop mają każdy swój, bo mają osobne
katalogi wyjściowe) — to świadome, nie efekt pomyłki.

Uruchomienie CLI:

```bash
docker run -d --name secondbrain-qdrant -p 6333:6333 -p 6334:6334 qdrant/qdrant   # jesli nie dziala
cd SecondBrain.PipelineTest
dotnet run -- list
dotnet run -- create praca
dotnet run -- add praca "tekst notatki..."
dotnet run -- notes praca
dotnet run -- delete-note praca <id>       # do kosza
dotnet run -- list-trash
dotnet run -- restore <sciezka>
dotnet run -- search praca "pytanie..."
```

Uruchomienie desktopu:

```bash
cd SecondBrain.Desktop
dotnet run
```

Stan weryfikacji:

- ✅ `dotnet build SecondBrain.slnx` — cała solucja buduje się czysto (0 warn, 0 err).
- ✅ CLI: `list`, `create`, `delete-folder`, `seed`, `add` (tytuł+tagi z LLM), `notes`,
  `delete-note`/`list-trash`/`restore`/`purge` (pełny cykl kosza), `search` (+ odpowiedź LLM)
  — wszystko przetestowane end-to-end na żywym Qdrant i plikach.
- ✅ Desktop: build czysty, proces startuje i działa bez wyjątków w logu (sprawdzone
  wielokrotnie uruchomieniem w tle + `gnome-screenshot`). To środowisko okazało się **mieć**
  działający realny display mimo wcześniejszych podejrzeń o headless — zrzuty pokazują
  faktyczną wyrenderowaną treść okna, nie czarny ekran.
- ✅ Wygląd zweryfikowany zrzutami ekranu wielokrotnie: sidebar jako `TreeView` (foldery z
  kolorową kropką jako korzenie, notatki jako rozwijane dzieci), 5 zakładek, szablony,
  pole tagów, selektor notatki nadrzędnej — wszystko renderuje się poprawnie w Catppuccin Mocha.
- ⚠️ **Nieprzeklikane ręcznie**: to środowisko nie ma `xdotool`/`wmctrl`, więc nie da się
  symulować kliknięcia — tylko zrzuty stanu spoczynkowego (po starcie, przed interakcją).
  Nie zweryfikowano interaktywnie: rozwijanie węzłów drzewa, przełączanie na zakładkę
  "Notatka" po kliknięciu notatki, faktyczne renderowanie Markdown w treści, zachowanie
  tła edytora na focus (naprawione w kodzie, ale nieklikniete). Wymaga przejścia przez
  Ciebie na Twojej maszynie — patrz Zadanie 8.
- ⏳ `OpenAiEmbedder` — kod gotowy, nadal zablokowany po stronie OpenAI (403 na
  `text-embedding-3-small` mimo widocznego dostępu — patrz decyzje, sekcja 3).
- ⏳ `CohereReranker` — kod gotowy pod wire-format Cohere v2/rerank, nieprzetestowany
  (provider dostępny tylko na drugiej maszynie użytkownika).
- ✅ Auto-słownik (`.glossary/`) i wykrywacz sprzeczności (`gpt-5`, `ConflictModel`) —
  oba przetestowane end-to-end przez CLI: `add` z definicją zapisuje wpis do słownika
  (`glossary` go listuje), `add` z notatką sprzeczną z istniejącą w tym samym folderze
  poprawnie drukuje `UWAGA - mozliwa sprzecznosc` z tytułem i wyjaśnieniem.
- ✅ Agent czatowy (`IAgent`/`OpenAiAgent`, 15 narzędzi, `gpt-5`) — przetestowany end-to-end
  przez `dotnet run -- agent`: listowanie folderów, pytanie RAG, `add_note` z potwierdzeniem
  "tak" (notatka realnie zapisana i zaindeksowana), `trash_note` z odmową "nie" (poprawnie
  niewykonane). W Desktopie zakładka "Agent" (przycisk w pasku narzędzi) zweryfikowana
  zrzutem ekranu — widoczna, poprawnie zawija się do drugiej linii wraz ze "Słownik" (pasek
  narzędzi w sidebarze 270px zmieniony ze `StackPanel` na `WrapPanel` przy tej okazji, bo
  piąty przycisk wypadał poza widoczny obszar). Samo kliknięcie w zakładkę nieprzeklikane
  ręcznie (brak `xdotool`) — patrz Zadanie 8. Dodatkowo przetestowane: `add_note` przez
  agenta z potwierdzeniem "tak" faktycznie zapisuje plik, indeksuje w Qdrant (widoczne w
  `search`) i dopisuje definicję do słownika; z "nie" nie zostawia żadnego śladu.
- ✅ Notatki sortowane alfabetycznie po tytule (`FileNoteStore.ListAsync`) — przetestowane
  CLI (`notes`) na trzech notatkach (Antylopa/Mrówka/Zebra), kolejność poprawna.
- ✅ Odświeżanie drzewa/kosza/luk/słownika po akcji agenta (`ConfirmAgentActionAsync`) —
  poprawka w kodzie (build czysty), niesprawdzona ręcznym klikiem w oknie z tego samego
  powodu co reszta interakcji Desktop (brak `xdotool`).

## 3. Decyzje i ich powody

Te ustalenia są wiążące — nie zmieniaj ich bez wyraźnej prośby użytkownika.

| Decyzja | Powód |
| --- | --- |
| **Każdy klient HTTP konfigurowany z `appsettings.json`**, z `BaseAddress`, `TimeoutSeconds` i słownikiem `Headers` obsługującym własne nagłówki | Użytkownik pracuje na dwóch maszynach o różnym dostępie do usług; wymiana providera ma być zmianą konfiguracji, nie kodu |
| **Klucze API w `appsettings.json`** (pole `ApiKey` + placeholder `{ApiKey}` w nagłówku), plik gitignorowany, `appsettings.Example.json` w repo jako szablon | Prostsza konfiguracja per maszyna niż zmienne środowiskowe; sekret nie trafia do repo dzięki `.gitignore` |
| **Reranker domyślnie zamockowany** (`MockReranker`), `CohereReranker` gotowy jako alternatywna rejestracja | Realne API rerankera dostępne jest tylko na drugiej maszynie użytkownika; wire-format Cohere v2/rerank (model, query, documents, top_n → results[].relevance_score) potwierdzony z oficjalnej dokumentacji |
| **Embedding chwilowo zamockowany** (`MockEmbedder`, deterministyczny wektor z hasha tekstu) | OpenAI blokuje `text-embedding-3-small` na tym kluczu (`403 model_not_found`) mimo dostępu widocznego w panelu/`/v1/models` — nie do naprawienia z poziomu aplikacji; `OpenAiEmbedder` zostaje w kodzie do podmiany jedną linią w DI |
| **Tytuł notatki ustala LLM**, jednym wywołaniem razem z kompresją (`response_format: json_object`, pola `title`+`content`) | Wprost zażądane przez użytkownika; jedno wywołanie zamiast dwóch — taniej i szybciej niż osobny call na sam tytuł |
| **Wyszukiwanie pokazuje cały dokument ORAZ syntezowaną odpowiedź LLM** (`IAnswerSynthesizer`, osobny interfejs od `ICompressor`) | Wprost zażądane przez użytkownika (RAG); trzymane jako osobny krok od czystego wyszukiwania — wyszukiwanie samo w sobie nadal działa bez tego kroku, to nakładka na wynik |
| **Zmiana folderu czyści wszystkie pola** (edytor, zapytanie, wyniki, odpowiedź) i przeładowuje listę notatek folderu | Wprost zażądane przez użytkownika — unika mylącego stanu z poprzedniego folderu |
| **Kompresja LLM: `gpt-3.5-turbo` przez `chat/completions`** | Ten model jest faktycznie dostępny na kluczu użytkownika (w przeciwieństwie do embeddingu) — zweryfikowane działającym wywołaniem w `add` |
| **Kompresja poprawia błędy ortograficzne/gramatyczne/interpunkcyjne z tekstu źródłowego** | Wprost zażądane przez użytkownika; jedna instrukcja w istniejącym prompcie `content`, bez osobnego wywołania LLM — zweryfikowane na notatce z celowymi literówkami |
| **Qdrant lokalnie w Dockerze**, gRPC na porcie 6334 | Pełna kontrola nad danymi, brak zależności od konta w chmurze |
| **Jedna notatka = jeden wektor**, bez chunkowania | LLM kompresuje całą notatkę przed embeddingiem; prostsze niż agregacja wyników z wielu chunków |
| **Pliki Markdown na dysku są źródłem prawdy**, Qdrant to odtwarzalny indeks; payload Qdrant trzyma `file_path` żeby wynik wyszukiwania mógł doczytać pełną treść z dysku | Notatki czytelne i edytowalne poza aplikacją; indeks można skasować i odbudować |
| **Front matter YAML pisany/czytany ręcznie**, bez zależności YamlDotNet | Format ma stałe 5 pól — pełny parser YAML byłby przerostem formy nad treścią |
| **Folder = kolekcja Qdrant** | Naturalna izolacja wyszukiwania per folder, bez filtrowania po payloadzie |
| **Cztery projekty: Core / Infrastructure / Desktop / PipelineTest**, wspólna rejestracja DI w `AddSecondBrainInfrastructure` | Desktop i CLI harness współdzielą całą logikę biznesową bez duplikacji; Core nie zależy od Qdrant/HTTP, więc jest łatwy do testowania |
| **Jeden `MainViewModel` na cały ekran Desktop**, TabControl zamiast nawigacji przez ViewLocator | Nawet po rozroście do 5 zakładek commandy się nie duplikują (każda funkcja to metoda + kilka property), więc rozbicie na osobne VM na razie nic by nie uprościło — dodać dopiero gdy realnie zabraknie miejsca |
| **MVVM: CommunityToolkit.Mvvm** (partial properties + `[RelayCommand]`) | Source-generatory zamiast ręcznego boilerplate'u; mniej kodu niż ReactiveUI dla tego zakresu; to, co dała domyślna templatka `avalonia.mvvm` |
| **UI: paleta Catppuccin (Mocha/Latte) na bazie FluentTheme**, akcent = mauve, foldery = kolorowe kropki (odpowiednik sekcji OneNote) | Wprost zażądane przez użytkownika; `FluentTheme` zostaje jako baza (zachowanie kontrolek, dostępność), kolory i kształty nadpisane w `Styles/Catppuccin.axaml` + `Styles/AppStyles.axaml` zamiast pisać kontrolki od zera |
| **Target framework: `net10.0`** wszędzie | Specyfikacja mówiła o .NET 8 LTS, ale na maszynie dev zainstalowane są tylko runtime 6, 7 i 10 — `net8.0` nie startuje |
| **Sidebar jako `TreeView`** (foldery = korzenie, notatki = dzieci, podstrony = dzieci notatek), zakładka "Notatki" usunięta na rzecz zakładki "Notatka" (podgląd tego, co zaznaczone w drzewie) | Wprost zażądane przez użytkownika; drzewo naturalnie łączy przeglądanie folderów z hierarchią notatek (podstrony) bez dwóch osobnych list |
| **Drzewo ładowane w całości przy starcie/odświeżeniu** (nie leniwie przy rozwinięciu węzła) | Prostszy kod (jeden `LoadTreeAsync` zamiast obsługi zdarzenia rozwinięcia); do zmiany gdy liczba notatek realnie zacznie spowalniać start |
| **Kosz zamiast trwałego usuwania notatek** — plik trafia do `.trash/<folder>___<id>.md`, punkt usuwany z Qdrant od razu (nie da się go "skosić") | Wprost zażądane przez użytkownika |
| **Luki w wiedzy** (`.gaps/<guid>.md`) — gdy RAG jawnie zwróci `Answered:false`, pytanie zapisuje się jako trwały, samo-generujący się backlog rzeczy do dopisania; usuwane tylko ręcznie ("Odrzuć"), bez auto-czyszczenia po dodaniu pasującej notatki | Własny pomysł (nie kopia konkurencji) — żadna popularna apka notatkowa nie ma kroku RAG z jawnym "nie wiem", więc nikt nie robi z porażki wyszukiwania trwałego TODO. Auto-zamykanie luk po dopisaniu notatki to świadomie odłożone v2 |
| **Usuwanie folderu zostaje trwałe** (nie trafia do kosza) | Pełne cofnięcie wymagałoby klonowania całej kolekcji Qdrant zamiast jednego `DeleteCollectionAsync` — niewspółmiernie drogie do tego jak rzadko się kasuje cały folder; dwa kliknięcia jako jedyna ochrona |
| **Tagi: pole wpisywane ręcznie, puste = LLM proponuje** (`CompressionResult.Tags` z tego samego wywołania co tytuł/treść) | Wprost zażądane przez użytkownika; brak osobnego wywołania LLM tylko po tagi |
| **Filtrowanie po tagach NIE zostało zrobione** | Świadomy cios w zakres — drzewo+5 zakładek to już duża zmiana za jeden raz; tagi są zapisywane i widoczne w zakładce "Notatka", ale nie ma jeszcze UI do filtrowania po nich |
| **Backlinki liczone na żądanie** (skan wszystkich notatek folderu w poszukiwaniu `[[Tytuł]]` przy otwarciu notatki), bez precomputowanego indeksu | Skala osobistej bazy wiedzy (dziesiątki–setki notatek na folder) nie uzasadnia utrzymywania indeksu; `[[...]]` nie jest klikalne (zwykły tekst) — nawigacja po linkach to możliwy kolejny krok |
| **"Auto-linkowanie" przez wyszukiwanie wektorowe, nie przez LLM** — po zapisaniu notatki program szuka top-3 podobnych własnym wektorem notatki i pokazuje w statusie | Tańsze i bez ryzyka halucynacji (LLM proszony o zgadywanie tytułów notatek mógłby wymyślić nieistniejący tytuł); reużywa już policzony embedding |
| **Renderowanie Markdown: `Markdown.Avalonia.Tight` (12.0.0-a3, alpha)** | Jedyny pakiet kompatybilny z Avalonia 12 + net10.0 w chwili pisania; ręczne pisanie renderera Markdown byłoby dużo większym nakładem niż ryzyko alpha-wersji biblioteki |
| **Zakładka "Dziś" (dziennik) usunięta** po przetestowaniu przez użytkownika | Wprost zażądane; folder `Dziennik` i notatki w nim utworzone podczas testów **zostają** — to realne dane użytkownika, usunięcie dotyczyło tylko dedykowanej zakładki/UX, nie danych |
| **Auto-słownik** (`.glossary/<slug>.md`) — kompresja LLM zwraca dodatkowo pole `definitions` (zdania typu "X to Y"), każda definicja zapisywana jako osobny plik, nazwa pliku = slug terminu (ten sam termin nadpisuje) | Własny pomysł, drugi z dwóch zaproponowanych obok "Luk w wiedzy"; jedno wywołanie LLM (to samo co kompresja) zamiast osobnego promptu tylko po definicje |
| **Wykrywacz sprzeczności używa osobnego modelu `ConflictModel` (`gpt-5`), różnego od `CompressionModel` (`gpt-3.5-turbo`)** | Zmierzone na żywo: `gpt-3.5-turbo` w trybie `json_object` myli się na tym zadaniu ok. 1/3 przypadków nawet przy `temperature:0` — `response_format: json_object` odbiera modelowi miejsce na chain-of-thought, co potwierdzone porównaniem z odpowiedzią tego samego promptu bez trybu JSON (poprawna za każdym razem). `gpt-5` ma natywne rozumowanie i rozwiązuje to poprawnie bez żadnych sztuczek w prompcie |
| **Wykrywanie sprzeczności tylko przy `add` (pojedyncza notatka), pominięte przy `import` (import z pliku, N linii)** | N notatek importu to już N wywołań LLM (kompresja); podwojenie do 2N przez conflict-check na każdej linii byłoby zbyt kosztowne/wolne, a import z pliku to zwykle świeże dane, nie duplikaty istniejących faktów |
| **Agent czatowy: zawsze globalny** (nie ograniczony do aktualnie otwartego folderu/zakładki), pełny zestaw 15 narzędzi od razu (foldery, notatki, szukaj, RAG, dodaj, kosz, przywróć/usuń trwale, luki, słownik) | Wprost wybrane przez użytkownika (AskUserQuestion) zamiast kontekstu per-zakładka i okrojonego zestawu odczyt-only |
| **Narzędzia agenta modyfikujące dane wymagaja potwierdzenia w czacie (Tak/Nie) przed wykonaniem**; czyste odczyty wykonują się od razu bez pytania | Wprost wybrane przez użytkownika; lista mutujących: `create_folder`, `add_note`, `trash_note`, `restore_note`, `purge_note`, `delete_folder`, `resolve_gap` — wszystko inne to odczyt |
| **Agent: `gpt-5` (`AgentModel`), `parallel_tool_calls:false`** | Orkiestracja (co wywołać, w jakiej kolejności) to zadanie rozumowania jak wykrywanie sprzeczności — ten sam wybór modelu i powód (patrz `ConflictModel` wyżej). `parallel_tool_calls:false` wymusza jedno wywołanie narzędzia na turę, co upraszcza flow potwierdzeń (zawsze dokładnie jedno oczekujące wywołanie do zaakceptowania/odrzucenia, nie trzeba godzić czesciowo odpowiedzianych batchy) |
| **`ConversationState` w `IAgent` to nieprzezroczysty string** (cała historia + wywołania narzędzi w formacie OpenAI, serializowana do JSON) | Wywołujący (CLI, `MainViewModel`) tylko przechowuje i oddaje blob z powrotem, nie zna wewnętrznego formatu providera; nie jest zapisywany na dysk — rozmowa żyje tylko w pamięci na czas działania aplikacji, tak jak reszta stanu edytora |
| **Agent: nowa zakładka "Agent"** (przycisk w pasku narzędzi obok Słownika), dymki czatu (user/asystent), karta potwierdzenia Tak/Nie nad polem wpisywania | Wprost wybrane przez użytkownika (nowa zakładka zamiast zadokowanego panelu); Tak/Nie jako dwie osobne komendy bez parametru (nie `CommandParameter="True"/"False"` rzutowane z string na bool w runtime — kruche) |
| **Pasek narzędzi w sidebarze: `WrapPanel` zamiast `StackPanel`** | Piąty przycisk ("Agent") wypadał poza widoczny obszar sidebaru (stała szerokość 270px, `StackPanel` nie zawija) — złapane dopiero zrzutem ekranu po realnym uruchomieniu, nie na etapie budowania. `WrapPanel` zawija do kolejnej linii zamiast obcinać |
| **Notatki w folderze sortowane alfabetycznie po tytule** (`FileNoteStore.ListAsync`, `Pinned` dalej ma priorytet, potem `Title` przez `StringComparer.OrdinalIgnoreCase`) | Wprost zażądane przez użytkownika; wczesniej sortowanie bylo po `UpdatedAt` malejąco. Ta sama lista zasila i CLI (`notes`), i drzewo w Desktopie (`LoadTreeAsync` -> `BuildNoteTree` zachowuje kolejnosc z `ListAsync`) — jedna zmiana naprawia oba miejsca |
| **Po zaakceptowanej akcji agenta: `MainViewModel` ręcznie odświeża Drzewo/Kosz/Luki/Słownik** (`LoadTreeAsync`/`LoadTrashAsync`/`LoadGapsAsync`/`LoadGlossaryAsync` w `ConfirmAgentActionAsync`) | `OpenAiAgent` działa bezpośrednio na `INoteStore`/`IVectorIndex`, mijając komendy VM (`SaveNoteAsync` itp.), które normalnie same odświeżają UI po zmianie — bez tego np. `create_folder` przez agenta nie pojawiał się w drzewie bez ręcznego odświeżenia |

## 4. Co dalej

Rdzeń działa end-to-end (na mockach embeddingu/rerankera). To, co zostało, to odblokowanie
realnych providerów i dopracowanie UX — żadne z tych zadań nie blokuje pozostałych.

### Zadanie 6 — przywrócić realny embedding OpenAI

1. Ustal z OpenAI dlaczego `text-embedding-3-small` zwraca `403 model_not_found` mimo
   dostępu w `/v1/models` (`x-request-id: req_dc27471d968841b595c69aae8a3435f0` jako
   punkt odniesienia) — to blokada poza aplikacją, nie kod.
2. Gdy `curl` bezpośrednio do `https://api.openai.com/v1/embeddings` zwróci 200: w
   `SecondBrain.Infrastructure/ServiceCollectionExtensions.cs` zmień
   `services.AddSingleton<IEmbedder, MockEmbedder>()` na `OpenAiEmbedder` (jedna linia,
   dotyczy od razu CLI i Desktop).
3. Powtórz `dotnet run -- seed test` / `add` / `search` w PipelineTest — wynik ma się
   zgadzać semantycznie, nie tylko przez dopasowanie słów jak w mocku.

**Odbiór:** `seed`/`add`/`search` przechodzą z `OpenAiEmbedder`, bez błędów HTTP.

### Zadanie 7 — przetestować `CohereReranker` na drugiej maszynie

1. Wpisać prawdziwy klucz Cohere w `appsettings.json` (`Reranker.ApiKey`) na tamtej maszynie.
2. W `ServiceCollectionExtensions.cs` zmienić `services.AddSingleton<IReranker, MockReranker>()`
   na `CohereReranker`.
3. Uruchomić `search` i sprawdzić czy `relevance_score` z odpowiedzi Cohere sensownie
   porządkuje wyniki (mock robi to przez dopasowanie słów — realny reranker powinien
   dawać lepsze rankingi na parafrazach).

**Odbiór:** `search` zwraca wyniki uszeregowane przez realne Cohere, bez błędów HTTP/parsowania.

### Zadanie 8 — przeklikać interakcje Desktop ręcznie

Wygląd zweryfikowany wielokrotnie zrzutami ekranu (drzewo, zakładki, szablony, pole tagów —
wszystko renderuje się poprawnie). Nie sprawdzono jeszcze realnym klikaniem:

1. `cd SecondBrain.Desktop && dotnet run`.
2. Rozwinięcie węzła folderu w drzewie (strzałka `>`) pokazuje jego notatki.
3. Kliknięcie notatki w drzewie przełącza na zakładkę "Notatka" i pokazuje treść jako
   wyrenderowany Markdown (nagłówki `##` z szablonów powinny wyglądać jak nagłówki, nie
   jak `##` w zwykłym tekście).
4. Pole notatki w zakładce "Nowa notatka" ma tło identyczne z resztą karty, także po
   kliknięciu w nie (to był zgłoszony wcześniej problem — kod naprawiony, nieklikane).
5. Przypnij/Odepnij w zakładce "Notatka" przenosi notatkę na górę drzewa przy odświeżeniu.
6. Kosz: usuń notatkę → pojawia się w zakładce "Kosz" → "Przywróć" wraca do drzewa.
8. Skróty Ctrl+N/F/D przełączają zakładki, Ctrl+S zapisuje z zakładki edytora.
9. Zakładka "Agent": wysłanie wiadomości pokazuje dymek usera + odpowiedź agenta; akcja
   mutująca (np. "dodaj notatkę o...") pokazuje kartę Tak/Nie zamiast wykonać się od razu;
   "Tak" wykonuje i pokazuje wynik, "Nie" pokazuje że akcja odrzucona. Enter w polu
   wpisywania wysyła wiadomość tak samo jak przycisk "Wyślij" (`KeyBinding` na `TextBox`,
   niesprawdzone ręcznie z tego samego powodu co reszta tego zadania).

**Odbiór:** wszystkie dziewięć punktów działa bez wyjątków w oknie aplikacji.

### Zadanie 9 — filtrowanie notatek po tagach (świadomie pominięte w tej turze)

Tagi są zapisywane (ręcznie albo przez LLM) i widoczne w zakładce "Notatka", ale nigdzie
nie da się po nich filtrować/szukać. Najprostsze podejście: pole tekstowe nad drzewem,
filtrujące widoczne węzły-notatki po dopasowaniu do `SearchResultItem.Tags` (samo drzewo
zostaje, tylko chowa niepasujące liście).

### Zadanie 10 — klikalne `[[linki]]` w treści notatki

Backlinki działają (sekcja "Odnośniki do tej notatki" w zakładce "Notatka"), ale sam
`[[Tytuł notatki]]` w treści to obecnie zwykły tekst, nie link. Wymaga własnego inline
parsera nad `Markdown.Avalonia.Tight` albo TextBlock z `Inlines` i `Run`/`InlineUIContainer`
reagującym na klik — nietrywialne, nie zaczynaj bez wyraźnej prośby.

## 5. Czego nie robić

- Nie dodawaj chunkowania długich notatek — świadomie odrzucone na tym etapie.
- Nie wprowadzaj bazy SQLite ani innego magazynu metadanych; pliki `.md` plus Qdrant wystarczą.
- Nie buduj warstwy abstrakcji nad providerami LLM „na przyszłość" — jeden interfejs na
  funkcję (`ICompressor`, `IEmbedder`, `IReranker`) i tyle.
- Nie commituj `appsettings.json` z prawdziwym kluczem — jest w `.gitignore`, aktualizuj
  tylko `appsettings.Example.json` (z pustymi wartościami `ApiKey`).
- Nie rozbijaj `MainViewModel` na osobne ViewModels/nawigację, dopóki jeden plik faktycznie
  nie zrobi się nieczytelny — na razie to celowy wybór, nie dług.
- Nie dodawaj YamlDotNet ani innej biblioteki do front matteru — format jest stały i prosty.
- Nie dodawaj kosza dla usuwania folderów bez wyraźnej prośby — świadomie zostawione jako
  trwałe (patrz decyzja w sekcji 3, powód: koszt klonowania kolekcji Qdrant).
- Nie zaczynaj klikalnych `[[linków]]` (Zadanie 10) ani filtrowania po tagach (Zadanie 9)
  bez wyraźnej prośby — to świadome cięcia zakresu, nie zapomniane elementy.

## 6. Środowisko

- SDK: .NET 10.0.111. Zainstalowane runtime'y: 6.0, 7.0, 10.0 (brak 8.0).
- Docker 20.10.17; kontener `secondbrain-qdrant` na portach 6333 (REST) i 6334 (gRPC).
- Katalog roboczy: `/home/piotr/Projekty/Brain`. Solucja: `SecondBrain.slnx` (format .NET 10,
  nie klasyczny `.sln`). Repozytorium git: https://github.com/PiotrFerenc/second-brain
  (publiczne), CI (`.github/workflows/package.yml`) publikuje zip źródeł jako GitHub
  Release przy każdym pushu na `master`.
- Kolekcje istniejące w Qdrant po testach: `123`, `osobiste`, `praca`, `test`.
- To środowisko deweloperskie **ma działający display** (`gnome-screenshot` pokazuje
  realną treść okna, sprawdzone wielokrotnie) — ale nie ma `xdotool`/`wmctrl`, więc nie da
  się symulować kliknięć/klawiatury. Zrzuty ekranu = tylko stan spoczynkowy po starcie.
  Weryfikacja interakcji (klikanie, rozwijanie drzewa, skróty klawiszowe) wymaga
  przejścia przez Ciebie ręcznie — patrz Zadanie 8.
- Build Desktop do jednego pliku wykonywalnego (self-contained, bez osobnych `.dll`):

  ```bash
  cd SecondBrain.Desktop
  dotnet publish -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -o ./publish
  ```

  Wynik: `publish/SecondBrain.Desktop` (~103MB, .NET runtime + Avalonia + Skia/HarfBuzz
  wbudowane) + `publish/appsettings.json` obok, osobno (świadomie nie wbudowany w plik —
  konfiguracja/klucz API mają iść bez rebuildu). Przetestowane: publish czysty, plik
  startuje bez błędów. `-r linux-x64` dobrać pod docelową maszynę (np. `win-x64`,
  `osx-arm64`); to nie jest krok w CI (`.github/workflows/package.yml` dalej tylko pakuje
  źródła do zipa), robi się ręcznie na żądanie.
