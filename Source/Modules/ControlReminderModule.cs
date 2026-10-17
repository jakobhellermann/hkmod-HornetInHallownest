extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using GlobalEnums;
using HornetInHallownest.Core;
using HutongGames.PlayMaker;
using Modding;
using TMProOld;
using UnityEngine;
using HkLanguage = TeamCherry.Localization.Language;
using SLanguage = SilksongLoc::TeamCherry.Localization.Language;

namespace HornetInHallownest.Modules;

public sealed class ControlReminderModule : ModuleBase {
    private const string PromptsSheet = "Prompts";
    private const float DirectionButtonOffset = 1.3f;
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

    private const string FloorBreakPromptKey = "GET_QUAKE_1";
    private const float TwoLineBackboardHeight = 1.8f;
    private static bool isFloorBreakPending;
    private static Vector3? backboardScale;

    public override string Id => "control-reminder";

    // Hornet breaks Desolate Dive floors with her down-dash, which the Silk Skill screen she gets for the spell doesn't
    // teach. HK's dash reminder shows it, with the line from HK's Desolate Dive screen.
    internal static void ShowFloorBreakReminder() {
        isFloorBreakPending = true;
        PlayMakerFSM.BroadcastEvent("REMINDER DASH");
        // A reminder already up ignores the event, and the next dash reminder would carry this line instead.
        isFloorBreakPending = false;
    }

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
        var reminder = self.GameObject.transform;
        switch (to.Name) {
            case "Dash" when isFloorBreakPending:
                isFloorBreakPending = false;
                if (reminder.Find("Text") is { } text && text.TryGetComponent<TextMeshPro>(out var textMesh))
                    textMesh.text = SplitInTwoLines(HkLanguage.Get(FloorBreakPromptKey, PromptsSheet));
                if (reminder.Find("Backboard") is { } board) {
                    backboardScale ??= board.localScale;
                    board.localScale = Vector3.Scale(backboardScale.Value, new Vector3(1f, TwoLineBackboardHeight, 1f));
                }

                break;
            case "Idle" when backboardScale != null:
                if (reminder.Find("Backboard") is { } shared) shared.localScale = backboardScale.Value;
                backboardScale = null;
                break;
            case "Superdash":
                PlaceDirectionButton(reminder, "ActionButton Up", HeroSwitch.HornetActive);
                break;
            case "Interact":
                PlaceDirectionButton(reminder, "ActionButton Up", false);
                break;
            case "Dreamnail":
                PlaceDirectionButton(reminder, "ActionButton Down", HeroSwitch.HornetActive);
                break;
            case "Look Down Mawlek":
                PlaceDirectionButton(reminder, "ActionButton Down", false);
                break;
        }

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

    // At the space nearest the middle; a line in a language without spaces stays whole.
    private static string SplitInTwoLines(string line) {
        var middle = line.Length / 2;
        var before = line.LastIndexOf(' ', middle);
        var after = line.IndexOf(' ', middle);
        var split = before < 0 ? after : after < 0 || middle - before <= after - middle ? before : after;
        return split < 0 ? line : line.Substring(0, split) + "\n" + line.Substring(split + 1);
    }

    private static void PlaceDirectionButton(Transform reminder, string name, bool besideAction) {
        var direction = reminder.Find(name);
        var superDash = reminder.Find("ActionButton SD");
        if (!direction || !superDash) return;
        var x = superDash.localPosition.x - (besideAction ? DirectionButtonOffset : 0f);
        direction.localPosition = new Vector3(x, direction.localPosition.y, direction.localPosition.z);
        if (besideAction) direction.gameObject.SetActive(true);
    }

    private static bool InDreamerDream() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("Dream_Guardian_",
            StringComparison.Ordinal);
}
