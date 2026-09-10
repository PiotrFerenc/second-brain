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

## 2. Stan obecny — wszystkie zadania z poprzedniej wersji planu zrobione

Wszystkie pięć zadań z poprzedniej wersji tego planu (kompresja LLM, magazyn plikowy,
restrukturyzacja na projekty, realny reranker, aplikacja Avalonia) jest zaimplementowane.
Solucja: `SecondBrain.slnx` (nowy XML-owy format solucji z .NET 10 SDK — nie `.sln`).

```
SecondBrain.slnx
  SecondBrain.Core/            interfejsy domenowe + modele, bez zaleznosci zewnetrznych
    Note.cs                    record Note (Id, Title, RawContent, CompressedContent, Tags,
                                CreatedAt, UpdatedAt, FilePath) + ScoredNote
    Interfaces.cs               ICompressor (zwraca CompressionResult: Title+CompressedContent,
                                 tytul ustala LLM), IEmbedder, IReranker, IAnswerSynthesizer
                                 (RAG: odpowiedz na pytanie na podstawie znalezionych notatek),
                                 INoteStore (+ ListAsync — wszystkie notatki z folderu), IVectorIndex

  SecondBrain.Infrastructure/  implementacje, referencja do Core
    Options.cs                  HttpClientOptions (BaseAddress, TimeoutSeconds, ApiKey, Headers)
                                 + OpenAiOptions, RerankerOptions, QdrantOptions, StorageOptions
    HttpClientHeaders.cs        wspolne nakladanie naglowkow (w tym {ApiKey}) na nazwany HttpClient
    Embedding.cs                OpenAiEmbedder (realny) + MockEmbedder (aktywny, patrz nizej)
    Compression.cs              OpenAiCompressor (chat/completions, response_format json_object,
                                 zwraca tytul+tresc jednym wywolaniem) — dziala, klucz ma dostep
    AnswerSynthesis.cs           OpenAiAnswerSynthesizer (chat/completions) — dziala
    Reranking.cs                MockReranker (aktywny) + CohereReranker (v2/rerank, gotowy,
                                 nieprzetestowany — provider dostepny tylko na 2. maszynie)
    NoteFileStore.cs             FileNoteStore — zapis/odczyt .md z front matterem + ListAsync
                                 (skan katalogu folderu, wszystkie lata, posortowane po dacie)
    QdrantVectorIndex.cs         implementacja IVectorIndex na Qdrant.Client
    ServiceCollectionExtensions.cs  AddSecondBrainInfrastructure(config) — jedna rejestracja
                                 DI uzywana przez PipelineTest i Desktop

  SecondBrain.Desktop/         Avalonia + CommunityToolkit.Mvvm, referencja do Core+Infrastructure
    Program.cs                  buduje ServiceProvider PRZED startem Avalonii, wystawia App.Services
    App.axaml.cs                 rozwiazuje MainViewModel z App.Services zamiast `new MainViewModel()`
    ViewModels/MainViewModel.cs  jeden ViewModel na cale okno (foldery + edytor + wyszukiwanie —
                                 zakres MVP nie uzasadnial rozbicia na wiecej VM/widokow)
    ViewModels/SearchResultItem.cs  DTO do bindowania listy wynikow
    Converters/FolderAccentConverter.cs  string -> kolorowa kropka folderu (Catppuccin)
    Styles/Catppuccin.axaml      paleta Mocha/Latte jako ThemeDictionaries (Light/Dark),
                                 nadpisuje tez SystemAccentColor (mauve) dla FluentTheme
    Styles/AppStyles.axaml       Style dla Window/Button/TextBox/ListBox(Item)/TabItem -
                                 FluentTheme zostaje baza, tu tylko kolory/ksztalty
    Views/MainWindow.axaml       Grid: sidebar folderow (kolorowe kropki, jak sekcje OneNote)
                                 | "kartka" tresci z TabControl: "Nowa notatka", "Szukaj"
                                 (+ karta z odpowiedzia LLM nad wynikami), "Notatki" (wszystkie
                                 notatki z wybranego folderu). Zmiana folderu czysci wszystkie
                                 pola (OnSelectedFolderChanged) i przeladowuje liste notatek.

  SecondBrain.PipelineTest/    cienki CLI nad Core+Infrastructure, do szybkich testow bez UI
    Program.cs                  komendy: list, create, seed, add, notes, search (search
                                 drukuje tez syntezowana odpowiedz LLM pod wynikami)
    SampleNotes.cs               3 przykladowe notatki do `seed`
```

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
dotnet run -- search praca "pytanie..."
```

Uruchomienie desktopu:

```bash
cd SecondBrain.Desktop
dotnet run
```

Stan weryfikacji:

- ✅ `dotnet build SecondBrain.slnx` — cała solucja buduje się czysto (0 warn, 0 err).
- ✅ CLI: `list`, `create`, `seed`, `add`, `search` przetestowane end-to-end (mock embedding,
  realna kompresja `gpt-3.5-turbo`, mock reranker, realny zapis/odczyt plików).
- ✅ Desktop: proces startuje i działa (sprawdzone uruchomieniem w tle — brak wyjątków
  w logu, proces żywy i zużywa CPU jak działająca pętla UI).
- ✅ Desktop UI: zweryfikowano zrzutem ekranu (`gnome-screenshot`, ten sam `DISPLAY` co
  wcześniej działa poprawnie — wcześniejszy czarny zrzut był przejściowym problemem, nie
  cechą środowiska). Sidebar folderów, kolorowe kropki, zakładki i przycisk akcji renderują
  się poprawnie w motywie Catppuccin Mocha. Klikanie w kontrolki (Zapisz/Szukaj/wybór
  folderu) nie zostało jeszcze przeklikane end-to-end przez człowieka — tylko wizualnie.
- ⏳ `OpenAiEmbedder` — kod gotowy, nadal zablokowany po stronie OpenAI (403 na
  `text-embedding-3-small` mimo widocznego dostępu — patrz decyzje, sekcja 3).
- ⏳ `CohereReranker` — kod gotowy pod wire-format Cohere v2/rerank, nieprzetestowany
  (provider dostępny tylko na drugiej maszynie użytkownika).
- ✅ Tytuł notatki ustalany przez LLM (`ICompressor.CompressAsync` zwraca `CompressionResult`
  z `Title`+`CompressedContent` jednym wywołaniem `chat/completions` w trybie JSON) —
  przetestowane w CLI (`add`), tytuł sensowny i różny od pierwszej linii wpisu.
- ✅ Odpowiedź LLM na pytanie (RAG) — `IAnswerSynthesizer` bierze top wyniki wyszukiwania
  i syntetyzuje bezpośrednią odpowiedź — przetestowane w CLI (`search`), działa.
- ✅ `notes <folder>` / zakładka "Notatki" w Desktop — wszystkie notatki z folderu, testowane
  w CLI.
- ✅ Czyszczenie pól po zmianie folderu — zaimplementowane (`OnSelectedFolderChanged`),
  nieprzeklikane ręcznie (patrz Zadanie 8).
- ⚠️ Tło edytora notatki na focus — naprawione najbardziej prawdopodobnym mechanizmem
  (nadpisanie `TextControlBackground*` w scope stylu `.editor` + `/template/
  Border#PART_BorderElement`), ale **nieprzetestowane interaktywnie**: to środowisko nie ma
  `xdotool`/`wmctrl` do symulacji kliknięcia, więc nie dało się zrobić zrzutu z realnie
  sfokusowanym polem. Zweryfikuj klikając w pole notatki — jeśli nadal robi się czarne,
  zgłoś to z opisem (jaki motyw systemowy, jasny czy ciemny).

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
| **Qdrant lokalnie w Dockerze**, gRPC na porcie 6334 | Pełna kontrola nad danymi, brak zależności od konta w chmurze |
| **Jedna notatka = jeden wektor**, bez chunkowania | LLM kompresuje całą notatkę przed embeddingiem; prostsze niż agregacja wyników z wielu chunków |
| **Pliki Markdown na dysku są źródłem prawdy**, Qdrant to odtwarzalny indeks; payload Qdrant trzyma `file_path` żeby wynik wyszukiwania mógł doczytać pełną treść z dysku | Notatki czytelne i edytowalne poza aplikacją; indeks można skasować i odbudować |
| **Front matter YAML pisany/czytany ręcznie**, bez zależności YamlDotNet | Format ma stałe 5 pól — pełny parser YAML byłby przerostem formy nad treścią |
| **Folder = kolekcja Qdrant** | Naturalna izolacja wyszukiwania per folder, bez filtrowania po payloadzie |
| **Cztery projekty: Core / Infrastructure / Desktop / PipelineTest**, wspólna rejestracja DI w `AddSecondBrainInfrastructure` | Desktop i CLI harness współdzielą całą logikę biznesową bez duplikacji; Core nie zależy od Qdrant/HTTP, więc jest łatwy do testowania |
| **Jeden `MainViewModel` na cały ekran Desktop** (foldery + edytor + wyszukiwanie), TabControl zamiast nawigacji przez ViewLocator | Zakres MVP (3 proste panele) nie uzasadnia rozbicia na osobne ViewModels/widoki z nawigacją — dodać dopiero gdy realnie zabraknie miejsca |
| **MVVM: CommunityToolkit.Mvvm** (partial properties + `[RelayCommand]`) | Source-generatory zamiast ręcznego boilerplate'u; mniej kodu niż ReactiveUI dla tego zakresu; to, co dała domyślna templatka `avalonia.mvvm` |
| **UI: paleta Catppuccin (Mocha/Latte) na bazie FluentTheme**, akcent = mauve, foldery = kolorowe kropki (odpowiednik sekcji OneNote) | Wprost zażądane przez użytkownika; `FluentTheme` zostaje jako baza (zachowanie kontrolek, dostępność), kolory i kształty nadpisane w `Styles/Catppuccin.axaml` + `Styles/AppStyles.axaml` zamiast pisać kontrolki od zera |
| **Target framework: `net10.0`** wszędzie | Specyfikacja mówiła o .NET 8 LTS, ale na maszynie dev zainstalowane są tylko runtime 6, 7 i 10 — `net8.0` nie startuje |

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

