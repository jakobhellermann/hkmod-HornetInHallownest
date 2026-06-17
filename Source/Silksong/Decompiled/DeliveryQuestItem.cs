using System;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using GlobalSettings;
using TeamCherry.Localization;
using UnityEngine;

namespace Silksong;

[CreateAssetMenu(menuName = "Hornet/Collectable Items/Collectable Item (Delivery Quest)")]
public class DeliveryQuestItem : CollectableItem
{
	public struct ActiveItem
	{
		public DeliveryQuestItem Item;

		public FullQuestBase Quest;

		public int CurrentCount;

		public int MaxCount;
	}

	[Space]
	[SerializeField]
	private LocalisedString displayName;

	[SerializeField]
	private LocalisedString description;

	[SerializeField]
	private Sprite icon;

	[SerializeField]
	private Sprite tinyIcon;

	[SerializeField]
	private Color barColour = Color.white;

	[SerializeField]
	private Sprite breakIcon;

	[Space]
	[SerializeField]
	private GameObject hitHeroEffect;

	[SerializeField]
	private GameObject hitUIEffect;

	[SerializeField]
	private GameObject heroLoopEffect;

	[SerializeField]
	private GameObject uiLoopEffect;

	[SerializeField]
	private GameObject breakHeroEffect;

	[SerializeField]
	private GameObject breakUIEffect;

	[Space]
	[SerializeField]
	private float totalTimer;

	private static ObjectCache<IEnumerable<ActiveItem>> activeItemsCache = new ObjectCache<IEnumerable<ActiveItem>>();

	public GameObject HitUIEffect => hitUIEffect;

	public GameObject HeroLoopEffect => heroLoopEffect;

	public GameObject UILoopEffect => uiLoopEffect;

	public override bool DisplayAmount => false;

	public Color BarColour => barColour;

	public GameObject BreakUIEffect => breakUIEffect;

	protected override bool CanShowQuestUpdatedForItem => false;

	public override bool CanConsume => true;

	public override string GetDisplayName(ReadSource readSource)
	{
		return displayName;
	}

	public override string GetDescription(ReadSource readSource)
	{
		return description;
	}

	public override Sprite GetIcon(ReadSource readSource)
	{
		switch (readSource)
		{
		case ReadSource.Inventory:
		case ReadSource.GetPopup:
			return icon;
		case ReadSource.Tiny:
			return tinyIcon;
		default:
			throw new ArgumentOutOfRangeException("readSource", readSource, null);
		}
	}

	protected override void OnCollected()
	{
		HeroController.instance.SetupDeliveryItems();
		EventRegister.SendEvent(EventRegisterEvents.DeliveryHudRefresh);
	}

	protected override void OnTaken()
	{
		HeroController instance = HeroController.instance;
		instance.RemoveDeliveryItemEffect(this);
		instance.SetupDeliveryItems();
		EventRegister.SendEvent(EventRegisterEvents.DeliveryHudRefresh);
	}

	public override void Consume(int amount, bool showCounter)
	{
		base.Consume(amount, showCounter);
		ClearGenericQuests();
	}

	public static void ClearGenericQuests()
	{
		PlayerData.instance.BelltownCouriersGenericQuests = null;
	}

	public static bool CanTakeHit()
	{
		if (ToolItemManager.ActiveState == ToolsActiveStates.Disabled)
		{
			return false;
		}
		return GameManager.instance.GetCurrentMapZoneEnum() != MapZone.MEMORY;
	}

	public static IEnumerable<ActiveItem> GetActiveItems()
	{
		int version = QuestManager.Version + CollectableItemManager.Version;
		if (activeItemsCache.ShouldUpdate(version))
		{
			IEnumerable<ActiveItem> first = from a in QuestManager.GetActiveQuests().Select(delegate(FullQuestBase q)
				{
					(FullQuestBase.QuestTarget, int) tuple = q.TargetsAndCounters.FirstOrDefault();
					return new ActiveItem
					{
						Item = (tuple.Item1.Counter as DeliveryQuestItem),
						Quest = q,
						CurrentCount = tuple.Item2,
						MaxCount = tuple.Item1.Count
					};
				})
				where a.Item
				select a;
			IEnumerable<ActiveItem> second = from item in CollectableItemManager.GetCollectedItems().OfType<DeliveryQuestItemStandalone>()
				select new ActiveItem
				{
					Item = item,
					CurrentCount = item.CollectedAmount,
					MaxCount = item.TargetCount
				};
			activeItemsCache.UpdateCache(first.Union(second).ToArray(), version);
		}
		return activeItemsCache.Value;
	}

