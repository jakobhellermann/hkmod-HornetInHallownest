extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetInHallownest.Modules;

// Some HK cutscenes hide the hero and animate a separate Knight sprite in his place: lifted into a dream, taking a spell,
// the Dream Nail or Monarch Wings.
public sealed class KnightStandInModule : ModuleBase {
    private const string DefaultClip = "Collect Memory Orb";

    private static readonly Dictionary<string, string[]> standIns = new() {
        ["Fungus1_04_boss"] = ["Dreamer Scene 1/Knight Lift"],
        ["GG_Hornet_1"] = ["Dreamer Scene 1/Knight Lift"],
        ["RestingGrounds_04"] = ["Dreamer Scene 2/Knight Lift"],
        ["Dream_Nailcollection"] = ["Witch Control/Dream Nail Get/Knight Dummy"],
        ["RestingGrounds_07"] = ["Dream Moth/Knight Dummy"],
        ["Crossroads_ShamanTemple"] = ["_Props/Knight Get Fireball/Knight Cutscene Animator"],
        ["Ruins1_24"] = ["Quake Real Parent/Knight Get Quake/Knight Cutscene Animator"],
        ["Ruins1_24_boss"] = ["Quake Fake Parent/Knight Get Quake Fake/Knight Cutscene Animator"],
        ["Room_Fungus_Shaman"] = ["Scream Control/Knight Get Scream/Knight Cutscene Animator"],
        ["Abyss_21"] = ["Shiny Item DJ/Knight Cutscene"]
    };

    // The stand-ins' clips that aren't DefaultClip.
    private static readonly Dictionary<string, string> clips = new() {
        ["Collect Magical Fall"] = "Fall",
        ["Collect Magical Land"] = "Land",
        ["DN End"] = "Idle"
    };

    private static bool heroShown;

    public override string Id => "knight-stand-in";

    public override void Initialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        Detour(typeof(SetMeshRenderer), nameof(SetMeshRenderer.OnEnter), OnSetMeshRenderer);
    }

    protected override void OnDeinitialize() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

    // A cutscene that shows the hero again has taken her back from its stand-in, and places her itself.
    private static void OnSetMeshRenderer(Action<SetMeshRenderer> orig, SetMeshRenderer self) {
        var root = HornetSpawner.HornetRoot;
        if (self.active.Value && root && self.Fsm.GetOwnerDefaultTarget(self.gameObject) == root) heroShown = true;
        orig(self);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (!standIns.TryGetValue(scene.name, out var paths)) return;
        foreach (var path in paths)
            if (Find(scene, path) is { } standIn)
                StartCoroutine(TakePlace(standIn));
    }

    private static Transform? Find(Scene scene, string path) {
        var slash = path.IndexOf('/');
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == path.Substring(0, slash))
                return root.transform.Find(path.Substring(slash + 1));
        return null;
    }

    // Cutscenes show and hide one by switching it or its renderer, so it's hidden here by its color instead. Some stay
    // shown after their cutscene, which ends once it shows the hero again or hands her control back.
    private static IEnumerator TakePlace(Transform standIn) {
        var renderer = standIn.GetComponent<MeshRenderer>();
        var animator = standIn.GetComponent<tk2dSpriteAnimator>();
        var sprite = standIn.GetComponent<tk2dSprite>();
        while (true) {
            while (standIn && !IsShown()) yield return null;
            if (!standIn) yield break;
            if (HeroSwitch.HornetActive && HornetSpawner.Hornet is { } hero) yield return Replace(hero);
            while (standIn && IsShown()) yield return null;
        }

        bool IsShown() => standIn && standIn.gameObject.activeInHierarchy && (!renderer || renderer.enabled);

        IEnumerator Replace(Silksong::HeroController hero) {
            hero.StopAnimationControl();
            hero.AffectedByGravity(false);
            var color = sprite ? sprite.color : Color.white;
            if (sprite) sprite.color = Color.clear;
            string? playing = null;
            heroShown = false;
            while (standIn && IsShown() && hero && !heroShown && (hero.controlReqlinquished || !hero.acceptingInput)) {
                hero.GetComponent<MeshRenderer>().enabled = true;
                // A Knight sprite faces left at a positive x scale; Hornet's facing is her own state.
                if (standIn.lossyScale.x < 0f != hero.cState.facingRight) {
                    if (standIn.lossyScale.x < 0f) hero.FaceRight();
                    else hero.FaceLeft();
                }

                hero.transform.position = standIn.position + new Vector3(0f, HeroTargetModule.FeetDelta(), 0f);
                var clip = animator && animator.CurrentClip is { } current &&
                           clips.TryGetValue(current.name, out var mapped)
                    ? mapped
                    : DefaultClip;
                if (clip != playing) {
                    hero.AnimCtrl.PlayClipForced(clip);
                    playing = clip;
                }

                yield return null;
            }

            if (sprite && !IsShown()) sprite.color = color;
            if (standIn && hero) hero.AffectedByGravity(true);
        }
    }
}