Wygląd zweryfikowany zrzutem ekranu (sidebar, kolorowe kropki folderów, zakładki, akcent
mauve — wszystko renderuje się poprawnie w Catppuccin Mocha). Nie sprawdzono jeszcze
realnym klikaniem, czy komendy faktycznie coś robią w działającym oknie:

1. `cd SecondBrain.Desktop && dotnet run`.
2. Sprawdzić: lista folderów ładuje się przy starcie, tworzenie nowego folderu działa,
   zakładka "Nowa notatka" zapisuje (kompresja → plik → embedding → Qdrant), zakładka
   "Szukaj" zwraca wyniki z podglądem pełnej treści po prawej, przyciski disable'ują się
   w trakcie operacji (`IsBusy`).

**Odbiór:** wszystkie cztery interakcje działają bez wyjątków w oknie aplikacji.

### Zadanie 9 (opcjonalne, dopiero po 6-8) — tagi i edycja notatki w UI

Obecnie `add`/edytor Desktop zawsze zapisuje notatkę z pustą listą tagów (`Tags: []`) —
CLI/UI nie mają jeszcze pola do ich wpisania, mimo że model i front matter je wspierają.
Podobnie nie ma edycji istniejącej notatki (tylko tworzenie nowej). Nie zaczynaj tego
zadania bez wyraźnej prośby — nie było części żadnego wcześniejszego ustalenia.

