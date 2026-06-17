using UnityEngine;

namespace Silksong;

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
    protected virtual void Awake() { }
    protected virtual void OnDestroy() { }
}

// HK has HazardRespawnMarker but not Silksong's nested FacingDirection enum; HeroController only uses that enum.
public class HazardRespawnMarker : MonoBehaviour {
    public enum FacingDirection { None, Left, Right }
}
