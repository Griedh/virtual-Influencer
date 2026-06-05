using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VirtualInfluencer.Backend.Configuration;
using VirtualInfluencer.Backend.Contracts;

namespace VirtualInfluencer.Backend.Services;

public sealed class OpenAiApiClient
{
    private const string JsonMimeType = "application/json";
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiApiClient> _logger;

    public OpenAiApiClient(HttpClient httpClient, OpenAiOptions options, ILogger<OpenAiApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(90);
    }

    public async Task<RealtimeSessionResponse> CreateRealtimeClientSecretAsync(RealtimeSessionRequest request, CancellationToken cancellationToken)
    {
        EnsureApiKey();

        var requestedModel = string.IsNullOrWhiteSpace(request.Model) ? _options.RealtimeModel : request.Model;
        var requestedVoice = string.IsNullOrWhiteSpace(request.Voice) ? _options.RealtimeVoice : request.Voice;
        var requestedInstructions = string.IsNullOrWhiteSpace(request.Instructions) ? _options.RealtimeInstructions : request.Instructions;

        var payload = new
        {
            session = new
            {
                model = requestedModel,
                voice = requestedVoice,
                instructions = requestedInstructions
            }
        };

        using var requestMessage = CreateJsonRequest(HttpMethod.Post, "v1/realtime/client_secrets", payload);
        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        var root = await ParseJsonOrThrowAsync(response, cancellationToken);

        var clientSecret = TryGetString(root, "client_secret", "value")
            ?? TryGetString(root, "value");

        var sessionId = TryGetString(root, "session", "id")
            ?? TryGetString(root, "id");

        var expiresAt = TryGetUnixTimestamp(root, "client_secret", "expires_at")
            ?? TryGetUnixTimestamp(root, "expires_at");

        return new RealtimeSessionResponse(
            ClientSecret: clientSecret,
            SessionId: sessionId,
            ExpiresAt: expiresAt?.ToString("O"),
            Model: requestedModel ?? _options.RealtimeModel,
            Voice: requestedVoice ?? _options.RealtimeVoice);
    }

    public async Task<string> TranscribeAudioAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        EnsureApiKey();

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        multipart.Add(fileContent, "file", "turn.wav");
        multipart.Add(new StringContent(_options.ModularTranscribeModel, Encoding.UTF8), "model");
        multipart.Add(new StringContent("json", Encoding.UTF8), "response_format");

        using var request = CreateRequest(HttpMethod.Post, "v1/audio/transcriptions");
        request.Content = multipart;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var root = await ParseJsonOrThrowAsync(response, cancellationToken);
        var text = TryGetString(root, "text");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new OpenAiApiException("Transkript fehlt in der Antwort von /v1/audio/transcriptions.");
        }

        return text;
    }

    public async Task<string> GenerateAssistantTextAsync(
        IReadOnlyList<(string Role, string Text)> history,
        string userText,
        CancellationToken cancellationToken)
    {
        EnsureApiKey();

        if (string.IsNullOrWhiteSpace(_options.ModularTextModel))
        {
            throw new OpenAiApiException("OPENAI_TEXT_MODEL ist nicht gesetzt. Bitte in backend/.env konfigurieren.");
        }

        var input = new List<object>(history.Count + 1);
        foreach (var message in history)
        {
            input.Add(new
            {
                role = message.Role,
                content = new[]
                {
                    new
                    {
                        type = "input_text",
                        text = message.Text
                    }
                }
            });
        }

        input.Add(new
        {
            role = "user",
            content = new[]
            {
                new
                {
                    type = "input_text",
                    text = userText
                }
            }
        });

        var payload = new
        {
            model = _options.ModularTextModel,
            instructions = _options.ModularInstructions,
            input
        };

        using var requestMessage = CreateJsonRequest(HttpMethod.Post, "v1/responses", payload);
        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        var root = await ParseJsonOrThrowAsync(response, cancellationToken);

        var outputText = TryGetString(root, "output_text");
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            return outputText;
        }

        var fallback = TryReadOutputTextFromArray(root);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        throw new OpenAiApiException("Textantwort fehlt in der Antwort von /v1/responses.");
    }

    public async Task<byte[]> SynthesizeSpeechAsync(string assistantText, CancellationToken cancellationToken)
    {
        EnsureApiKey();

        var payload = new
        {
            model = _options.ModularTtsModel,
            voice = _options.ModularTtsVoice,
            input = assistantText,
            response_format = _options.ModularTtsFormat
        };

        using var requestMessage = CreateJsonRequest(HttpMethod.Post, "v1/audio/speech", payload);
        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenAiApiException(
                $"OpenAI TTS Fehler ({(int)response.StatusCode}): {TrimForLog(errorBody)}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<ClientWebSocket> ConnectRealtimeWebSocketAsync(string? modelOverride, CancellationToken cancellationToken)
    {
        EnsureApiKey();

        var targetUri = _options.GetRealtimeWebSocketUri(modelOverride);
        var clientSocket = new ClientWebSocket();
        clientSocket.Options.SetRequestHeader("Authorization", $"Bearer {_options.ApiKey}");
        clientSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _logger.LogInformation("Verbinde Realtime-WebSocket: {Uri}", targetUri);
        await clientSocket.ConnectAsync(targetUri, cancellationToken);
        return clientSocket;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMimeType));
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string relativePath, object payload)
    {
        var request = CreateRequest(method, relativePath);
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null
        };
    }

    private static DateTimeOffset? TryGetUnixTimestamp(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return null;
    }

    private static string? TryReadOutputTextFromArray(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("text", out var textElement))
                {
                    continue;
                }

                var text = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string TrimForLog(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "Keine Fehlermeldung im Body.";
        }

        const int max = 600;
        return input.Length <= max ? input : input[..max] + "...";
    }

    private async Task<JsonElement> ParseJsonOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAiApiException(
                $"OpenAI API Fehler ({(int)response.StatusCode}): {TrimForLog(body)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new OpenAiApiException("OpenAI API lieferte keine gueltige JSON-Antwort.");
        }
    }

    private void EnsureApiKey()
    {
        if (!_options.HasApiKey)
        {
            throw new OpenAiApiException("OPENAI_API_KEY ist nicht gesetzt.");
        }
    }
}

public sealed class OpenAiApiException : Exception
{
    public OpenAiApiException(string message)
        : base(message)
    {
    }
}
