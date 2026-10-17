using UnityEngine;

namespace HornetInHallownest.Util;

// Unity 6 APIs, falling back to their predecessors on HK 1.5.78 (Unity 2020.2), where they don't exist yet.
internal static class UnityApi {
    internal static T[] FindAll<T>(bool includeInactive = false) where T : Object =>
#if HK1578
        Object.FindObjectsOfType<T>(includeInactive);
#else
        Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#endif

    internal static T? FindAny<T>() where T : Object =>
#if HK1578
        Object.FindObjectOfType<T>();
#else
        Object.FindAnyObjectByType<T>();
#endif

    internal static Vector2 GetVelocity(this Rigidbody2D rb) =>
#if HK1578
        rb.velocity;
#else
        rb.linearVelocity;
#endif

    internal static void SetVelocity(this Rigidbody2D rb, Vector2 velocity) {
#if HK1578
        rb.velocity = velocity;
#else
        rb.linearVelocity = velocity;
#endif
    }
}
