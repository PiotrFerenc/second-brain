namespace SecondBrain.Infrastructure;

public class HttpClientOptions
{
    public string BaseAddress { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public string? ApiKey { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class OpenAiOptions : HttpClientOptions
{
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string CompressionModel { get; set; } = "gpt-3.5-turbo";
}

public class RerankerOptions : HttpClientOptions
{
    public string Model { get; set; } = "rerank-v3.5";
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
