using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class VirtualInfluencerApp : MonoBehaviour
    {
        [SerializeField] private VoiceWebSocketClient webSocketClient;
        [SerializeField] private MicrophoneStreamController microphoneController;

        [Header("Defaults")]
        [SerializeField] private VoiceMode startMode = VoiceMode.Realtime;

        [Header("Hotkeys")]
        [SerializeField] private KeyCode realtimeModeKey = KeyCode.F1;
        [SerializeField] private KeyCode modularModeKey = KeyCode.F2;
        [SerializeField] private KeyCode reconnectKey = KeyCode.C;
        [SerializeField] private KeyCode resetConversationKey = KeyCode.R;

        private async void Start()
        {
            if (webSocketClient == null)
            {
                webSocketClient = FindFirstObjectByType<VoiceWebSocketClient>();
            }

            if (microphoneController == null)
            {
                microphoneController = FindFirstObjectByType<MicrophoneStreamController>();
            }

            if (webSocketClient != null)
            {
                await webSocketClient.ConnectAsync(startMode);
                webSocketClient.SetMode(startMode);
                webSocketClient.SetVadEnabled(microphoneController != null && microphoneController.IsVadEnabled);
            }
        }

        private void Update()
        {
            if (webSocketClient == null)
            {
                return;
            }

            if (Input.GetKeyDown(realtimeModeKey))
            {
                _ = SwitchModeAsync(VoiceMode.Realtime);
            }

            if (Input.GetKeyDown(modularModeKey))
            {
                _ = SwitchModeAsync(VoiceMode.Modular);
            }

            if (Input.GetKeyDown(reconnectKey))
            {
                _ = webSocketClient.ConnectAsync(webSocketClient.CurrentMode);
            }

            if (Input.GetKeyDown(resetConversationKey))
            {
                webSocketClient.SendConversationReset();
            }
        }

        private async System.Threading.Tasks.Task SwitchModeAsync(VoiceMode mode)
        {
            await webSocketClient.ConnectAsync(mode);
            webSocketClient.SetMode(mode);
            webSocketClient.SetVadEnabled(microphoneController != null && microphoneController.IsVadEnabled);
        }
    }
}
