using System;
using System.Linq;
using UnityEngine;

namespace VirtualInfluencer
{
    public sealed class ULipSyncSetupHelper : MonoBehaviour
    {
        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private AudioSource assistantAudioSource;
        [SerializeField] private bool logSetupHintsOnStart = true;

        private void Start()
        {
            if (!logSetupHintsOnStart)
            {
                return;
            }

            if (avatarRoot == null)
            {
                avatarRoot = gameObject;
            }

            var hasLipSync = HasComponent(avatarRoot, "uLipSync.uLipSync");
            var hasBlendShape = HasComponent(avatarRoot, "uLipSync.uLipSyncBlendShape")
                                || HasComponent(avatarRoot, "uLipSync.uLipSyncBlendShapeVRM");

            if (hasLipSync && hasBlendShape)
            {
                Debug.Log("uLipSync-Setup gefunden. Pruefe im Inspector, dass die AudioSource korrekt zugewiesen ist.");
                return;
            }

            Debug.LogWarning(
                "uLipSync ist noch nicht komplett eingerichtet. " +
                "Erwartet: uLipSync + uLipSyncBlendShape (oder uLipSyncBlendShapeVRM).");

            if (assistantAudioSource == null)
            {
                Debug.LogWarning("Assistant AudioSource fehlt. uLipSync kann ohne AudioSource kein Live-LipSync berechnen.");
            }
        }

        private static bool HasComponent(GameObject root, string typeName)
        {
            var type = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(found => found != null);

            return type != null && root.GetComponentInChildren(type, true) != null;
        }
    }
}
