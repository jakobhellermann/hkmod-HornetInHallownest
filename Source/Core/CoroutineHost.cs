using UnityEngine;

namespace HornetInHallownest.Core;

// Live host for coroutines whose real owner can't run them: modules (not MonoBehaviours) and inactive GOs (Unity
// silently drops StartCoroutine on those).
public sealed class CoroutineHost : MonoBehaviour {
}
