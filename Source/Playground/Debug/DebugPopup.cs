using System.Collections;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Playground;

// POST /popup?text=..: a label beside Hornet that follows her until replaced or cleared (POST /popup-clear).
internal static class DebugPopup {
    private static TextMesh? label;

    internal static object Show(string text) {
        if (!label) {
            var go = new GameObject("Debug Popup");
            Object.DontDestroyOnLoad(go);
            label = go.AddComponent<TextMesh>();
#if HK1578
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#else
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#endif
            label.fontSize = 48;
            label.characterSize = 0.08f;
            label.anchor = TextAnchor.MiddleLeft;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = label.font.material;
            renderer.sortingOrder = 100;
            ModuleBase.Runtime.StartCoroutine(Follow(label));
        }

        label!.text = text;
        return new { text };
    }

    internal static object Clear() {
        if (label) Object.Destroy(label!.gameObject);
        label = null;
        return new { cleared = true };
    }

    private static IEnumerator Follow(TextMesh followed) {
        while (followed) {
            if (BundleSpike.HornetRoot is { } hornet)
                followed.transform.position = hornet.transform.position + new Vector3(1.5f, 1f, -0.1f);
            yield return null;
        }
    }
}
