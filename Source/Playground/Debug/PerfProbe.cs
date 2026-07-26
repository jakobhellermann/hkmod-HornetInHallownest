extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetPlayer.DevServer;
using HornetPlayer.HornetInHallownest.Modules;
using UnityEngine;

namespace HornetPlayer.Playground;

// GET /perf?ms=1500&disable=fsm,animator,tk2d,particles,physics
// Samples fps over the window with the named component groups on Hornet held disabled (re-applied every frame so the
// hero's own systems can't re-enable them mid-measurement), then restores the original state. disable empty = baseline.
internal static class PerfProbe {
    internal static IEnumerator Measure(DevRequest req, Action<object?> respond) {
        var ms = int.TryParse(req["ms"], out var m) ? m : 1500;
        var groups = (req["disable"] ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet();
        var root = HornetSpawner.HornetRoot;
        if (!root) { respond(new { error = "no Hornet spawned" }); yield break; }

        var behaviours = new List<Behaviour>();
        if (groups.Contains("fsm")) behaviours.AddRange(root.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true));
        if (groups.Contains("animator")) behaviours.AddRange(root.GetComponentsInChildren<Animator>(true));
        if (groups.Contains("tk2d")) behaviours.AddRange(root.GetComponentsInChildren<tk2dSpriteAnimator>(true));
        if (groups.Contains("tk2dsprite")) behaviours.AddRange(root.GetComponentsInChildren<tk2dSprite>(true));
        if (groups.Contains("collider")) behaviours.AddRange(root.GetComponentsInChildren<Collider2D>(true));
        if (groups.Contains("mb")) behaviours.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
        // exclude=Type1,Type2 : keep these MonoBehaviour types enabled (for bisecting which one costs)
        var keep = (req["exclude"] ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet();
        if (keep.Count > 0) behaviours.RemoveAll(b => keep.Contains(b.GetType().Name));
        if (groups.Contains("hero")) {
            behaviours.AddRange(root.GetComponentsInChildren<Silksong::HeroController>(true));
            behaviours.AddRange(root.GetComponentsInChildren<Silksong::HeroAnimationController>(true));
        }
        // Not under Hornet_Real: our per-frame driver (dispatches HornetActiveUpdate + the InputHandler bookkeeping).
        if (groups.Contains("adapter"))
            behaviours.AddRange(UnityEngine.Object.FindObjectsByType<HornetEnvironmentAdapter>(FindObjectsSortMode.None));
        var beh = behaviours.Where(b => b).ToArray();
        var behWasEnabled = beh.Select(b => b.enabled).ToArray();

        var particles = groups.Contains("particles")
            ? root.GetComponentsInChildren<ParticleSystem>(true).Where(p => p).ToArray()
            : Array.Empty<ParticleSystem>();
        var partWasPlaying = particles.Select(p => p.isPlaying).ToArray();

        var bodies = groups.Contains("physics")
            ? root.GetComponentsInChildren<Rigidbody2D>(true).Where(r => r).ToArray()
            : Array.Empty<Rigidbody2D>();
        var bodyWasSim = bodies.Select(r => r.simulated).ToArray();

        var renderers = groups.Contains("render")
            ? root.GetComponentsInChildren<Renderer>(true).Where(r => r).ToArray()
            : Array.Empty<Renderer>();
        var rendWasEnabled = renderers.Select(r => r.enabled).ToArray();

        // "go" = deactivate the whole Hornet_Real subtree (mirrors the manual "hornet_real off" test — catches every
        // component, not just the grouped ones).
        var goOff = groups.Contains("go");
        if (goOff) root.SetActive(false);

        // goX = SetActive(false) the GameObjects owning component X (truly stops managers/sim that .enabled doesn't).
        var killGos = new List<GameObject>();
        if (groups.Contains("goparticles")) killGos.AddRange(root.GetComponentsInChildren<ParticleSystem>(true).Select(c => c.gameObject));
        if (groups.Contains("gotk2d")) killGos.AddRange(root.GetComponentsInChildren<tk2dSpriteAnimator>(true).Select(c => c.gameObject));
        if (groups.Contains("gorender")) killGos.AddRange(root.GetComponentsInChildren<Renderer>(true).Select(c => c.gameObject));
        if (groups.Contains("kids")) for (var i = 0; i < root.transform.childCount; i++) killGos.Add(root.transform.GetChild(i).gameObject);
        var killGosArr = killGos.Where(g => g && g.activeSelf).Distinct().ToArray();
        foreach (var g in killGosArr) g.SetActive(false);

        // Disable once (no per-frame fight — that would pollute the measurement and battle the hero's systems).
        foreach (var b in beh) b.enabled = false;
        foreach (var p in particles) p.Pause(false);
        foreach (var r in bodies) r.simulated = false;
        foreach (var r in renderers) r.enabled = false;

        var frames = 0;
        var t0 = Time.realtimeSinceStartup;
        var end = t0 + ms / 1000f;
        while (Time.realtimeSinceStartup < end) {
            frames++;
            yield return null;
        }
        var elapsed = Time.realtimeSinceStartup - t0;

        // How many the hero re-enabled during the window — if high, the fps number for this group is not trustworthy.
        var reEnabled = beh.Count(b => b && b.enabled)
                        + particles.Count(p => p && p.isPlaying)
                        + bodies.Count(r => r && r.simulated)
                        + renderers.Count(r => r && r.enabled);

        for (var i = 0; i < beh.Length; i++) beh[i].enabled = behWasEnabled[i];
        for (var i = 0; i < particles.Length; i++) if (partWasPlaying[i]) particles[i].Play(false);
        for (var i = 0; i < bodies.Length; i++) bodies[i].simulated = bodyWasSim[i];
        for (var i = 0; i < renderers.Length; i++) renderers[i].enabled = rendWasEnabled[i];
        foreach (var g in killGosArr) if (g) g.SetActive(true);
        if (goOff) root.SetActive(true);

        var fps = frames / elapsed;
        respond(new {
            disabled = groups.Count == 0 ? "(baseline)" : string.Join(",", groups),
            fps = Mathf.Round(fps),
            frameMs = (float)Math.Round(1000f / fps, 2),
            frames,
            held = beh.Length + particles.Length + bodies.Length + renderers.Length + killGosArr.Length,
            reEnabled // > 0 = the hero fought back, treat this row's fps with suspicion
        });
    }
}
