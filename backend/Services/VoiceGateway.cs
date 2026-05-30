using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualInfluencer.Backend.Configuration;
using VirtualInfluencer.Backend.Contracts;

namespace VirtualInfluencer.Backend.Services;

public sealed class VoiceGateway
{
    private readonly OpenAiApiClient _openAiApiClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<VoiceGateway> _logger;

    public VoiceGateway(OpenAiApiClient openAiApiClient, OpenAiOptions options, ILogger<VoiceGateway> logger)
    {
        _openAiApiClient = openAiApiClient;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext httpContext, VoicePipelineMode mode)
    {
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket Request erwartet.");
            return;
        }

        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync();
        var modeText = mode == VoicePipelineMode.Realtime ? "realtime" : "modular";
        _logger.LogInformation("Neue Voice-Socket Verbindung: {Mode}", modeText);

        if (!_options.HasApiKey)
        {
            await SendEventAsync(socket, VoiceEventFactory.Error("OPENAI_API_KEY ist nicht gesetzt.", "missing_api_key"), httpContext.RequestAborted);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "OPENAI_API_KEY fehlt.", httpContext.RequestAborted);
            return;
        }

        await SendEventAsync(socket, VoiceEventFactory.StateChanged("Idle", modeText), httpContext.RequestAborted);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        try
        {
            if (mode == VoicePipelineMode.Realtime)
            {
                await HandleRealtimeAsync(socket, cts.Token);
                return;
            }

            await HandleModularAsync(socket, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Voice-Gateway Fehler in {Mode}", modeText);
            await SendEventAsync(socket, VoiceEventFactory.Error(exception.Message, "gateway_error"), CancellationToken.None);
        }
        finally
        {
            await CloseSocketSafelyAsync(socket, WebSocketCloseStatus.NormalClosure, "Gateway beendet", CancellationToken.None);
        }
    }

    private async Task HandleRealtimeAsync(WebSocket clientSocket, CancellationToken cancellationToken)
    {
        using var realtimeSocket = await _openAiApiClient.ConnectRealtimeWebSocketAsync(_options.RealtimeModel, cancellationToken);
        await ConfigureRealtimeSessionAsync(realtimeSocket, cancellationToken);

        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clientToOpenAiTask = RelayClientToRealtimeAsync(clientSocket, realtimeSocket, relayCancellation.Token);
        var openAiToClientTask = RelayRealtimeToClientAsync(realtimeSocket, clientSocket, relayCancellation.Token);

        var completed = await Task.WhenAny(clientToOpenAiTask, openAiToClientTask);
        relayCancellation.Cancel();

        await AwaitRelayTaskAsync(completed);
        await AwaitRelayTaskAsync(clientToOpenAiTask);
        await AwaitRelayTaskAsync(openAiToClientTask);
    }

    private async Task HandleModularAsync(WebSocket clientSocket, CancellationToken cancellationToken)
    {
        var context = new ModularSessionContext(_options);

        while (!cancellationToken.IsCancellationRequested && clientSocket.State == WebSocketState.Open)
        {
            var clientEvent = await ReceiveClientEventAsync(clientSocket, cancellationToken);
            if (clientEvent is null)
            {
                return;
            }

            switch (clientEvent.Value.Type)
            {
                case "mode.set":
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "modular"), cancellationToken);
                    break;
                case "vad.toggle":
                    context.VadEnabled = TryReadBool(clientEvent.Value.Raw, "enabled");
                    await SendEventAsync(
                        clientSocket,
                        VoiceEventFactory.StateChanged("Idle", "modular", $"VAD={(context.VadEnabled ? "on" : "off")}"),
                        cancellationToken);
                    break;
                case "ptt.down":
                    context.StartTurn();
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Listening", "modular"), cancellationToken);
                    break;
                case "audio.chunk":
                    var chunkBase64 = TryReadString(clientEvent.Value.Raw, "audioBase64");
                    if (string.IsNullOrWhiteSpace(chunkBase64))
                    {
                        break;
                    }

                    var decodeSuccess = TryDecodeBase64(chunkBase64, out var decodedBytes);
                    if (!decodeSuccess || decodedBytes is null)
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.Error("Ungueltiger Audio-Chunk empfangen.", "invalid_audio"), cancellationToken);
                        break;
                    }

                    context.AppendAudio(decodedBytes);
                    break;
                case "ptt.up":
                    await ProcessModularTurnAsync(clientSocket, context, cancellationToken);
                    break;
                case "conversation.reset":
                    context.ResetConversation();
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "modular", "Conversation reset"), cancellationToken);
                    break;
                default:
                    await SendEventAsync(clientSocket, VoiceEventFactory.Error($"Unbekannter Event-Typ: {clientEvent.Value.Type}", "unknown_event"), cancellationToken);
                    break;
            }
        }
    }

    private async Task ConfigureRealtimeSessionAsync(WebSocket realtimeSocket, CancellationToken cancellationToken)
    {
        var sessionUpdate = new
        {
            type = "session.update",
            session = new
            {
                instructions = _options.RealtimeInstructions,
                voice = _options.RealtimeVoice,
                turn_detection = new
                {
                    type = "server_vad",
                    create_response = true,
                    interrupt_response = true,
                    silence_duration_ms = 500,
                    prefix_padding_ms = 300
                }
            }
        };

        await SendJsonToSocketAsync(realtimeSocket, sessionUpdate, cancellationToken);
    }

    private async Task RelayClientToRealtimeAsync(WebSocket clientSocket, WebSocket realtimeSocket, CancellationToken cancellationToken)
    {
        var manualTurnControl = false;

        while (!cancellationToken.IsCancellationRequested
               && clientSocket.State == WebSocketState.Open
               && realtimeSocket.State == WebSocketState.Open)
        {
            var clientEvent = await ReceiveClientEventAsync(clientSocket, cancellationToken);
            if (clientEvent is null)
            {
                return;
            }

            switch (clientEvent.Value.Type)
            {
                case "mode.set":
                    break;
                case "vad.toggle":
                    var enabled = TryReadBool(clientEvent.Value.Raw, "enabled");
                    manualTurnControl = !enabled;
                    if (enabled)
                    {
                        await SendJsonToSocketAsync(realtimeSocket, new
                        {
                            type = "session.update",
                            session = new
                            {
                                turn_detection = new
                                {
                                    type = "server_vad",
                                    create_response = true,
                                    interrupt_response = true,
                                    silence_duration_ms = 500,
                                    prefix_padding_ms = 300
                                }
                            }
                        }, cancellationToken);
                    }
                    else
                    {
                        await SendJsonToSocketAsync(realtimeSocket, new
                        {
                            type = "session.update",
                            session = new
                            {
                                turn_detection = (object?)null
                            }
                        }, cancellationToken);
                    }

                    break;
                case "audio.chunk":
                    var audioChunkBase64 = TryReadString(clientEvent.Value.Raw, "audioBase64");
                    if (!string.IsNullOrWhiteSpace(audioChunkBase64))
                    {
                        await SendJsonToSocketAsync(realtimeSocket, new
                        {
                            type = "input_audio_buffer.append",
                            audio = audioChunkBase64
                        }, cancellationToken);
                    }

                    break;
                case "ptt.down":
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Listening", "realtime"), cancellationToken);
                    if (manualTurnControl)
                    {
                        await SendJsonToSocketAsync(realtimeSocket, new { type = "input_audio_buffer.clear" }, cancellationToken);
                    }

                    break;
                case "ptt.up":
                    if (manualTurnControl)
                    {
                        await SendJsonToSocketAsync(realtimeSocket, new { type = "input_audio_buffer.commit" }, cancellationToken);
                        await SendJsonToSocketAsync(realtimeSocket, new
                        {
                            type = "response.create",
                            response = new
                            {
                                modalities = new[] { "audio", "text" }
                            }
                        }, cancellationToken);
                    }

                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Thinking", "realtime"), cancellationToken);
                    break;
                case "conversation.reset":
                    await SendJsonToSocketAsync(realtimeSocket, new { type = "response.cancel" }, cancellationToken);
                    await SendJsonToSocketAsync(realtimeSocket, new { type = "input_audio_buffer.clear" }, cancellationToken);
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "realtime", "Conversation reset"), cancellationToken);
                    break;
                default:
                    await SendEventAsync(clientSocket, VoiceEventFactory.Error($"Unbekannter Event-Typ: {clientEvent.Value.Type}", "unknown_event"), cancellationToken);
                    break;
            }
        }
    }

    private async Task RelayRealtimeToClientAsync(WebSocket realtimeSocket, WebSocket clientSocket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
               && realtimeSocket.State == WebSocketState.Open
               && clientSocket.State == WebSocketState.Open)
        {
            var rawMessage = await ReceiveTextMessageAsync(realtimeSocket, cancellationToken, _options.IdleTimeoutSeconds);
            if (rawMessage is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;
            var eventType = TryReadString(root, "type");
            if (string.IsNullOrWhiteSpace(eventType))
            {
                continue;
            }

            switch (eventType)
            {
                case "input_audio_buffer.speech_started":
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Listening", "realtime"), cancellationToken);
                    break;
                case "response.created":
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Thinking", "realtime"), cancellationToken);
                    break;
                case "response.output_audio.delta":
                case "response.audio.delta":
                    var deltaAudio = TryReadString(root, "delta");
                    if (!string.IsNullOrWhiteSpace(deltaAudio))
                    {
                        await SendEventAsync(
                            clientSocket,
                            VoiceEventFactory.AssistantAudioChunk(deltaAudio, "pcm16", _options.InputSampleRateHz),
                            cancellationToken);
                        await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Speaking", "realtime"), cancellationToken);
                    }

                    break;
                case "response.output_audio.done":
                case "response.audio.done":
                case "response.done":
                    await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "realtime"), cancellationToken);
                    break;
                case "response.output_audio_transcript.delta":
                case "response.audio_transcript.delta":
                    var transcriptDelta = TryReadString(root, "delta");
                    if (!string.IsNullOrWhiteSpace(transcriptDelta))
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.TranscriptDelta(transcriptDelta), cancellationToken);
                    }

                    break;
                case "response.output_audio_transcript.done":
                case "response.audio_transcript.done":
                    var transcriptFinal = TryReadString(root, "transcript")
                        ?? TryReadString(root, "text");
                    if (!string.IsNullOrWhiteSpace(transcriptFinal))
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.TranscriptFinal(transcriptFinal), cancellationToken);
                    }

                    break;
                case "response.output_text.delta":
                    var textDelta = TryReadString(root, "delta");
                    if (!string.IsNullOrWhiteSpace(textDelta))
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.AssistantText(textDelta), cancellationToken);
                    }

                    break;
                case "response.output_text.done":
                    var outputText = TryReadString(root, "text");
                    if (!string.IsNullOrWhiteSpace(outputText))
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.AssistantText(outputText), cancellationToken);
                    }

                    break;
                case "conversation.item.input_audio_transcription.completed":
                    var inputTranscript = TryReadString(root, "transcript");
                    if (!string.IsNullOrWhiteSpace(inputTranscript))
                    {
                        await SendEventAsync(clientSocket, VoiceEventFactory.TranscriptFinal(inputTranscript), cancellationToken);
                    }

                    break;
                case "error":
                    var errorMessage = TryReadString(root, "error", "message")
                        ?? TryReadString(root, "message")
                        ?? "Unbekannter Realtime-Fehler.";
                    await SendEventAsync(clientSocket, VoiceEventFactory.Error(errorMessage, "realtime_error"), cancellationToken);
                    break;
            }
        }
    }

    private async Task ProcessModularTurnAsync(WebSocket clientSocket, ModularSessionContext context, CancellationToken cancellationToken)
    {
        var turnPcm16 = context.EndTurnAndExtractAudio();
        if (turnPcm16.Length == 0)
        {
            await SendEventAsync(clientSocket, VoiceEventFactory.Error("Kein Audio im Turn empfangen.", "empty_turn"), cancellationToken);
            await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "modular"), cancellationToken);
            return;
        }

        await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Thinking", "modular"), cancellationToken);

        try
        {
            var wavBytes = WrapPcm16ToWav(turnPcm16, _options.InputSampleRateHz, _options.InputChannels);
            var transcript = await _openAiApiClient.TranscribeAudioAsync(wavBytes, cancellationToken);
            context.AddUserMessage(transcript);
            await SendEventAsync(clientSocket, VoiceEventFactory.TranscriptFinal(transcript), cancellationToken);

            var assistantText = await _openAiApiClient.GenerateAssistantTextAsync(context.History, transcript, cancellationToken);
            context.AddAssistantMessage(assistantText);
            await SendEventAsync(clientSocket, VoiceEventFactory.AssistantText(assistantText), cancellationToken);

            await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Speaking", "modular"), cancellationToken);
            var ttsAudio = await _openAiApiClient.SynthesizeSpeechAsync(assistantText, cancellationToken);
            var ttsAudioBase64 = Convert.ToBase64String(ttsAudio);
            await SendEventAsync(
                clientSocket,
                VoiceEventFactory.AssistantAudioChunk(ttsAudioBase64, _options.ModularTtsFormat, _options.InputSampleRateHz),
                cancellationToken);
        }
        catch (OpenAiApiException exception)
        {
            await SendEventAsync(clientSocket, VoiceEventFactory.Error(exception.Message, "openai_error"), cancellationToken);
        }
        catch (Exception exception)
        {
            await SendEventAsync(clientSocket, VoiceEventFactory.Error(exception.Message, "modular_error"), cancellationToken);
        }
        finally
        {
            await SendEventAsync(clientSocket, VoiceEventFactory.StateChanged("Idle", "modular"), cancellationToken);
        }
    }

    private static byte[] WrapPcm16ToWav(byte[] pcmBytes, int sampleRateHz, int channels)
    {
        const short bitsPerSample = 16;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRateHz * blockAlign;
        var dataSize = pcmBytes.Length;

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRateHz);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(pcmBytes);
        writer.Flush();

        return stream.ToArray();
    }

    private static async Task SendJsonToSocketAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);
        await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task SendEventAsync(WebSocket socket, JsonObject payload, CancellationToken cancellationToken)
    {
        var json = payload.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);
        await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<ClientVoiceEvent?> ReceiveClientEventAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var rawMessage = await ReceiveTextMessageAsync(socket, cancellationToken, _options.IdleTimeoutSeconds);
        if (rawMessage is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawMessage);
        var root = document.RootElement.Clone();
        var type = TryReadString(root, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return new ClientVoiceEvent(type, root);
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken, int idleTimeoutSeconds)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(idleTimeoutSeconds));

        while (!timeout.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task CloseSocketSafelyAsync(WebSocket socket, WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(status, reason, cancellationToken);
        }
    }

    private static bool TryDecodeBase64(string input, out byte[]? bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(input);
            return true;
        }
        catch (FormatException)
        {
            bytes = null;
            return false;
        }
    }

    private static string? TryReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var node in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(node, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static bool TryReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True;
    }

    private async Task AwaitRelayTaskAsync(Task relayTask)
    {
        try
        {
            await relayTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException exception)
        {
            _logger.LogWarning(exception, "WebSocket relay beendet.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Relay Task Fehler.");
        }
    }
}

