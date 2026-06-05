using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class VoiceWebSocketClient : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private string backendHttpBaseUrl = "http://127.0.0.1:5050";
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private VoiceMode startMode = VoiceMode.Realtime;

        public event Action<VoiceServerEvent> ServerEventReceived;
        public event Action<string> ConnectionStateChanged;

        public VoiceMode CurrentMode { get; private set; } = VoiceMode.Realtime;
        public bool IsConnected => _socket is { State: WebSocketState.Open };

        private ClientWebSocket _socket;
        private CancellationTokenSource _socketCts;
        private Task _receiveLoopTask;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentQueue<string> _incomingMessages = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        private void Start()
        {
            if (autoConnectOnStart)
            {
                _ = ConnectAsync(startMode);
            }
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action.Invoke();
            }

            while (_incomingMessages.TryDequeue(out var rawMessage))
            {
                var serverEvent = JsonUtility.FromJson<VoiceServerEvent>(rawMessage);
                if (serverEvent == null || string.IsNullOrWhiteSpace(serverEvent.type))
                {
                    continue;
                }

                ServerEventReceived?.Invoke(serverEvent);
            }
        }

        private void OnDestroy()
        {
            _ = DisconnectAsync();
        }

        public async Task ConnectAsync(VoiceMode mode)
        {
            await DisconnectAsync();

            CurrentMode = mode;
            _socketCts = new CancellationTokenSource();
            _socket = new ClientWebSocket();

            var endpoint = BuildEndpointUri(mode);
            try
            {
                QueueMainThread(() => ConnectionStateChanged?.Invoke($"Connecting: {endpoint}"));
                await _socket.ConnectAsync(endpoint, _socketCts.Token);
                QueueMainThread(() => ConnectionStateChanged?.Invoke("Connected"));

                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_socket, _socketCts.Token));
                await SendAsync(VoiceProtocol.CreateModeSet(mode), _socketCts.Token);
            }
            catch (Exception exception)
            {
                QueueMainThread(() => ConnectionStateChanged?.Invoke($"Connect failed: {exception.Message}"));
                await DisconnectAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_socketCts != null)
            {
                _socketCts.Cancel();
            }

            if (_socket != null)
            {
                try
                {
                    if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                    }
                }
                catch
                {
                }
                finally
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }

            if (_socketCts != null)
            {
                _socketCts.Dispose();
                _socketCts = null;
            }

            QueueMainThread(() => ConnectionStateChanged?.Invoke("Disconnected"));
        }

        public void SetMode(VoiceMode mode)
        {
            CurrentMode = mode;
            _ = SendAsync(VoiceProtocol.CreateModeSet(mode), _socketCts?.Token ?? CancellationToken.None);
        }

        public void SetVadEnabled(bool enabled)
        {
            _ = SendAsync(VoiceProtocol.CreateToggleVad(enabled), _socketCts?.Token ?? CancellationToken.None);
        }

        public void SendPttDown()
        {
            _ = SendAsync(VoiceProtocol.CreatePttDown(), _socketCts?.Token ?? CancellationToken.None);
        }

        public void SendPttUp()
        {
            _ = SendAsync(VoiceProtocol.CreatePttUp(), _socketCts?.Token ?? CancellationToken.None);
        }

        public void SendConversationReset()
        {
            _ = SendAsync(VoiceProtocol.CreateResetConversation(), _socketCts?.Token ?? CancellationToken.None);
        }

        public void SendAudioPcm16(byte[] pcm16Bytes, int sampleRateHz, int channels)
        {
            var evt = VoiceProtocol.CreateAudioChunk(pcm16Bytes, sampleRateHz, channels);
            _ = SendAsync(evt, _socketCts?.Token ?? CancellationToken.None);
        }

        private async Task SendAsync(VoiceClientEvent payload, CancellationToken cancellationToken)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            var json = JsonUtility.ToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (Exception exception)
            {
                QueueMainThread(() => ConnectionStateChanged?.Invoke($"Send failed: {exception.Message}"));
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using var messageStream = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            QueueMainThread(() => ConnectionStateChanged?.Invoke("Server closed socket"));
                            return;
                        }

                        messageStream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var rawMessage = Encoding.UTF8.GetString(messageStream.ToArray());
                    _incomingMessages.Enqueue(rawMessage);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                QueueMainThread(() => ConnectionStateChanged?.Invoke($"Receive failed: {exception.Message}"));
            }
        }

        private Uri BuildEndpointUri(VoiceMode mode)
        {
            var clean = backendHttpBaseUrl.TrimEnd('/');
            var baseUri = new Uri(clean, UriKind.Absolute);

            var scheme = baseUri.Scheme switch
            {
                "https" => "wss",
                "http" => "ws",
                _ => baseUri.Scheme
            };

            var path = mode == VoiceMode.Realtime ? "/voice/realtime" : "/voice/modular";
            var builder = new UriBuilder(baseUri)
            {
                Scheme = scheme,
                Path = path
            };
            return builder.Uri;
        }

        private void QueueMainThread(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }
    }
}
