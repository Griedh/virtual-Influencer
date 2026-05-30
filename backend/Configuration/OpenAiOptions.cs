using System.Globalization;

namespace VirtualInfluencer.Backend.Configuration;

public sealed class OpenAiOptions
{
    public string BaseUrl { get; init; } = "https://api.openai.com";
    public string ApiKey { get; init; } = string.Empty;

    public string RealtimeModel { get; init; } = "gpt-realtime-2";
    public string RealtimeVoice { get; init; } = "alloy";
    public string RealtimeInstructions { get; init; } = "Du bist ein freundliches Team-Maskottchen. Antworte kurz, klar und hilfreich.";

    public string ModularTranscribeModel { get; init; } = "gpt-4o-transcribe";
    public string ModularTextModel { get; init; } = string.Empty;
    public string ModularTtsModel { get; init; } = "gpt-4o-mini-tts";
    public string ModularTtsVoice { get; init; } = "alloy";
    public string ModularTtsFormat { get; init; } = "wav";
    public string ModularInstructions { get; init; } = "Du bist ein hilfreicher Meeting-Assistent. Antworte in maximal drei Saetzen.";

    public int InputSampleRateHz { get; init; } = 16000;
    public int InputChannels { get; init; } = 1;
    public int MaxTurnAudioSeconds { get; init; } = 30;
    public int IdleTimeoutSeconds { get; init; } = 120;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public Uri GetRealtimeWebSocketUri(string? modelOverride = null)
    {
        var normalizedBaseUrl = BaseUrl.TrimEnd('/');
        var baseUri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        var scheme = baseUri.Scheme switch
        {
            "https" => "wss",
            "http" => "ws",
            _ => baseUri.Scheme
        };

        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Path = "/v1/realtime",
            Query = $"model={Uri.EscapeDataString(modelOverride ?? RealtimeModel)}"
        };
        return builder.Uri;
    }

    public static OpenAiOptions FromConfiguration(IConfiguration configuration)
    {
        return new OpenAiOptions
        {
            BaseUrl = ReadString(configuration, "OPENAI_BASE_URL", "OpenAI:BaseUrl", "https://api.openai.com"),
            ApiKey = ReadString(configuration, "OPENAI_API_KEY", "OpenAI:ApiKey", string.Empty),
            RealtimeModel = ReadString(configuration, "OPENAI_REALTIME_MODEL", "OpenAI:Realtime:Model", "gpt-realtime-2"),
            RealtimeVoice = ReadString(configuration, "OPENAI_REALTIME_VOICE", "OpenAI:Realtime:Voice", "alloy"),
            RealtimeInstructions = ReadString(configuration, "OPENAI_REALTIME_INSTRUCTIONS", "OpenAI:Realtime:Instructions", "Du bist ein freundliches Team-Maskottchen. Antworte kurz, klar und hilfreich."),
            ModularTranscribeModel = ReadString(configuration, "OPENAI_TRANSCRIBE_MODEL", "OpenAI:Modular:TranscribeModel", "gpt-4o-transcribe"),
            ModularTextModel = ReadString(configuration, "OPENAI_TEXT_MODEL", "OpenAI:Modular:TextModel", string.Empty),
            ModularTtsModel = ReadString(configuration, "OPENAI_TTS_MODEL", "OpenAI:Modular:TtsModel", "gpt-4o-mini-tts"),
            ModularTtsVoice = ReadString(configuration, "OPENAI_TTS_VOICE", "OpenAI:Modular:TtsVoice", "alloy"),
            ModularTtsFormat = ReadString(configuration, "OPENAI_TTS_FORMAT", "OpenAI:Modular:TtsFormat", "wav"),
            ModularInstructions = ReadString(configuration, "OPENAI_MODULAR_INSTRUCTIONS", "OpenAI:Modular:Instructions", "Du bist ein hilfreicher Meeting-Assistent. Antworte in maximal drei Saetzen."),
            InputSampleRateHz = ReadInt(configuration, "VOICE_INPUT_SAMPLE_RATE", "Voice:InputSampleRateHz", 16000),
            InputChannels = ReadInt(configuration, "VOICE_INPUT_CHANNELS", "Voice:InputChannels", 1),
            MaxTurnAudioSeconds = ReadInt(configuration, "VOICE_MAX_TURN_SECONDS", "Voice:MaxTurnAudioSeconds", 30),
            IdleTimeoutSeconds = ReadInt(configuration, "VOICE_IDLE_TIMEOUT_SECONDS", "Voice:IdleTimeoutSeconds", 120)
        };
    }

    private static string ReadString(IConfiguration configuration, string environmentKey, string configKey, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = configuration[configKey];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadInt(IConfiguration configuration, string environmentKey, string configKey, int fallback)
    {
        var rawValue = Environment.GetEnvironmentVariable(environmentKey) ?? configuration[configKey];
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