internal sealed class ModularSessionContext
{
    private readonly OpenAiOptions _options;
    private readonly List<(string Role, string Text)> _history = new();
    private readonly MemoryStream _turnPcm16 = new();
    private readonly int _maxTurnBytes;

    public ModularSessionContext(OpenAiOptions options)
    {
        _options = options;
        _maxTurnBytes = options.InputSampleRateHz * options.InputChannels * 2 * options.MaxTurnAudioSeconds;
    }

    public bool VadEnabled { get; set; }

    public IReadOnlyList<(string Role, string Text)> History => _history;

    public void StartTurn()
    {
        _turnPcm16.SetLength(0);
    }

    public void AppendAudio(byte[] bytes)
    {
        if (_turnPcm16.Length + bytes.Length > _maxTurnBytes)
        {
            return;
        }

        _turnPcm16.Write(bytes, 0, bytes.Length);
    }

    public byte[] EndTurnAndExtractAudio()
    {
        var payload = _turnPcm16.ToArray();
        _turnPcm16.SetLength(0);
        return payload;
    }

    public void AddUserMessage(string text)
    {
        _history.Add(("user", text));
        TrimHistory();
    }

    public void AddAssistantMessage(string text)
    {
        _history.Add(("assistant", text));
        TrimHistory();
    }

    public void ResetConversation()
    {
        _history.Clear();
        _turnPcm16.SetLength(0);
    }

    private void TrimHistory()
    {
        const int maxMessages = 14;
        if (_history.Count <= maxMessages)
        {
            return;
        }

        var removeCount = _history.Count - maxMessages;
        _history.RemoveRange(0, removeCount);
    }
}
