extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using GlobalEnums;
using HornetInHallownest.Core;
using HutongGames.PlayMaker;
using Modding;
using UnityEngine;
using SLanguage = SilksongLoc::TeamCherry.Localization.Language;

namespace HornetInHallownest.Modules;

public sealed class ControlReminderModule : ModuleBase {
    private const string PromptsSheet = "Prompts";
    private const float UpButtonOffset = 1.3f;
    private static readonly string[] castButtons = ["ActionButton Cast", "ActionButton Cast JA"];

    private static readonly Dictionary<string, string> promptKeys = new() {
        ["PROMPT_ATTACK"] = "PROMPT_ATTACK",
        ["PROMPT_JUMP"] = "PROMPT_JUMP",
        ["PROMPT_INTERACT"] = "PROMPT_INTERACT",
        ["PROMPT_LOOK_DOWN"] = "PROMPT_LOOK_DOWN",
        ["PROMPT_DASH"] = "PROMPT_DASH",
        ["PROMPT_FOCUS"] = "PROMPT_BIND",
        ["PROMPT_FIREBALL"] = "PROMPT_SKILL",
        ["PROMPT_DREAMNAIL"] = "PROMPT_NEEDOLIN"
    };

    public override string Id => "control-reminder";

    public override void Initialize() {
        ModHooks.LanguageGetHook += OnLanguageGet;
        Detour(typeof(Fsm), "SwitchState", OnSwitchState, typeof(FsmState));
    }

    protected override void OnDeinitialize() => ModHooks.LanguageGetHook -= OnLanguageGet;

    private static string OnLanguageGet(string key, string sheet, string orig) {
        if (!HeroSwitch.HornetActive || sheet != PromptsSheet || !promptKeys.TryGetValue(key, out var hornetKey))
            return orig;
        if (key == "PROMPT_FOCUS" && InDreamerDream()) hornetKey = "PROMPT_NEEDOLIN";
        return SLanguage.Get(hornetKey, PromptsSheet);
    }

    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (self is not { Name: "Display", GameObjectName: "Control Reminder" }) return;
        if (to.Name is "Superdash" or "Interact")
            PlaceUpButton(self.GameObject.transform, to.Name == "Superdash" && HeroSwitch.HornetActive);

        HeroActionButton? action = to.Name switch {
            "Focus" => HeroSwitch.HornetActive && InDreamerDream()
                ? HeroActionButton.DREAM_NAIL
                : HeroActionButton.CAST,
            "Japanese?" or "Fireball" or "Fireball JA" =>
                HeroSwitch.HornetActive ? HeroActionButton.QUICK_CAST : HeroActionButton.CAST,
            _ => null
        };
        if (action == null) return;
        foreach (var name in castButtons)
            if (self.GameObject.transform.Find(name) is { } cast &&
                cast.TryGetComponent<ActionButtonIcon>(out var icon) &&
                icon.action != action)
                icon.SetAction(action.Value);
    }

    // Hornet charges the Crystal Heart on up + Clawline (CrystalHeartModule), so its reminder shows Up beside the Clawline
    // button. For the interact reminder Up sits where every other button does.
    private static void PlaceUpButton(Transform reminder, bool crystalHeart) {
        var up = reminder.Find("ActionButton Up");
        var superDash = reminder.Find("ActionButton SD");
        if (!up || !superDash) return;
        var x = superDash.localPosition.x - (crystalHeart ? UpButtonOffset : 0f);
        up.localPosition = new Vector3(x, up.localPosition.y, up.localPosition.z);
        if (crystalHeart) up.gameObject.SetActive(true);
    }

    private static bool InDreamerDream() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("Dream_Guardian_",
            StringComparison.Ordinal);
}
