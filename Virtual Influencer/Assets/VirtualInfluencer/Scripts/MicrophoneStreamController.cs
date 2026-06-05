using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class MicrophoneStreamController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VoiceWebSocketClient webSocketClient;

        [Header("Input")]
        [SerializeField] private KeyCode pushToTalkKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode toggleVadKey = KeyCode.V;
        [SerializeField] private bool useVadByDefault = true;

        [Header("Audio Capture")]
        [SerializeField] private int sampleRateHz = 24000;
        [SerializeField] private int channels = 1;
        [SerializeField] private int captureBufferSeconds = 10;
        [SerializeField] private int chunkDurationMs = 120;

        [Header("VAD")]
        [SerializeField] private float vadThreshold = 0.015f;
        [SerializeField] private float vadSilenceHoldSeconds = 0.65f;

        public bool IsVadEnabled => _vadEnabled;

        private AudioClip _microphoneClip;
        private string _microphoneDevice;
        private bool _vadEnabled;
        private bool _isCaptureActive;
        private int _lastMicSamplePosition;
        private float _vadSilenceTimer;
        private int _chunkSizeBytes;
        private readonly List<byte> _pendingPcm16 = new();

        private void Start()
        {
            if (webSocketClient == null)
            {
                webSocketClient = FindAnyObjectByType<VoiceWebSocketClient>();
            }

            _vadEnabled = useVadByDefault;
            _chunkSizeBytes = Mathf.Max(320, sampleRateHz * channels * 2 * chunkDurationMs / 1000);
            StartMicrophone();
        }

        private void OnDisable()
        {
            StopCapture();
            StopMicrophone();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleVadKey))
            {
                ToggleVad();
            }

            if (!_vadEnabled)
            {
                if (Input.GetKeyDown(pushToTalkKey))
                {
                    StartCapture();
                }

                if (Input.GetKeyUp(pushToTalkKey))
                {
                    StopCapture();
                }
            }

            var samples = PullNewMicrophoneSamples();
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            if (_vadEnabled)
            {
                ProcessVadState(samples);
            }

            if (_isCaptureActive)
            {
                AppendPcm16(samples);
                FlushQueuedAudioChunks(sendRemainder: false);
            }
        }

        public void ForceVadState(bool enabled)
        {
            _vadEnabled = enabled;
            webSocketClient?.SetVadEnabled(enabled);
        }

        private void ToggleVad()
        {
            _vadEnabled = !_vadEnabled;
            webSocketClient?.SetVadEnabled(_vadEnabled);

            if (_vadEnabled)
            {
                StopCapture();
            }
        }

        private void StartCapture()
        {
            if (_isCaptureActive)
            {
                return;
            }

            _isCaptureActive = true;
            _pendingPcm16.Clear();
            _vadSilenceTimer = 0f;
            webSocketClient?.SendPttDown();
        }

        private void StopCapture()
        {
            if (!_isCaptureActive)
            {
                return;
            }

            FlushQueuedAudioChunks(sendRemainder: true);
            _isCaptureActive = false;
            webSocketClient?.SendPttUp();
            _pendingPcm16.Clear();
            _vadSilenceTimer = 0f;
        }

        private void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("Kein Mikrofon gefunden.");
                return;
            }

            _microphoneDevice = Microphone.devices[0];
            _microphoneClip = Microphone.Start(_microphoneDevice, true, captureBufferSeconds, sampleRateHz);
            _lastMicSamplePosition = 0;
            webSocketClient?.SetVadEnabled(_vadEnabled);
        }

        private void StopMicrophone()
        {
            if (string.IsNullOrWhiteSpace(_microphoneDevice))
            {
                return;
            }

            if (Microphone.IsRecording(_microphoneDevice))
            {
                Microphone.End(_microphoneDevice);
            }
        }

        private float[] PullNewMicrophoneSamples()
        {
            if (_microphoneClip == null || string.IsNullOrWhiteSpace(_microphoneDevice))
            {
                return Array.Empty<float>();
            }

            var currentPosition = Microphone.GetPosition(_microphoneDevice);
            if (currentPosition < 0 || currentPosition == _lastMicSamplePosition)
            {
                return Array.Empty<float>();
            }

            var sampleDelta = currentPosition - _lastMicSamplePosition;
            if (sampleDelta < 0)
            {
                sampleDelta += _microphoneClip.samples;
            }

            if (sampleDelta <= 0)
            {
                return Array.Empty<float>();
            }

            var data = new float[sampleDelta * channels];
            _microphoneClip.GetData(data, _lastMicSamplePosition);
            _lastMicSamplePosition = currentPosition;

            return data;
        }

        private void ProcessVadState(float[] samples)
        {
            var rms = CalculateRms(samples);
            var speaking = rms >= vadThreshold;

            if (speaking)
            {
                _vadSilenceTimer = 0f;
                if (!_isCaptureActive)
                {
                    StartCapture();
                }
                return;
            }

            if (!_isCaptureActive)
            {
                return;
            }

            _vadSilenceTimer += Time.unscaledDeltaTime;
            if (_vadSilenceTimer >= vadSilenceHoldSeconds)
            {
                StopCapture();
            }
        }

        private void AppendPcm16(float[] samples)
        {
            for (var index = 0; index < samples.Length; index++)
            {
                var clamped = Mathf.Clamp(samples[index], -1f, 1f);
                var sample = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                _pendingPcm16.Add((byte)(sample & 0xFF));
                _pendingPcm16.Add((byte)((sample >> 8) & 0xFF));
            }
        }

        private void FlushQueuedAudioChunks(bool sendRemainder)
        {
            if (webSocketClient == null || !webSocketClient.IsConnected)
            {
                return;
            }

            while (_pendingPcm16.Count >= _chunkSizeBytes)
            {
                var bytes = _pendingPcm16.GetRange(0, _chunkSizeBytes).ToArray();
                _pendingPcm16.RemoveRange(0, _chunkSizeBytes);
                webSocketClient.SendAudioPcm16(bytes, sampleRateHz, channels);
            }

            if (sendRemainder && _pendingPcm16.Count > 0)
            {
                var bytes = _pendingPcm16.ToArray();
                _pendingPcm16.Clear();
                webSocketClient.SendAudioPcm16(bytes, sampleRateHz, channels);
            }
        }

        private static float CalculateRms(float[] samples)
        {
            if (samples.Length == 0)
            {
                return 0f;
            }

            var sum = 0f;
            for (var index = 0; index < samples.Length; index++)
            {
                var value = samples[index];
                sum += value * value;
            }

            return Mathf.Sqrt(sum / samples.Length);
        }
    }
}
