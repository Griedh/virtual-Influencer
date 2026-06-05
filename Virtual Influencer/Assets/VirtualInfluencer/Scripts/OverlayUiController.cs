using System.Text;
using TMPro;
using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class OverlayUiController : MonoBehaviour
    {
        [SerializeField] private VoiceWebSocketClient webSocketClient;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text transcriptLabel;
        [SerializeField] private TMP_Text assistantLabel;
        [SerializeField] private TMP_Text connectionLabel;

        [SerializeField] private int maxTranscriptChars = 320;
        [SerializeField] private int maxAssistantChars = 240;

        private readonly StringBuilder _transcriptBuilder = new();
        private readonly StringBuilder _assistantBuilder = new();

        private void Start()
        {
            if (webSocketClient == null)
            {
                webSocketClient = FindAnyObjectByType<VoiceWebSocketClient>();
            }

            if (webSocketClient != null)
            {
                webSocketClient.ServerEventReceived += OnServerEvent;
                webSocketClient.ConnectionStateChanged += OnConnectionStateChanged;
            }

            SetStatus(AssistantState.Idle, VoiceMode.Realtime, string.Empty);
        }

        private void OnDestroy()
        {
            if (webSocketClient != null)
            {
                webSocketClient.ServerEventReceived -= OnServerEvent;
                webSocketClient.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        private void OnConnectionStateChanged(string stateText)
        {
            if (connectionLabel != null)
            {
                connectionLabel.text = $"Socket: {stateText}";
            }
        }

        private void OnServerEvent(VoiceServerEvent serverEvent)
        {
            switch (serverEvent.type)
            {
                case "state.changed":
                    var mode = serverEvent.mode == "modular" ? VoiceMode.Modular : VoiceMode.Realtime;
                    SetStatus(VoiceProtocol.ToAssistantState(serverEvent.state), mode, serverEvent.details);
                    break;
                case "transcript.delta":
                case "transcript.final":
                    AppendText(_transcriptBuilder, serverEvent.text, maxTranscriptChars);
                    if (transcriptLabel != null)
                    {
                        transcriptLabel.text = _transcriptBuilder.ToString();
                    }

                    break;
                case "assistant.text":
                    AppendText(_assistantBuilder, serverEvent.text, maxAssistantChars);
                    if (assistantLabel != null)
                    {
                        assistantLabel.text = _assistantBuilder.ToString();
                    }

                    break;
                case "error":
                    if (statusLabel != null)
                    {
                        statusLabel.text = $"Error: {serverEvent.message}";
                    }

                    break;
            }
        }

        private void SetStatus(AssistantState state, VoiceMode mode, string details)
        {
            if (statusLabel == null)
            {
                return;
            }

            var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" ({details})";
            statusLabel.text = $"{mode.ToWireValue()} | {state}{suffix}";
        }

        private static void AppendText(StringBuilder builder, string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            builder.Append(text);
            if (builder.Length <= maxChars)
            {
                return;
            }

            builder.Remove(0, builder.Length - maxChars);
        }
    }
}
