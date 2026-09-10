using SecondBrain.Core;

namespace SecondBrain.PipelineTest;

public static class SampleNotes
{
    public static readonly Note[] All = BuildAll();

    private static Note[] BuildAll()
    {
        var now = DateTimeOffset.UtcNow;

        return
        [
            MakeNote("Konfiguracja HttpClient dla OpenAI",
                "appsettings.json trzyma BaseAddress, naglowki i nazwe zmiennej srodowiskowej z kluczem API. HttpClientFactory rejestruje nazwany klient, ktory podstawia klucz do naglowka Authorization.",
                ["dotnet", "http", "openai"], now),

            MakeNote("Dystans cosinusowy w Qdrant",
                "Qdrant liczy podobienstwo wektorow miara cosinusowa - kat miedzy wektorami, nie ich dlugosc. Dla embeddingow tekstu to standardowy wybor dystansu w kolekcji.",
                ["qdrant", "wektory", "teoria"], now),

            MakeNote("Reranker a wyszukiwanie wektorowe",
                "Wyszukiwanie wektorowe zwraca szeroka pule kandydatow po podobienstwie. Reranker (np. Cohere) porzadkuje ich trafnosc wzgledem calego zapytania, dajac lepszy top-N niz samo similarity search.",
                ["reranker", "wyszukiwanie", "rag"], now),
        ];
    }

    private static Note MakeNote(string title, string content, string[] tags, DateTimeOffset now) =>
        new(Guid.NewGuid(), title, content, content, tags, now, now);
}
