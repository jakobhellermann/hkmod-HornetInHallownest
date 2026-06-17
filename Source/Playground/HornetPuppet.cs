using System;
using UnityEngine;

namespace HornetPlayer.Playground;

// A visual-only Hornet puppet: parents the stripped tk2d body and keeps it positioned to the right of the live HK
// player (Knight) every frame, so it's easy to compare side by side while playing.
public class HornetPuppet : MonoBehaviour {
    public Vector3 Offset = new(4f, 0f, 0f);

    private HeroController? hero;

    private void LateUpdate() {
        try {
            if (hero == null) hero = UnityEngine.Object.FindFirstObjectByType<HeroController>();
            if (hero != null) transform.position = hero.transform.position + Offset;
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
