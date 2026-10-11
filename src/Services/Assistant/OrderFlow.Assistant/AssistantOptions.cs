namespace OrderFlow.Assistant;

/// <summary>
/// Defaults target Google Gemini's free tier through its OpenAI-compatible endpoint. Any OpenAI-compatible
/// provider works by setting <c>ASSISTANT_ENDPOINT</c>, <c>ASSISTANT_MODEL</c> and the key.
/// </summary>
public sealed class AssistantOptions
{
    public const string DefaultModel = "gemini-flash-latest";
    public static readonly Uri DefaultEndpoint = new("https://generativelanguage.googleapis.com/v1beta/openai/");

    public string? ApiKey { get; init; }
    public string Model { get; init; } = DefaultModel;
    public Uri Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Requests allowed per UTC day across all users, so a free quota is never exhausted by one visitor.</summary>
    public int DailyLimit { get; init; } = 300;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

    public static AssistantOptions From(IConfiguration config) => new()
    {
        ApiKey = config["GEMINI_API_KEY"] ?? config["Assistant:ApiKey"],
        Model = config["ASSISTANT_MODEL"] ?? config["Assistant:Model"] ?? DefaultModel,
        Endpoint = Uri.TryCreate(config["ASSISTANT_ENDPOINT"] ?? config["Assistant:Endpoint"], UriKind.Absolute, out var endpoint)
            ? endpoint
            : DefaultEndpoint,
        DailyLimit = int.TryParse(config["ASSISTANT_DAILY_LIMIT"] ?? config["Assistant:DailyLimit"], out var limit) ? limit : 300
    };
}
