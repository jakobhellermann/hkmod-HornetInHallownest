using System;
using GlobalSettings;
using TeamCherry.Localization;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

public abstract class ToolItem : ToolBase
{
	public enum ThrowAnimType
	{
		Default,
		Up,
		Down
	}

	public enum ReplenishResources
	{
		None = -1,
		Money,
		Shard
	}

	public enum ReplenishUsages
	{
		Percentage,
		OneForOne,
		Custom
	}

	[Flags]
	public enum PopupFlags
	{
		None = 0,
		ItemGet = 1,
		Tutorial = 2,
		Default = 3
	}

	public enum IconVariants
	{
		Default,
		Poison
	}

	[Serializable]
	public struct UsageOptions
	{
		public GameObject ThrowPrefab;

		public bool UseAltForQuickSling;

		public float ThrowCooldown;

		public ThrowAnimType ThrowAnim;

		public Vector2 ThrowVelocity;

		public Vector2 ThrowVelocityAlt;

		public Vector2 ThrowOffset;

		public Vector2 ThrowOffsetAlt;

		public bool ScaleToHero;

		public bool FlipScale;

		public bool SetDamageDirection;

		public string FsmEventName;

		public bool IsNonBlockingEvent;

		public int SilkRequired;

		public int MaxActive;

		public int MaxActiveAlt;

		public int ThrowAnimVerticalDirection
		{
			get
			{
				if (ThrowAnim != ThrowAnimType.Up)
				{
					return 0;
				}
				return 1;
			}
		}
	}

	[SerializeField]
	[Tooltip("Is this counted in the total tools achievement count?")]
	private bool isCounted = true;

	[SerializeField]
	[Tooltip("Tools sharing a key will be counted as 1 in the achievement count.")]
	private SavedItem countKey;

	[SerializeField]
	private ToolItem getReplaces;

	[Space]
	[SerializeField]
	private ToolItemType type;

	[SerializeField]
	private PlayerDataTest alternateUnlockedTest;

	[SerializeField]
	private bool preventTutorialMsg;

	[Space]
	[SerializeField]
	[ModifiableProperty]
	[Conditional("HasLimitedUses", true, true, false)]
	private int baseStorageAmount;

