using System;
using System.Linq;
using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class AvatarLipSyncController : MonoBehaviour
    {
        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private SkinnedMeshRenderer fallbackFaceRenderer;
        [SerializeField] private string fallbackMouthBlendShape = "A";
        [SerializeField] private float fallbackResponse = 14f;
        [SerializeField] private float fallbackGain = 18f;

        private int _fallbackBlendShapeIndex = -1;
        private float _targetMouthWeight;
        private float _currentMouthWeight;
        private float _lastFeedTime;
        private bool _useULipSync;

        private void Start()
        {
            if (avatarRoot == null)
            {
                avatarRoot = gameObject;
            }

            if (fallbackFaceRenderer != null)
            {
                _fallbackBlendShapeIndex = fallbackFaceRenderer.sharedMesh != null
                    ? fallbackFaceRenderer.sharedMesh.GetBlendShapeIndex(fallbackMouthBlendShape)
                    : -1;
            }

            _useULipSync = HasULipSyncSetup(avatarRoot);
            if (_useULipSync)
            {
                Debug.Log("uLipSync wurde erkannt. Fallback-LipSync ist deaktiviert.");
                return;
            }

            Debug.LogWarning("uLipSync nicht gefunden. Es wird Fallback-LipSync (Amplitude-basiert) genutzt.");
        }

        private void Update()
        {
            if (_useULipSync || fallbackFaceRenderer == null || _fallbackBlendShapeIndex < 0)
            {
                return;
            }

            if (Time.unscaledTime - _lastFeedTime > 0.35f)
            {
                _targetMouthWeight = 0f;
            }

            _currentMouthWeight = Mathf.Lerp(
                _currentMouthWeight,
                _targetMouthWeight,
                Time.unscaledDeltaTime * fallbackResponse);

            fallbackFaceRenderer.SetBlendShapeWeight(_fallbackBlendShapeIndex, _currentMouthWeight);
        }

        public void FeedSamples(float[] samples)
        {
            if (_useULipSync || samples == null || samples.Length == 0)
            {
                return;
            }

            var rms = CalculateRms(samples);
            _targetMouthWeight = Mathf.Clamp01(rms * fallbackGain) * 100f;
            _lastFeedTime = Time.unscaledTime;
        }

        public void ResetFallbackMouth()
        {
            _targetMouthWeight = 0f;
        }

        private static float CalculateRms(float[] samples)
        {
            var sum = 0f;
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                sum += sample * sample;
            }

            return Mathf.Sqrt(sum / samples.Length);
        }

        private static bool HasULipSyncSetup(GameObject root)
        {
            var lipSyncType = FindType("uLipSync.uLipSync");
            var blendShapeType = FindType("uLipSync.uLipSyncBlendShape");
            var blendShapeVrmType = FindType("uLipSync.uLipSyncBlendShapeVRM");

            if (lipSyncType == null)
            {
                return false;
            }

            var hasLipSync = root.GetComponentInChildren(lipSyncType, true) != null;
            var hasBlendShape = blendShapeType != null && root.GetComponentInChildren(blendShapeType, true) != null;
            var hasBlendShapeVrm = blendShapeVrmType != null && root.GetComponentInChildren(blendShapeVrmType, true) != null;

            return hasLipSync && (hasBlendShape || hasBlendShapeVrm);
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(type => type != null);
        }
    }
}
