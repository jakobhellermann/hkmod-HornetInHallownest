extern alias Silksong;
using System;
using System.Linq;
using HornetInHallownest.Core;
using HutongGames.PlayMaker.Actions;
using TMProOld;
using UnityEngine;
using HkLanguage = TeamCherry.Localization.Language;
using Object = UnityEngine.Object;
using SHeroController = Silksong::HeroController;

namespace HornetInHallownest.Modules;

// HK breaks the Delicate Flower from the Knight's ProxyFSM ("Flower?", on his damage): the flag, a petal burst, a relic
// message and a save. Hornet's hits never reach that FSM, so this replays the state for them.
public sealed class DelicateFlowerModule : ModuleBase {
    private const float EffectLifetime = 3f;

    public override string Id => "delicate-flower";

    public override void Initialize() {
        Detour(typeof(SHeroController), nameof(SHeroController.SendHeroDamagedEvent), OnSendHeroDamagedEvent,
            typeof(SHeroController.DamageInfo));
    }

    private static void OnSendHeroDamagedEvent(Action<SHeroController, SHeroController.DamageInfo> orig,
        SHeroController self, SHeroController.DamageInfo damageInfo) {
        orig(self, damageInfo);
        if (HeroSwitch.HornetActive && self == HornetSpawner.Hornet) Break(self);
    }

    private static void Break(SHeroController hero) {
        var pd = PlayerData.instance;
        if (!pd.hasXunFlower || pd.xunFlowerBroken) return;
        pd.SetBool(nameof(PlayerData.xunFlowerBroken), true);
        pd.IncrementInt(nameof(PlayerData.xunFlowerBrokeTimes));

        var knight = HeroController.UnsafeInstance;
        var proxy = knight ? knight.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "ProxyFSM") : null;
        var state = proxy ? proxy.Fsm.GetState("Flower?") : null;
        if (knight && state != null) {
            PlayEffect(knight, hero);
            ShowMessage(state);
        }

        GameManager.instance.SaveGame();
    }

    // The Knight's own effect stays hidden with him while Hornet plays (KnightPresence), so she gets a copy.
    private static void PlayEffect(HeroController knight, SHeroController hero) {
        var source = knight.transform.Find("White_Flower_Break");
        if (!source) return;
        var effect = Object.Instantiate(source.gameObject, hero.transform.position, source.rotation);
        foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (var fsm in effect.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = true;
        effect.SetActive(true);
        Object.Destroy(effect, EffectLifetime);
    }

    private static void ShowMessage(HutongGames.PlayMaker.FsmState state) {
        var prefab = state.Actions.OfType<SpawnObjectFromGlobalPool>().FirstOrDefault()?.gameObject.Value;
        if (!prefab) return;
        var msg = prefab.Spawn();
        var icon = msg.transform.Find("Icon");
        if (icon && icon.TryGetComponent<SpriteRenderer>(out var iconRenderer))
            iconRenderer.sprite =
                state.Actions.OfType<SetSpriteRendererSprite>().FirstOrDefault()?.sprite.Value as Sprite;
        var text = msg.transform.Find("Text");
        if (text && text.TryGetComponent<TextMeshPro>(out var textMesh))
            textMesh.text = HkLanguage.Get("NOTIFICATION_FLOWER_BREAK", "UI");
    }
}
