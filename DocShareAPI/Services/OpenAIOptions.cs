namespace DocShareAPI.Services;

public sealed class OpenAIOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-4.1-mini";
    public int TimeoutSeconds { get; init; } = 60;
    public int MaxOutputTokens { get; init; } = 1200;
    public const int MaxChatCharacters = 20000;
    public int MaxDocumentCharacters { get; init; } = 60000;
    public int MaxDocumentPages { get; init; } = 100;
    public int MaxPdfBytes { get; init; } = 10 * 1024 * 1024;

    public static OpenAIOptions FromConfiguration(IConfiguration configuration, Func<string, string?>? environment = null)
    {
        var envKey = (environment ?? Environment.GetEnvironmentVariable)("OPEN_AI_API_KEY");
        return new()
        {
            ApiKey = (string.IsNullOrWhiteSpace(envKey) ? configuration["OpenAIKey"] : envKey)?.Trim() ?? "",
            Model = string.IsNullOrWhiteSpace(configuration["OpenAI:Model"]) ? "gpt-4.1-mini" : configuration["OpenAI:Model"]!.Trim(),
            TimeoutSeconds = Math.Clamp(configuration.GetValue("OpenAI:TimeoutSeconds", 60), 5, 120),
            MaxOutputTokens = Math.Clamp(configuration.GetValue("OpenAI:MaxOutputTokens", 1200), 128, 4096)
        };
    }
}

public sealed class AIServiceException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
