using UnityEngine;

// Global namespace on purpose: these are all Silksong-only types (verified absent from HK), referenced from both the
// `namespace Silksong`-wrapped extracts AND the pristine `namespace GlobalSettings`/`GlobalEnums` extracts. Global is
// in scope for all of them; none collide with HK. (HK-colliding shims like CheatManager live in `namespace Silksong`.)
//
// Stubs for HeroController's combat / tools / quest / environment dependencies. These cascade into half the game
// (ToolItemManager, quests, collectables, Addressables…) if extracted, and none are needed for a playable,
// locomotion-focused Hornet — so they're stubbed here instead. Members are added only as HeroController's code
// demands them (compiler-driven); stubbed methods are intentionally inert. Kept out of Decompiled/ so re-extraction
// stays clean.
//
// As the locomotion port matures, the goal is to stop *referencing* these from the movement path entirely; until
// then they exist just to satisfy the compiler.

public class ToolItem : MonoBehaviour { }

public class ToolItemsData {
    public class Data { }
}

public class DeliveryQuestItem : MonoBehaviour {
    public class ActiveItem { }
}

public class DamageTag {
    public class DamageTagInstance { }
}

public class TagDamageTaker : MonoBehaviour { }

public interface ITagDamageTakerOwner { }

public class NoiseMaker : MonoBehaviour { }

public class EnviroRegionListener : MonoBehaviour { }

public class AreaEffectTint : MonoBehaviour { }

public class MatchXScaleSignOnEnable : MonoBehaviour { }

public class HeroLight : MonoBehaviour { }

public class HeroNailImbuement : MonoBehaviour { }

public class NailAttackBase : MonoBehaviour {
    protected virtual void Awake() { }
    protected virtual void OnAttackCancelled() { }
    public virtual void QueueBounce() { }
}

public class HeroSlashBounceConfig { }

public class SilkChunk : MonoBehaviour { }

public class FixedUpdateCache { }

// PlayerData's save-data containers (quests, collectables, journal, tools, story). None are needed for a moving
// Hornet; stubbed empty. Members get added only if PlayerData's own code touches them (compiler-driven).
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
public class QuestCompletionData { }
public class QuestRumourData { }
public class SaveSlotCompletionIcons {
    public class CompletionState { }
}
public class SteelSoulQuestSpot {
    public class Spot { }
}
public class ToolCrestsData { }
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

// ── Silksong-only managers / regions / effects / utilities referenced by HeroController. Stubbed (no HK equivalent,
//    so no shadow risk). Members are added below only as the compiler demands them; behaviour is inert for now. ──
public class HeroChargeEffects : ManagerSingleton<HeroChargeEffects> { }
public class HeroCorpseMarker : MonoBehaviour { }
public class HeroCorpseMarkerProxy : MonoBehaviour { }
public class HeroDeathSequence : MonoBehaviour { }
public class HeroInvincibilitySource : MonoBehaviour { }
public class HeroPerformanceRegion : MonoBehaviour { }
public class FrostRegion : MonoBehaviour { }
public class NoClamberRegion : MonoBehaviour { }
public class NoWallClingRegion : MonoBehaviour { }
public class SlideSurface : MonoBehaviour { }
public class NailSlashTerrainThunk : MonoBehaviour { }
public class GenericMessageCanvas : MonoBehaviour { }
public class StatusVignette : MonoBehaviour { }
public class PlayVibration : MonoBehaviour { }
public class CollectableItemMemento { }
public class ToolCrest { }
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

public static class ToolItemManager { }
public static class CurrencyManager { }
public static class InteractManager { }
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
