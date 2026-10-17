extern alias Silksong;
using System;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules;

// HK's essence counter, shown on Hornet's HUD as a Silksong counter under her rosaries. The "Control" FSM of HK's counter
// keeps running while HK's HUD is hidden and still decides when the counter appears, updates and leaves.
public sealed class EssenceCounterModule : ModuleBase {
    private const string IconClip = "DN In";
    private const float FadeInTime = 0.1f;
    private const float FadeOutTime = 0.5f;

    private static GameObject? counter;
    private static Component? fade;
    private static Silksong::TextBridge? text;
    private static Silksong::CurrencyCounterIcon? icon;

    public override string Id => "essence-counter";

    public override void Initialize() {
        Detour(typeof(Fsm), "SwitchState", OnSwitchState, typeof(FsmState));
    }

    protected override void OnDeinitialize() {
        if (counter) Object.Destroy(counter);
        counter = null;
    }

    public override void HornetToggled(bool active) {
        if (!active && fade) fade.SetPropertyValue("AlphaSelf", 0f);
    }

    internal static bool IsHkCounter(PlayMakerFSM fsm) =>
        fsm.FsmName == "Control" && fsm.gameObject.name == "Dream Nail";

    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (self.Name != "Control" || self.GameObjectName != "Dream Nail" || !HeroSwitch.HornetActive) return;
        switch (to.Name) {
            case "Activate":
            case "Anim":
                Show(self.GameObject);
                break;
            case "Update":
                if (text) text.Text = PlayerData.instance.dreamOrbs.ToString();
                break;
            case "Deactivate":
                if (fade) fade.InvokeMethod("FadeToZero", FadeOutTime);
                break;
        }
    }

    private static void Show(GameObject hkCounter) {
        if (!counter && !Build(hkCounter)) return;
        text!.Text = PlayerData.instance.dreamOrbs.ToString();
        icon!.Appear();
        fade!.InvokeMethod("FadeToOne", FadeInTime);
    }

    private static bool Build(GameObject hkCounter) {
        var template =
            typeof(Silksong::ItemCurrencyCounter).GetFieldValue<Silksong::ItemCurrencyCounter>("_templateItem");
        if (!template) return false;
        var templateIcon = template.transform.Find("Parent/Geo Sprite").GetComponent<SpriteRenderer>();
        var definition = FindEssenceDefinition(hkCounter);
        if (definition?.material.mainTexture is not Texture2D texture) return false;

        // Cloned under an inactive holder, so the copy's item counter never wakes to join the stack's counters.
        var stack = template.transform.parent;
        var holder = new GameObject();
        holder.SetActive(false);
        counter = Object.Instantiate(template.gameObject, holder.transform);
        Object.DestroyImmediate(counter.GetComponent<Silksong::ItemCurrencyCounter>());
        Object.DestroyImmediate(counter.GetComponent<Animator>());
        counter.name = "Essence Counter";
        counter.transform.SetParent(stack, false);
        Object.Destroy(holder);

        // The slot under the rosaries, which are always shown.
        var slotY = stack.GetComponent<Silksong::CurrencyCounterStack>().GetFieldValue<float>("yOffset");
        counter.transform.localPosition = new Vector3(0f, slotY, 0f);
        counter.transform.Find("Parent/Roller Group").gameObject.SetActive(false);
        var iconRenderer = counter.transform.Find("Parent/Geo Sprite").GetComponent<SpriteRenderer>();
        SetIcon(iconRenderer, definition, texture, templateIcon.sprite.bounds.size.y);
        icon = iconRenderer.GetComponent<Silksong::CurrencyCounterIcon>();
        text = counter.transform.Find("Parent/Geo Text").GetComponent<Silksong::TextBridge>();
        fade = counter.GetComponents<Component>().First(c => c.GetType().Name == "NestedFadeGroup");
        fade.SetPropertyValue("AlphaSelf", 0f);
        return true;
    }

    private static tk2dSpriteDefinition? FindEssenceDefinition(GameObject hkCounter) {
        var animator = hkCounter.transform.Find("Sprite").GetComponent<tk2dSpriteAnimator>();
        var frame = animator.GetClipByName(IconClip)?.frames.LastOrDefault();
        return frame?.spriteCollection.spriteDefinitions[frame.spriteId];
    }

    // Cuts the sprite from its tk2d atlas at the height of Silksong's counter icons. The atlas can hold it rotated a
    // quarter turn: tk2d quad corners run left-bottom, right-bottom, left-top, so the first two UVs give the atlas
    // direction along the sprite's x and the first and third the one along its y, and the renderer rotates it back.
    private static void SetIcon(SpriteRenderer renderer, tk2dSpriteDefinition definition, Texture2D texture,
        float height) {
        var uvs = definition.uvs;
        var xDirection = uvs[1] - uvs[0];
        var yDirection = uvs[2] - uvs[0];
        var isRotated = Mathf.Abs(xDirection.y) > Mathf.Abs(xDirection.x);
        var min = new Vector2(uvs.Min(uv => uv.x), uvs.Min(uv => uv.y));
        var max = new Vector2(uvs.Max(uv => uv.x), uvs.Max(uv => uv.y));
        var rect = new Rect(min.x * texture.width, min.y * texture.height, (max.x - min.x) * texture.width,
            (max.y - min.y) * texture.height);
        renderer.sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f),
            (isRotated ? rect.width : rect.height) / height);
        if (isRotated) {
            // A quarter turn clockwise lays the atlas v along the sprite's x and u along its y; the flips point each the way
            // its UVs run.
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            renderer.flipX = yDirection.x > 0f;
            renderer.flipY = xDirection.y < 0f;
        }
        else {
            renderer.flipX = xDirection.x < 0f;
            renderer.flipY = yDirection.y < 0f;
        }
    }
}
