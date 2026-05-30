using System;
using UnityEngine;

namespace VirtualInfluencer
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class AssistantAudioPlayer : MonoBehaviour
    {
        [SerializeField] private VoiceWebSocketClient webSocketClient;
        [SerializeField] private AvatarLipSyncController lipSyncController;
        [SerializeField] private AudioSource outputSource;

        private void Start()
        {
            if (outputSource == null)
            {
                outputSource = GetComponent<AudioSource>();
            }

            if (webSocketClient == null)
            {
                webSocketClient = FindFirstObjectByType<VoiceWebSocketClient>();
            }

            if (lipSyncController == null)
            {
                lipSyncController = FindFirstObjectByType<AvatarLipSyncController>();
            }

            if (webSocketClient != null)
            {
                webSocketClient.ServerEventReceived += OnServerEvent;
            }
        }

        private void OnDestroy()
        {
            if (webSocketClient != null)
            {
                webSocketClient.ServerEventReceived -= OnServerEvent;
            }
        }

        private void OnServerEvent(VoiceServerEvent serverEvent)
        {
            if (serverEvent.type == "assistant.audio.chunk")
            {
                PlayAudioChunk(serverEvent);
                return;
            }

            if (serverEvent.type == "state.changed" && string.Equals(serverEvent.state, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                lipSyncController?.ResetFallbackMouth();
            }
        }

        private void PlayAudioChunk(VoiceServerEvent serverEvent)
        {
            if (string.IsNullOrWhiteSpace(serverEvent.audioBase64))
            {
                return;
            }

            byte[] decodedBytes;
            try
            {
                decodedBytes = Convert.FromBase64String(serverEvent.audioBase64);
            }
            catch (FormatException)
            {
                Debug.LogWarning("assistant.audio.chunk enthaelt kein gueltiges Base64.");
                return;
            }

            var format = string.IsNullOrWhiteSpace(serverEvent.format) ? "pcm16" : serverEvent.format.ToLowerInvariant();
            float[] samples;
            var channels = 1;
            var sampleRateHz = serverEvent.sampleRateHz > 0 ? serverEvent.sampleRateHz : 24000;

            if (format == "wav")
            {
                if (!AudioDecodeUtility.TryDecodeWavPcm16(decodedBytes, out samples, out channels, out sampleRateHz))
                {
                    Debug.LogWarning("WAV-Audio konnte nicht dekodiert werden.");
                    return;
                }
            }
            else
            {
                samples = AudioDecodeUtility.DecodePcm16(decodedBytes);
            }

            if (samples.Length == 0)
            {
                return;
            }

            var clipSamples = samples.Length / channels;
            var clip = AudioClip.Create($"assistant_{Time.frameCount}", clipSamples, channels, sampleRateHz, false);
            clip.SetData(samples, 0);
            outputSource.PlayOneShot(clip);
            lipSyncController?.FeedSamples(samples);
        }
    }
}
