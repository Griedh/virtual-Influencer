using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualInfluencer.Backend.Contracts;

public enum VoicePipelineMode
{
    Realtime,
    Modular
}

public sealed record HealthResponse(
    string Status,
    DateTimeOffset TimestampUtc,
    bool OpenAiKeyConfigured,
    string DotNetRuntime);

public sealed record RealtimeSessionRequest(
    string? Model,
    string? Voice,
    string? Instructions);

public sealed record RealtimeSessionResponse(
    string? ClientSecret,
    string? SessionId,
    string? ExpiresAt,
    string Model,
    string Voice);

public sealed record ClientVoiceEvent(
    string Type,
    JsonElement Raw);

public static class VoiceEventFactory
{
    public static JsonObject StateChanged(string state, string mode, string? details = null)
    {
        var payload = new JsonObject
        {
            ["type"] = "state.changed",
            ["state"] = state,
            ["mode"] = mode,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(details))
        {
            payload["details"] = details;
        }

        return payload;
    }

    public static JsonObject TranscriptDelta(string text)
    {
        return new JsonObject
        {
            ["type"] = "transcript.delta",
            ["text"] = text,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };
    }

    public static JsonObject TranscriptFinal(string text)
    {
        return new JsonObject
        {
            ["type"] = "transcript.final",
            ["text"] = text,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };
    }

    public static JsonObject AssistantText(string text)
    {
        return new JsonObject
        {
            ["type"] = "assistant.text",
            ["text"] = text,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };
    }

    public static JsonObject AssistantAudioChunk(string audioBase64, string format, int sampleRateHz)
    {
        return new JsonObject
        {
            ["type"] = "assistant.audio.chunk",
            ["audioBase64"] = audioBase64,
            ["format"] = format,
            ["sampleRateHz"] = sampleRateHz,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };
    }

    public static JsonObject Error(string message, string? code = null)
    {
        var payload = new JsonObject
        {
            ["type"] = "error",
            ["message"] = message,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(code))
        {
            payload["code"] = code;
        }

        return payload;
    }
}