	[SerializeField]
	private int unlockStartAmount = -1;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("HasLimitedUses", true, true, false)]
	private bool preventStorageIncrease;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsAutoReplenished", true, true, false)]
	private ReplenishResources replenishResource = ReplenishResources.Shard;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsAutoReplenished", true, true, false)]
	private ReplenishUsages replenishUsage;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsCustomReplenish", false, true, false)]
	private float replenishUsageMultiplier = 1f;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("HasLimitedUses", true, true, false)]
	private bool isCustomUsage;

	[Space]
	[SerializeField]
	private AudioClip reloadAudioLoop;

	[SerializeField]
	private AudioEvent reloadEndAudio;

	[Space]
	[SerializeField]
	private LocalisedString togglePromptText;

	[Space]
	[SerializeField]
	[EnumPickerBitmask]
	private ToolDamageFlags damageFlags;

	[SerializeField]
	private int poisonDamageTicks;

	[SerializeField]
	private bool usePoisonTintRecolour;

	[SerializeField]
	[Range(-1f, 1f)]
	private float poisonHueShift;

	[SerializeField]
	private int zapDamageTicks;

	[Space]
	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsAttackType", false, true, false)]
	private bool hasCustomAction;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsAttackType", false, true, false)]
	private InventoryItemComboButtonPromptDisplay.Display customButtonCombo;

	[SerializeField]
	private bool showPromptHold;

	[Space]
	[SerializeField]
	[LocalisedString.NotRequired]
	private LocalisedString refillMsg;

	[Space]
	[SerializeField]
	private GameObject extraDescriptionSection;

	private ToolItemManager.ToolStatus status;

	private bool cachedName;

	private string nameCache;

	public ToolItemType Type => type;

	public int BaseStorageAmount
	{
		get
		{
			if (!HasLimitedUses())
			{
				return 0;
			}
			return baseStorageAmount;
		}
	}

	public bool PreventStorageIncrease => preventStorageIncrease;

	public ReplenishResources ReplenishResource => replenishResource;

	public ReplenishUsages ReplenishUsage => replenishUsage;

	public float ReplenishUsageMultiplier => replenishUsageMultiplier;

	public bool IsCustomUsage => isCustomUsage;

	public ToolDamageFlags DamageFlags => damageFlags;

	public int PoisonDamageTicks => poisonDamageTicks;

	public bool UsePoisonTintRecolour => usePoisonTintRecolour;

	public float PoisonHueShift => poisonHueShift;

	public int ZapDamageTicks => zapDamageTicks;

	public bool ShowPromptHold => showPromptHold;

	public GameObject ExtraDescriptionSection => extraDescriptionSection;

	public abstract UsageOptions Usage { get; }

	public virtual bool UsableWhenEmpty => false;

	public virtual bool UsableWhenEmptyPrevented => false;

	public virtual bool HideUsePrompt => false;

	public abstract LocalisedString DisplayName { get; }

	public abstract LocalisedString Description { get; }

	private bool IsUnlockedTest
	{
		get
		{
			if (alternateUnlockedTest.IsDefined)
			{
				return alternateUnlockedTest.IsFulfilled;
			}
			return false;
		}
	}

	public bool IsUnlocked
	{
		get
		{
			if (!SavedData.IsUnlocked)
			{
				return IsUnlockedTest;
			}
			return true;
		}
	}

	public bool IsUnlockedNotHidden
	{
		get
		{
			if (!SavedData.IsHidden)
			{
				return IsUnlocked;
			}
			return false;
		}
	}

	public bool HasBeenSeen
	{
		get
		{
			return SavedData.HasBeenSeen;
		}
		set
		{
			ToolItemsData.Data savedData = SavedData;
			savedData.HasBeenSeen = value;
			SavedData = savedData;
		}
	}

	public bool HasBeenSelected
	{
		get
		{
			return SavedData.HasBeenSelected;
		}
		set
		{
			ToolItemsData.Data savedData = SavedData;
			savedData.HasBeenSelected = value;
			SavedData = savedData;
		}
	}

	public override bool IsEquipped => ToolItemManager.IsToolEquipped(this, ToolEquippedReadSource.Active);

	public bool IsEquippedHud => ToolItemManager.IsToolEquipped(this, ToolEquippedReadSource.Hud);

	public ToolItemManager.ToolStatus Status
	{
		get
		{
			if (status == null)
			{
				status = new ToolItemManager.ToolStatus(this);
			}
			return status;
		}
	}

	public new string name
	{
		get
		{
			if (!cachedName)
			{
				nameCache = base.name;
				cachedName = true;
			}
			return nameCache;
		}
		set
		{
			nameCache = value;
			base.name = value;
		}
	}

	public bool IsEmpty
	{
		get
		{
			if (HasLimitedUses() && SavedData.AmountLeft <= 0)
			{
				return baseStorageAmount > 0;
			}
			return false;
		}
	}

	public ToolItemsData.Data SavedData
	{
		get
		{
			return PlayerData.instance.GetToolData(name);
		}
		set
		{
			ToolItemsData.Data data = value;
			if (!data.IsUnlocked && IsUnlockedTest)
			{
				data.IsUnlocked = true;
			}
			PlayerData.instance.SetToolData(name, data);
		}
	}

	public bool DisplayAmountText
	{
		get
		{
			int num = ((type != ToolItemType.Red) ? 1 : 0);
			return BaseStorageAmount > num;
		}
	}

	public virtual bool DisplayTogglePrompt => false;

	public string CustomToggleText => togglePromptText;

	public bool HasCustomAction
	{
		get
		{
			if (hasCustomAction)
			{
				return !IsAttackType();
			}
			return false;
		}
	}

	public InventoryItemComboButtonPromptDisplay.Display CustomButtonCombo => customButtonCombo;

	public bool IsCounted => isCounted;

	public SavedItem CountKey
	{
		get
		{
			if (!countKey)
			{
				return this;
			}
			return countKey;
		}
	}

	public Sprite InventorySpriteBase => GetInventorySprite(IconVariants.Default);

	public Sprite InventorySpriteModified => GetInventorySprite((PoisonDamageTicks > 0 && Gameplay.PoisonPouchTool.IsEquippedHud) ? IconVariants.Poison : IconVariants.Default);

	public Sprite HudSpriteBase => GetHudSprite(IconVariants.Default);

	public Sprite HudSpriteModified => GetHudSprite((PoisonDamageTicks > 0 && Gameplay.PoisonPouchTool.IsEquippedHud) ? IconVariants.Poison : IconVariants.Default);

	public override bool CanConsume => true;

	public virtual bool CanToggle => false;

	public abstract Sprite GetInventorySprite(IconVariants iconVariant);

	public abstract Sprite GetHudSprite(IconVariants iconVariant);

	private bool IsAttackType()
	{
		return type.IsAttackType();
	}

	private bool HasLimitedUses()
	{
		return type != ToolItemType.Skill;
	}

	public bool IsAutoReplenished()
	{
		if (HasLimitedUses())
		{
			return BaseStorageAmount > 0;
		}
		return false;
	}

	public bool IsCustomReplenish()
	{
		return replenishUsage == ReplenishUsages.Custom;
	}

	public virtual bool TryReplenishSingle(bool doReplenish, float inCost, out float outCost, out int reserveCost)
	{
		outCost = inCost;
		reserveCost = 0;
		return true;
	}

	public virtual void OnWasUsed(bool wasEmpty)
	{
	}

	public void SetUnlockedTestsComplete()
	{
		if (alternateUnlockedTest == null)
		{
			return;
		}
		PlayerData instance = PlayerData.instance;
		PlayerDataTest.TestGroup[] testGroups = alternateUnlockedTest.TestGroups;
		for (int i = 0; i < testGroups.Length; i++)
		{
			PlayerDataTest.Test[] tests = testGroups[i].Tests;
			for (int j = 0; j < tests.Length; j++)
			{
				PlayerDataTest.Test test = tests[j];
				if (test.Type == PlayerDataTest.TestType.Bool)
				{
					instance.SetVariable(test.FieldName, test.BoolValue);
				}
			}
		}
	}

	public void Unlock(Action afterTutorialMsg = null, PopupFlags popupFlags = PopupFlags.Default)
	{
		OnUnlocked();
		bool flag = (popupFlags & PopupFlags.ItemGet) != 0;
		bool flag2 = (popupFlags & PopupFlags.Tutorial) != 0;
		PlayerData instance = PlayerData.instance;
		bool flag3;
		if (IsUnlocked)
		{
			ToolItemsData.Data savedData = SavedData;
			savedData.AmountLeft = ToolItemManager.GetToolStorageAmount(this);
			flag3 = savedData.IsHidden;
			savedData.IsHidden = false;
			SavedData = savedData;
			AttackToolBinding? attackToolBinding = ToolItemManager.GetAttackToolBinding(this);
			if (attackToolBinding.HasValue)
			{
				ToolItemManager.ReportBoundAttackToolUpdated(attackToolBinding.Value);
			}
			if (flag)
			{
				ShowRefillMsg();
			}
		}
		else
		{
			SavedData = new ToolItemsData.Data
			{
				IsUnlocked = true,
				AmountLeft = ((unlockStartAmount >= 0) ? unlockStartAmount : ToolItemManager.GetToolStorageAmount(this))
			};
			bool flag4 = afterTutorialMsg == null;
			afterTutorialMsg = (Action)Delegate.Combine(afterTutorialMsg, (Action)delegate
			{
				ToolItemManager.ReportToolUnlocked(type);
			});
			if (flag2 && !preventTutorialMsg && (!instance.SeenToolGetPrompt || (type == ToolItemType.Red && !instance.SeenToolWeaponGetPrompt)))
			{
				if (flag4)
				{
					afterTutorialMsg = (Action)Delegate.Combine(afterTutorialMsg, (Action)delegate
					{
						GameCameras.instance.HUDIn();
					});
				}
				ToolTutorialMsg.Spawn(this, afterTutorialMsg);
				instance.SeenToolGetPrompt = true;
				if (type == ToolItemType.Red)
				{
					instance.SeenToolWeaponGetPrompt = true;
				}
				afterTutorialMsg = null;
			}
			flag3 = true;
			if ((bool)getReplaces)
			{
				ToolItemManager.ReplaceToolEquips(getReplaces, this);
				getReplaces.Lock();
			}
		}
		SetHasNew(flag3);
		if (flag3 && flag)
		{
			CollectableUIMsg itemUiMsg = CollectableUIMsg.Spawn(this);
			CollectableItemHeroReaction.DoReaction();
			QuestManager.MaybeShowQuestUpdated(this, itemUiMsg);
		}
		afterTutorialMsg?.Invoke();
	}

	public override void Consume(int amount, bool showCounter)
	{
		if (amount > 0)
		{
			Lock();
		}
	}

	public void Lock()
	{
		ToolItemsData.Data savedData = SavedData;
		savedData.IsHidden = true;
		SavedData = savedData;
		ToolItemManager.RemoveToolFromAllCrests(this);
	}

	protected void ShowRefillMsg()
	{
		if (!refillMsg.IsEmpty)
		{
			CollectableUIMsg.Spawn(new UIMsgDisplay
			{
				Name = refillMsg,
				Icon = GetUIMsgSprite(),
				IconScale = GetUIMsgIconScale(),
				RepresentingObject = this
			});
		}
	}

	protected virtual Sprite GetFullIcon()
	{
		return GetUIMsgSprite();
	}

	public void ShowRefillMsgFull()
	{
		CollectableUIMsg.Spawn(new UIMsgDisplay
		{
			Name = refillMsg,
			Icon = GetFullIcon(),
			IconScale = GetUIMsgIconScale(),
			RepresentingObject = this
		});
	}

	public override void SetHasNew(bool hasPopup)
	{
		if (hasPopup)
		{
			ToolItemManager.UnlockedTool = this;
			InventoryPaneList.SetNextOpen("Tools");
		}
		PlayerData.instance.ToolPaneHasNew = true;
	}

	protected virtual void OnUnlocked()
	{
	}

	public override void Get(bool showPopup = true)
	{
		Unlock(null, showPopup ? PopupFlags.Default : PopupFlags.None);
	}

	public override bool CanGetMore()
	{
		return !IsUnlocked;
	}

	public override int GetCompletionAmount(QuestCompletionData.Completion sourceCompletion)
	{
		if (!IsUnlocked)
		{
			return 0;
		}
		return 1;
	}

	public void CollectFree(int amount)
	{
		if (!IsUnlocked)
		{
			Debug.LogError("Trying to replenish a tool that is not unlocked!", this);
			return;
		}
		ToolItemsData.Data savedData = SavedData;
		int toolStorageAmount = ToolItemManager.GetToolStorageAmount(this);
		int num = savedData.AmountLeft + amount;
		if (num > toolStorageAmount)
		{
			num = toolStorageAmount;
		}
		if (num != savedData.AmountLeft)
		{
			savedData.AmountLeft = num;
			SavedData = savedData;
			AttackToolBinding? attackToolBinding = ToolItemManager.GetAttackToolBinding(this);
			if (attackToolBinding.HasValue)
			{
				ToolItemManager.ReportBoundAttackToolUpdated(attackToolBinding.Value);
			}
		}
	}

	public void CustomUsage(int amount)
	{
		ToolItemsData.Data savedData = SavedData;
		if (savedData.AmountLeft > 0)
		{
			savedData.AmountLeft -= amount;
			if (savedData.AmountLeft < 0)
			{
				savedData.AmountLeft = 0;
			}
			SavedData = savedData;
			AttackToolBinding? attackToolBinding = ToolItemManager.GetAttackToolBinding(this);
			if (attackToolBinding.HasValue)
			{
				ToolItemManager.ReportBoundAttackToolUsed(attackToolBinding.Value);
				ToolItemManager.ReportBoundAttackToolUpdated(attackToolBinding.Value);
				ToolItemLimiter.ReportToolUsed(this);
			}
		}
	}

	public bool CanReload()
	{
		if (SavedData.AmountLeft >= ToolItemManager.GetToolStorageAmount(this))
		{
			return false;
		}
		if (ReplenishResource == ReplenishResources.None)
		{
			return true;
		}
		if (CurrencyManager.GetCurrencyAmount((CurrencyType)ReplenishResource) < 1)
		{
			return false;
		}
		return true;
	}

	public void ReloadSingle()
	{
		ToolItemsData.Data savedData = SavedData;
		savedData.AmountLeft++;
		int toolStorageAmount = ToolItemManager.GetToolStorageAmount(this);
		if (savedData.AmountLeft > toolStorageAmount)
		{
			savedData.AmountLeft = toolStorageAmount;
		}
		SavedData = savedData;
		if (replenishResource != ReplenishResources.None)
		{
			CurrencyManager.TakeCurrency(1, (CurrencyType)ReplenishResource);
		}
	}

	public void StartedReloading(AudioSource audioSource)
	{
		audioSource.clip = reloadAudioLoop;
		audioSource.loop = true;
		audioSource.Play();
	}

	public void StoppedReloading(AudioSource audioSource, bool didFinish)
	{
		audioSource.Stop();
		audioSource.loop = false;
		if (didFinish)
		{
			reloadEndAudio.PlayOnSource(audioSource);
		}
	}

	public override Sprite GetPopupIcon()
	{
		return GetInventorySprite(IconVariants.Default);
	}

	public override string GetPopupName()
	{
		return DisplayName;
	}

	public override int GetSavedAmount()
	{
		if (!IsUnlocked)
		{
			return 0;
		}
		return 1;
	}

	public virtual bool DoToggle(out bool didChangeVisually)
	{
		didChangeVisually = false;
		return false;
	}

	public virtual void PlayToggleAudio(AudioSource audioSource)
	{
	}
}