	public static void TakeHit()
	{
		TakeHit(1);
	}

	public static void TakeHit(int hits)
	{
		foreach (ActiveItem activeItem in GetActiveItems())
		{
			TakeHitForItem(activeItem, hitEffect: true, hits);
		}
	}

	public static void TakeHitForItem(ActiveItem item, bool hitEffect)
	{
		TakeHitForItem(item, hitEffect, 1);
	}

	private static void CancelQuest(ActiveItem item, PlayerData pd)
	{
		if (item.Quest.WasEverCompleted)
		{
			item.Quest.SilentlyComplete();
		}
		else
		{
			pd.QuestCompletionData.SetData(item.Quest.name, default(QuestCompletionData.Completion));
		}
		QuestManager.IncrementVersion();
	}

	public static void TakeHitForItem(ActiveItem item, bool hitEffect, int amount)
	{
		if (amount <= 0 || !CanTakeHit())
		{
			return;
		}
		Vector2 heroPos = HeroController.instance.transform.position;
		PlayerData instance = PlayerData.instance;
		if (hitEffect)
		{
			EventRegister.SendEvent(EventRegisterEvents.DeliveryHudHit);
		}
		if (item.CurrentCount <= amount)
		{
			if ((bool)item.Quest)
			{
				CancelQuest(item, instance);
			}
			item.Item.BreakEffect(heroPos);
			EventRegister.SendEvent(EventRegisterEvents.DeliveryHudBreak);
			instance.BelltownCouriersBrokenDlgQueued = true;
			UI.DestroyedPopup.ToString().TryFormat(out var outText, item.Item.GetDisplayName(ReadSource.TakePopup));
			CollectableUIMsg.Spawn(new UIMsgDisplay
			{
				Name = outText,
				Icon = (item.Item.breakIcon ? item.Item.breakIcon : item.Item.icon),
				IconScale = item.Item.GetUIMsgIconScale(),
				RepresentingObject = item.Item
			});
		}
		else if (hitEffect)
		{
			item.Item.HitEffect(heroPos);
		}
		item.Item.Take(amount, showCounter: false);
	}

	public static void BreakAll()
	{
		BreakAllInternal(withEffects: true, onlyTimed: false);
	}

	public static void BreakAllNoEffects()
	{
		BreakAllInternal(withEffects: false, onlyTimed: false);
	}

	public static void BreakTimedNoEffects()
	{
		BreakAllInternal(withEffects: false, onlyTimed: true);
	}

	private static void BreakAllInternal(bool withEffects, bool onlyTimed)
	{
		Vector2 heroPos = (withEffects ? ((Vector2)HeroController.instance.transform.position) : Vector2.zero);
		PlayerData instance = PlayerData.instance;
		foreach (ActiveItem activeItem in GetActiveItems())
		{
			if (!onlyTimed || !(activeItem.Item.totalTimer <= 0f))
			{
				if ((bool)activeItem.Quest)
				{
					CancelQuest(activeItem, instance);
				}
				if (withEffects)
				{
					EventRegister.SendEvent(EventRegisterEvents.DeliveryHudBreak);
				}
				activeItem.Item.Take(activeItem.Item.CollectedAmount, showCounter: false);
				if (withEffects)
				{
					activeItem.Item.BreakEffect(heroPos);
				}
				instance.BelltownCouriersBrokenDlgQueued = true;
			}
		}
	}

	private void HitEffect(Vector2 heroPos)
	{
		if ((bool)hitHeroEffect)
		{
			Vector3 position = heroPos.ToVector3(hitHeroEffect.transform.localPosition.z);
			hitHeroEffect.Spawn(position);
		}
	}

	private void BreakEffect(Vector2 heroPos)
	{
		if ((bool)breakHeroEffect)
		{
			Vector3 position = heroPos.ToVector3(breakHeroEffect.transform.localPosition.z);
			breakHeroEffect.Spawn(position);
		}
	}

	public float GetChunkDuration(int maxCount)
	{
		if (totalTimer <= 0f)
		{
			return 0f;
		}
		return totalTimer / (float)maxCount;
	}
}
