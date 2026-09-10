namespace SecondBrain.Infrastructure;

// ponytail: jeden generyczny sposob nakladania nagłówków (w tym Authorization z {ApiKey})
// na dowolny nazwany HttpClient - dziala tak samo dla OpenAI, rerankera i kazdego kolejnego providera.
public static class HttpClientHeaders
{
    public static void Apply(HttpClient client, HttpClientOptions options)
    {
        client.BaseAddress = new Uri(options.BaseAddress);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        foreach (var (name, rawValue) in options.Headers)
        {
            var value = options.ApiKey is null ? rawValue : rawValue.Replace("{ApiKey}", options.ApiKey);
            client.DefaultRequestHeaders.Remove(name);
            client.DefaultRequestHeaders.Add(name, value);
        }
    }
}