## 5. Czego nie robić

- Nie dodawaj chunkowania długich notatek — świadomie odrzucone na tym etapie.
- Nie wprowadzaj bazy SQLite ani innego magazynu metadanych; pliki `.md` plus Qdrant wystarczą.
- Nie buduj warstwy abstrakcji nad providerami LLM „na przyszłość" — jeden interfejs na
  funkcję (`ICompressor`, `IEmbedder`, `IReranker`) i tyle.
- Nie commituj `appsettings.json` z prawdziwym kluczem — jest w `.gitignore`, aktualizuj
  tylko `appsettings.Example.json` (z pustymi wartościami `ApiKey`).
- Nie rozbijaj `MainViewModel` na osobne ViewModels/nawigację, dopóki jeden plik faktycznie
  nie zrobi się nieczytelny — na razie 3 panele w jednym VM to celowy wybór, nie dług.
- Nie dodawaj YamlDotNet ani innej biblioteki do front matteru — format jest stały i prosty.

## 6. Środowisko

- SDK: .NET 10.0.111. Zainstalowane runtime'y: 6.0, 7.0, 10.0 (brak 8.0).
- Docker 20.10.17; kontener `secondbrain-qdrant` na portach 6333 (REST) i 6334 (gRPC).
- Katalog roboczy: `/home/piotr/Projekty/Brain`. Solucja: `SecondBrain.slnx` (format .NET 10,
  nie klasyczny `.sln`). Repozytorium git nie jest jeszcze zainicjalizowane.
- Kolekcje istniejące w Qdrant po testach: `praca`, `osobiste`, `test`.
- Środowisko deweloperskie jest headless — `DISPLAY`/`WAYLAND_DISPLAY` są ustawione i
  `gnome-screenshot` działa technicznie, ale nie pokazuje realnie wyrenderowanej treści
  okien aplikacji (zrzut wychodzi czarny). Weryfikacja UI Desktop wymaga innej maszyny
  (patrz Zadanie 8).
