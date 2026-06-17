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
