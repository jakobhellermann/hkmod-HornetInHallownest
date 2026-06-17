using System;
using System.Linq;
using TeamCherry.Localization;
using UnityEngine;

namespace Silksong;

[CreateAssetMenu(fileName = "New Crest", menuName = "Hornet/Tool Crest")]
public class ToolCrest : ToolBase
{
	[Serializable]
	public struct SlotInfo
	{
		public Vector2 Position;

		public ToolItemType Type;

		[ModifiableProperty]
		[Conditional("IsAttackType", true, true, true)]
		public AttackToolBinding AttackBinding;

		[Space]
		public int NavUpIndex;

		public int NavDownIndex;

		public int NavLeftIndex;

		public int NavRightIndex;

		[Space]
		public int NavUpFallbackIndex;

		public int NavDownFallbackIndex;

		public int NavLeftFallbackIndex;

		public int NavRightFallbackIndex;

		[Space]
		public bool IsLocked;

		private bool IsAttackType()
		{
			return Type.IsAttackType();
		}
	}

	[SerializeField]
	private LocalisedString displayName;

	[SerializeField]
	private LocalisedString description;

	[Space]
	[SerializeField]
	private LocalisedString itemNamePrefix;

	[SerializeField]
	[LocalisedString.NotRequired]
	private LocalisedString getPromptDesc;

	[SerializeField]
	private LocalisedString equipText;

	[Space]
	[SerializeField]
	private Sprite crestSprite;

	[SerializeField]
	private Sprite crestSilhouette;

	[SerializeField]
	private Sprite crestGlow;

	[Space]
	[SerializeField]
	private bool isHidden;

	[SerializeField]
	private GameObject displayPrefab;

	[Space]
	[SerializeField]
	private SlotInfo[] slots;

	[Space]
	[SerializeField]
	private bool hasCustomAction;

	[SerializeField]
	private InventoryItemComboButtonPromptDisplay.Display customButtonCombo;

	[Space]
	[SerializeField]
	private HeroControllerConfig heroConfig;

	[Space]
	[SerializeField]
	private ToolCrest previousVersion;

	[NonSerialized]
	private ToolCrest oldPreviousVersion;

	[NonSerialized]
	private ToolCrest upgradedVersion;

	private bool cachedName;

	private string nameCache;

	public LocalisedString DisplayName => displayName;

	public LocalisedString Description => description;

	public LocalisedString ItemNamePrefix => itemNamePrefix;

	public LocalisedString GetPromptDesc => getPromptDesc;

	public LocalisedString EquipText => equipText;

	public Sprite CrestSprite => crestSprite;

	public Sprite CrestSilhouette => crestSilhouette;

	public Sprite CrestGlow => crestGlow;

	public bool IsHidden => isHidden;

	public GameObject DisplayPrefab => displayPrefab;

	public SlotInfo[] Slots => slots;

	public bool HasCustomAction => hasCustomAction;

	public InventoryItemComboButtonPromptDisplay.Display CustomButtonCombo => customButtonCombo;

	public HeroControllerConfig HeroConfig => heroConfig;

	public ToolCrestsData.Data SaveData
	{
		get
		{
			return PlayerData.instance.ToolEquips.GetData(name);
		}
		set
		{
			PlayerData.instance.ToolEquips.SetData(name, value);
		}
	}

	public bool IsUnlocked => SaveData.IsUnlocked;

	public bool IsUpgradedVersionUnlocked
	{
		get
		{
			if ((bool)upgradedVersion)
			{
				return upgradedVersion.IsUnlocked;
			}
			return false;
		}
	}

	public bool IsBaseVersion => !previousVersion;

	public bool IsVisible
	{
		get
		{
			if (IsUpgradedVersionUnlocked)
			{
				return false;
			}
			if (!IsUnlocked)
			{
				return false;
			}
			if (IsHidden)
			{
				return IsEquipped;
			}
			return true;
		}
	}

	public override bool IsEquipped => PlayerData.instance.CurrentCrestID == name;

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

	private void OnValidate()
	{
		if ((bool)oldPreviousVersion && oldPreviousVersion.upgradedVersion == this)
		{
			oldPreviousVersion.upgradedVersion = null;
		}
		if ((bool)previousVersion)
		{
			previousVersion.upgradedVersion = this;
		}
		oldPreviousVersion = previousVersion;
	}

	private void OnEnable()
	{
		OnValidate();
	}

	public void Unlock()
	{
		if (IsUnlocked)
		{
			return;
		}
		if ((bool)previousVersion)
		{
			if (!previousVersion.IsUnlocked)
			{
				previousVersion.Unlock();
			}
			SaveData = new ToolCrestsData.Data
			{
				IsUnlocked = true,
				Slots = previousVersion.SaveData.Slots?.ToList(),
				DisplayNewIndicator = true
			};
			if (PlayerData.instance.CurrentCrestID == previousVersion.name)
			{
				ToolItemManager.SetEquippedCrest(name);
			}
		}
		else
		{
			SaveData = new ToolCrestsData.Data
			{
				IsUnlocked = true,
				Slots = slots.Select((SlotInfo slotInfo, int _) => new ToolCrestsData.SlotData
				{
					IsUnlocked = !slotInfo.IsLocked
				}).ToList(),
				DisplayNewIndicator = true
			};
		}
		ToolItemManager.ReportCrestUnlocked(IsBaseVersion);
		InventoryPaneList.SetNextOpen("Tools");
	}

	public override void Get(bool showPopup = true)
	{
		Unlock();
	}

	public override bool CanGetMore()
	{
		return !IsUnlocked;
	}

	public override Sprite GetPopupIcon()
	{
		return CrestSprite;
	}
}
