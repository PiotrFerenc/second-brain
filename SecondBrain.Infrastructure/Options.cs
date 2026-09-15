namespace SecondBrain.Infrastructure;

public class HttpClientOptions
{
    public string BaseAddress { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public string? ApiKey { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class EmbeddingOptions : HttpClientOptions
{
    public string Model { get; set; } = "text-embedding-3-small";
}

public class CompressionOptions : HttpClientOptions
{
    public string Model { get; set; } = "gpt-3.5-turbo";

    // ponytail: dlugie, bardzo dosadne wyliczenie 4 pol (zawsze wszystkie cztery) nie jest
    // przypadkowe - krotsze/mniej nachalne wersje tego promptu testowane na zywo (curl)
    // albo gubily pole "definitions" calkowicie, albo zwracaly je puste nawet dla
    // oczywistych definicji typu "RAG (Retrieval-Augmented Generation) to technika...".
    public string SystemPrompt { get; set; } =
        "Jestes asystentem kompresujacym notatki do osobistej bazy wiedzy. Zwroc WYLACZNIE " +
        "obiekt JSON z DOKLADNIE czterema polami, zawsze wszystkimi czterema: " +
        "\"title\" (krotki, zwiezly tytul notatki ustalony przez Ciebie na podstawie tresci, " +
        "maks. 80 znakow), \"content\" (skompresowana, ustrukturyzowana tresc notatki " +
        "zachowujaca kluczowe fakty, bez powtorzen i dygresji, z poprawionymi bledami " +
        "ortograficznymi, gramatycznymi i interpunkcyjnymi z tekstu zrodlowego, sformatowana " +
        "jako czytelny markdown - naglowki, listy, pogrubienia tam gdzie pasuja do struktury " +
        "tresci), \"tags\" (tablica 2-5 " +
        "krotkich tagow jednowyrazowych po polsku, malymi literami), \"definitions\" " +
        "(tablica obiektow {\"term\",\"definition\"} - wyciagnij z tekstu kazde zdanie " +
        "postaci \"X to Y\", \"X oznacza Y\" lub rozwiniecie skrotu w nawiasie jak " +
        "\"RAG (Retrieval-Augmented Generation)\"; jesli notatka nie zawiera takiego " +
        "zdania, zwroc pusta tablice [], ale pole \"definitions\" MUSI byc obecne zawsze).";
}

public class AnswerSynthesisOptions : HttpClientOptions
{
    public string Model { get; set; } = "gpt-3.5-turbo";

    public string SystemPrompt { get; set; } =
        "Jestes asystentem odpowiadajacym na pytania wylacznie na podstawie prywatnych " +
        "notatek uzytkownika ponizej. Zwroc WYLACZNIE obiekt JSON o polach: \"answered\" " +
        "(true jesli notatki faktycznie zawieraja odpowiedz, false jesli nie) oraz \"answer\" " +
        "(zwiezla odpowiedz po polsku gdy answered=true; gdy answered=false, krotkie " +
        "zdanie ze notatki nie zawieraja odpowiedzi - bez zgadywania i bez wiedzy spoza notatek).";
}

public class TagCleaningOptions : HttpClientOptions
{
    // ponytail: grupowanie tagow to ekstrakcja/kategoryzacja, nie twarde rozumowanie jak
    // wykrywanie sprzecznosci - domyslny model jak w Compression wystarcza (patrz PLAN.md
    // decyzje: ConflictModel/AgentModel istnieja bo gpt-3.5-turbo konkretnie zawodzil na
    // tamtym zadaniu, to tu nie zaobserwowano). Konfiguracja mimo to osobna, jak kazdy provider.
    public string Model { get; set; } = "gpt-3.5-turbo";

    public string SystemPrompt { get; set; } =
        "Dostajesz liste WSZYSTKICH tagow uzywanych w osobistej bazie notatek uzytkownika. " +
        "Znajdz grupy tagow ktore znacza to samo (liczba pojedyncza/mnoga, oczywiste literowki, " +
        "synonimy) i zasugeruj jedna kanoniczna forme dla kazdej grupy. Pomin tagi ktore nie maja " +
        "duplikatu - nie twórz grup jednoelementowych. Zwroc WYLACZNIE obiekt JSON o jednym polu " +
        "\"groups\": tablica obiektow {\"tags\": [...], \"suggestedCanonical\": \"...\"}. Jesli nie " +
        "ma zadnych duplikatow, zwroc {\"groups\": []}.";
}

public class ConflictDetectionOptions : HttpClientOptions
{
    // Wykrywanie sprzecznosci to realne zadanie rozumowania, nie streszczanie -
    // gpt-3.5-turbo myli sie tu nawet przy temperature=0 (zmierzone: ~2/3 trafien
    // na tym samym przykladzie). gpt-5 rozwiazuje to poprawnie za kazdym razem.
    public string Model { get; set; } = "gpt-5";

    public string SystemPrompt { get; set; } =
        "Porownujesz NOWA notatke z lista JUZ ISTNIEJACYCH notatek uzytkownika. Sprawdz, " +
        "czy ktoras z istniejacych notatek podaje INNA wartosc dla tego samego faktu " +
        "(np. inna godzina/sala/data/liczba dla tego samego wydarzenia lub tematu) niz " +
        "NOWA notatka. Zwroc WYLACZNIE obiekt JSON o polach: \"hasConflict\" (bool), " +
        "\"conflictingTitle\" (tytul sprzecznej notatki albo null jesli brak), " +
        "\"explanation\" (jedno krotkie zdanie po polsku opisujace sprzecznosc, albo " +
        "null jesli brak). Nie zgaduj - hasConflict=true tylko gdy sprzecznosc faktow " +
        "jest jednoznaczna, nie przy zwyklej roznicy tematu.";
}

public class AgentOptions : HttpClientOptions
{
    // Agent orkiestruje wywolania narzedzi (co wywolac, w jakiej kolejnosci, kiedy skonczyc) -
    // to tez zadanie rozumowania jak wykrywanie sprzecznosci, nie ekstrakcja/streszczanie,
    // wiec ten sam silniejszy model co ConflictDetection.
    public string Model { get; set; } = "gpt-5";

    public string SystemPrompt { get; set; } =
        "Jestes asystentem osobistej bazy wiedzy uzytkownika (Second Brain), dostepnym jako czat. " +
        "Masz dostep do narzedzi pozwalajacych przeszukiwac, czytac, dodawac i porzadkowac notatki " +
        "w folderach uzytkownika. Odpowiadaj po polsku, zwiezle i konkretnie. Gdy uzytkownik pyta o " +
        "cos co jest w notatkach, uzyj narzedzia zamiast zgadywac. Narzedzia, ktore cos zmieniaja, " +
        "same poprosza uzytkownika o potwierdzenie zanim sie wykonaja - po prostu je wywoluj, nie " +
        "pytaj o zgode w tresci wiadomosci.";
}

public class RerankerOptions : HttpClientOptions
{
    public string Model { get; set; } = "rerank-v3.5";
}

// LightOnOCR-2-1B to zwykle self-hosted serwer (np. vLLM), nie publiczne SaaS jak OpenAI -
// BaseAddress pusty domyslnie, do uzupelnienia per-maszyna (ten sam wzorzec co Reranker/Cohere).
public class OcrOptions : HttpClientOptions
{
    public string Model { get; set; } = "LightOnOCR-2-1B";

    public string Prompt { get; set; } =
        "Przepisz caly tekst widoczny na tym obrazku, doslownie, bez komentarzy. Sformatuj wynik jako czytelny markdown (naglowki, listy, pogrubienia, akapity zgodnie ze struktura tekstu na obrazku), zachowujac oryginalna tresc bez zmian.";
}

public class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public bool UseHttps { get; set; }
    public string? ApiKey { get; set; }
    public uint VectorSize { get; set; } = 1536;
    public string Distance { get; set; } = "Cosine";
}

public class StorageOptions
{
    // Puste = ~/SecondBrain/notes
    public string NotesRootPath { get; set; } = "";
}
