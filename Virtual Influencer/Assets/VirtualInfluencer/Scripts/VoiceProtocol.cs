using System;
using UnityEngine;

namespace VirtualInfluencer
{
    public enum VoiceMode
    {
        Realtime,
        Modular
    }

    public enum AssistantState
    {
        Idle,
        Listening,
        Thinking,
        Speaking
    }

    [Serializable]
    public class VoiceClientEvent
    {
        public string type = string.Empty;
        public string mode = string.Empty;
        public bool enabled;
        public string audioBase64 = string.Empty;
        public string format = "pcm16";
        public int sampleRateHz = 24000;
        public int channels = 1;
    }

    [Serializable]
    public class VoiceServerEvent
    {
        public string type = string.Empty;
        public string state = string.Empty;
        public string mode = string.Empty;
        public string details = string.Empty;
        public string text = string.Empty;
        public string message = string.Empty;
        public string code = string.Empty;
        public string audioBase64 = string.Empty;
        public string format = "pcm16";
        public int sampleRateHz = 24000;
    }

    public static class VoiceProtocol
    {
        public static string ToWireValue(this VoiceMode mode)
        {
            return mode == VoiceMode.Realtime ? "realtime" : "modular";
        }

        public static AssistantState ToAssistantState(string rawState)
        {
            if (string.Equals(rawState, "Listening", StringComparison.OrdinalIgnoreCase))
            {
                return AssistantState.Listening;
            }

            if (string.Equals(rawState, "Thinking", StringComparison.OrdinalIgnoreCase))
            {
                return AssistantState.Thinking;
            }

            if (string.Equals(rawState, "Speaking", StringComparison.OrdinalIgnoreCase))
            {
                return AssistantState.Speaking;
            }

            return AssistantState.Idle;
        }

        public static VoiceClientEvent CreateModeSet(VoiceMode mode)
        {
            return new VoiceClientEvent
            {
                type = "mode.set",
                mode = mode.ToWireValue()
            };
        }

        public static VoiceClientEvent CreateToggleVad(bool enabled)
        {
            return new VoiceClientEvent
            {
                type = "vad.toggle",
                enabled = enabled
            };
        }

        public static VoiceClientEvent CreatePttDown()
        {
            return new VoiceClientEvent
            {
                type = "ptt.down"
            };
        }

        public static VoiceClientEvent CreatePttUp()
        {
            return new VoiceClientEvent
            {
                type = "ptt.up"
            };
        }

        public static VoiceClientEvent CreateResetConversation()
        {
            return new VoiceClientEvent
            {
                type = "conversation.reset"
            };
        }

        public static VoiceClientEvent CreateAudioChunk(byte[] bytes, int sampleRateHz, int channels)
        {
            return new VoiceClientEvent
            {
                type = "audio.chunk",
                audioBase64 = Convert.ToBase64String(bytes),
                format = "pcm16",
                sampleRateHz = sampleRateHz,
                channels = channels
            };
        }
    }
}
