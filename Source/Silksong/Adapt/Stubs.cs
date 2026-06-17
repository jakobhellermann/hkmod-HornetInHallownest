using UnityEngine;

// Global namespace on purpose: these are all Silksong-only types (verified absent from HK), referenced from both the
// `namespace Silksong`-wrapped extracts AND the pristine `namespace GlobalSettings`/`GlobalEnums` extracts. Global is
// in scope for all of them; none collide with HK. (HK-colliding shims like CheatManager live in `namespace Silksong`.)
//
// Inert stubs for HeroController's combat / tools / quest / environment dependencies that we don't (yet) extract.
// Members are added only as the compiler demands them. Kept out of Decompiled/ so re-extraction stays clean. As an
// owner accrues many missing members it graduates to a pristine extract (extract.sh) and is removed from here.

public interface ITagDamageTakerOwner { }
public class EnviroRegionListener : MonoBehaviour { }
public class AreaEffectTint : MonoBehaviour { }
public class MatchXScaleSignOnEnable : MonoBehaviour { }
public class HeroLight : MonoBehaviour { }
public class FixedUpdateCache { }

// PlayerData's save-data containers (quests, collectables, journal, tools, story) — not needed for a moving Hornet.
public class CollectableItemsData { }
public class CollectableMementosData { }
public class CollectableRelicsData { }
public class CollectionGramaphone {
    public class PlayingInfo { }
}
public class EnemyJournalKillData { }
public class FloatingCrestSlotsData { }
public class MateriumItemsData { }
public class PlayerStory {
    public class EventInfo { }
}
public class QuestCompletionData {
    public class Completion { }
}
public class QuestRumourData { }
public class SaveSlotCompletionIcons {
    public class CompletionState { }
}
public class SteelSoulQuestSpot {
    public class Spot { }
}
public class ToolCrestsData {
    public class Data { }
}
public class ToolItemLiquidsData { }
public class WrappedVector2List { }
public struct HeroItemsState { }

public class ManagerSingleton<T> : MonoBehaviour where T : ManagerSingleton<T> {
    public static T Instance = null!;
    protected virtual void Awake() { }
    protected virtual void OnDestroy() { }
}

// HK has HazardRespawnMarker but not Silksong's nested FacingDirection enum; HeroController only uses that enum.
public class HazardRespawnMarker : MonoBehaviour {
    public enum FacingDirection { None, Left, Right }
}

// Silksong-only managers / regions / effects / utilities referenced by HeroController. Inert.
public class HeroChargeEffects : ManagerSingleton<HeroChargeEffects> { }
public class HeroCorpseMarker : MonoBehaviour { }
public class HeroCorpseMarkerProxy : MonoBehaviour { }
public class HeroDeathSequence : MonoBehaviour { }
public class HeroInvincibilitySource : MonoBehaviour { }
public class HeroPerformanceRegion : MonoBehaviour { }
public class FrostRegion : MonoBehaviour { }
public class NoClamberRegion : MonoBehaviour { }
public class NoWallClingRegion : MonoBehaviour { }
public class NailSlashTerrainThunk : MonoBehaviour { }
public class GenericMessageCanvas : MonoBehaviour { }
public class PlayVibration : MonoBehaviour { }
public class CollectableItemMemento { }
public class TimerGroup { }
public class SpriteFlashCallbackHooks { }
public class WaitForTk2dAnimatorClipFinish { }
public class CurrencyObjectBase : MonoBehaviour { }
public class CurrencyCounter : MonoBehaviour { }
public class InventoryPaneInput : MonoBehaviour { }
public class ToolItemLimiter : MonoBehaviour { }

public class GlobalSettingsBase<T> { }

// leaf types pulled in by Gameplay config (collectables / quests / pickups / shop)
public class CollectableItemMementoList { }
public class CollectableItemPickup : MonoBehaviour { }
public class CostReference { }
public class FullQuestBase { }
public class GenericPickup : MonoBehaviour { }
public class QuestBoardList { }
public class QuestTargetPlayerDataBools { }
public class ShopItemList { }
public class ThiefSnatchEffect : MonoBehaviour { }

public static class CollectableItemManager { }
public static class EnemyJournalManager { }
public static class QuestManager { }
public static class PersistentAudioManager { }
public static class HeroUtility { }
public static class SaveDataUtility { }
public static class TerrainThunkUtils { }
public static class EdgeAdjustHelper { }
public static class DemoHelper { }
public static class Effects { }
public static class Audio { }
public static class CustomPlayerLoop { }

// leaf types pulled in by the just-extracted tool/crest/anim/quest owners
public class CameraShakeTarget : MonoBehaviour { }
// DeliveryQuestItem (extracted) derives from CollectableItem and overrides these.
public class CollectableItem : MonoBehaviour {
    public virtual bool CanConsume => false;
    public virtual bool DisplayAmount => false;
    protected virtual bool CanShowQuestUpdatedForItem => false;
    public virtual void Consume(int amount, bool showCounter) { }
    public virtual string GetDisplayName(ReadSource readSource) => null!;
    public virtual string GetDescription(ReadSource readSource) => null!;
    public virtual Sprite GetIcon(ReadSource readSource) => null!;
    protected virtual void OnCollected() { }
    protected virtual void OnTaken() { }
}
public class ControlReminder {
    public class ConfigBase { }
    public class SingleConfig { }
    public class DoubleConfig { }
}

public enum ReadSource { Active, Inactive, Any }
public class DamageTagInfo { }
public interface IHeroAnimationController { }
public class InteractableBase : MonoBehaviour { }
public class InventoryItemComboButtonPromptDisplay : MonoBehaviour {
    public class Display { }
}
public class NailImbuementConfig { }
public class ParticleEffectsLerpEmission : MonoBehaviour { }
public class PlayerDataTest { }
public class SavedItem { }
// ToolItem/ToolCrest/DeliveryQuestItem (extracted, real) derive from ToolBase and override these. The real ToolBase
// chains into the quest/counter system; we stub it with just the virtuals the extracts override.
public class ToolBase : MonoBehaviour {
    public virtual bool IsEquipped => false;
    public virtual bool CanConsume => false;
    public virtual void Consume(int amount, bool showCounter) { }
    public virtual void SetHasNew(bool hasPopup) { }
    public virtual void Get(bool showPopup = true) { }
    public virtual bool CanGetMore() => false;
    public virtual int GetCompletionAmount(QuestCompletionData.Completion sourceCompletion) => 0;
    public virtual Sprite GetPopupIcon() => null!;
    public virtual string GetPopupName() => null!;
    public virtual int GetSavedAmount() => 0;
    public virtual bool DisplayAmount => false;
    protected virtual bool CanShowQuestUpdatedForItem => false;
    public virtual string GetDisplayName(ReadSource readSource) => null!;
    public virtual string GetDescription(ReadSource readSource) => null!;
    public virtual Sprite GetIcon(ReadSource readSource) => null!;
    protected virtual void OnCollected() { }
    protected virtual void OnTaken() { }
}
public class ToolCrestList { }
public class ToolItemList { }
public class ToolItemStatesLiquid { }
public class ObjectCache<T> { }
