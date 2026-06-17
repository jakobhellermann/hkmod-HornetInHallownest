using System;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using GlobalSettings;
using JetBrains.Annotations;
using TeamCherry.Localization;
using UnityEngine;

namespace Silksong;

public class ToolItemManager : ManagerSingleton<ToolItemManager>
{
	[Flags]
	public enum OwnToolsCheckFlags
	{
		None = 0,
		Red = 1,
		Blue = 2,
		Yellow = 4,
		Skill = 8,
		AllTools = 7,
		All = -1
	}

	public enum ReplenishMethod
	{
		Bench,
		QuickCraft,
		BenchSilent
	}

	public sealed class ToolStatus
	{
		private readonly ToolItem tool;

		private int version;

		private bool isEquipped;

		private int cutsceneVersion;

		private bool cutsceneEquipped;

		public bool IsEquipped
		{
			get
			{
				if (version != Version)
				{
					version = Version;
					if ((bool)tool)
					{
						isEquipped = IsToolEquipped(tool, ToolEquippedReadSource.Active);
					}
					else
					{
						isEquipped = false;
					}
				}
				return isEquipped;
			}
		}

		public bool IsEquippedCutscene
		{
			get
			{
				if (cutsceneVersion != Version)
				{
					cutsceneVersion = Version;
					if ((bool)tool)
					{
						cutsceneEquipped = IsToolEquipped(tool, ToolEquippedReadSource.Hud);
					}
					else
					{
						cutsceneEquipped = false;
					}
				}
				return cutsceneEquipped;
			}
		}

		public bool IsEquippedAny
		{
			get
			{
				if (!IsEquipped)
				{
					return IsEquippedCutscene;
				}
				return true;
			}
		}

		public ToolStatus(ToolItem tool)
		{
			this.tool = tool;
		}
	}

	public const string EQUIPS_CHANGED_EVENT = "TOOL EQUIPS CHANGED";

	public const string EQUIPS_CHANGED_EVENT_POST = "POST TOOL EQUIPS CHANGED";

	private const float COST_ROUND_UP = 0.1f;

	[SerializeField]
	private ToolItemList toolItems;

	[SerializeField]
	private ToolCrestList crestList;

	[Space]
	[SerializeField]
	private ToolCrest cursedCrest;

	private ToolItem[] boundAttackTools;

	private bool queueAttackToolsChanged;

	private bool queueEquipsChanged;

	private string previousEquippedCrest;

	private ToolItem customToolOverride;

	private int customToolAmount;

	private ControlReminder.SingleConfig toolOverrideReminder;

	private ToolItem lastEquippedAttackTool;

	private ControlReminder.SingleConfig equipChangedToolSingleReminder;

	private ControlReminder.DoubleConfig equipChangedToolModifierReminder;

	private ControlReminder.ConfigBase queuedReminder;

	private ToolItem unlockedTool;

	private static ToolsActiveStates _activeState;

	private static float[] _startingCurrencyAmounts;

	private static float[] _endingCurrencyAmounts;

	private static Dictionary<ToolItemStatesLiquid, int> _liquidCostsTemp;

	private static bool _sIsInfiniteToolUseEnabled;

	private static readonly Dictionary<ToolItem, bool> toolCache = new Dictionary<ToolItem, bool>();

	public static bool IsCustomToolOverride
	{
		get
		{
			if ((bool)ManagerSingleton<ToolItemManager>.Instance)
			{
				return ManagerSingleton<ToolItemManager>.Instance.customToolOverride;
			}
			return false;
		}
	}

	public static ToolsActiveStates ActiveState => _activeState;

	public static bool IsChangingActiveState { get; private set; }

	public static bool IsInCutscene => ActiveState == ToolsActiveStates.Cutscene;

	public static ToolItem UnlockedTool
	{
		get
		{
			if (!ManagerSingleton<ToolItemManager>.Instance)
			{
				return null;
			}
			return ManagerSingleton<ToolItemManager>.Instance.unlockedTool;
		}
		set
		{
			if ((bool)ManagerSingleton<ToolItemManager>.Instance)
			{
				ManagerSingleton<ToolItemManager>.Instance.unlockedTool = value;
			}
		}
	}

	public static bool IsCursed { get; private set; }

	public static bool IsInfiniteToolUseEnabled
	{
		get
		{
			return _sIsInfiniteToolUseEnabled;
		}
		set
		{
			_sIsInfiniteToolUseEnabled = value;
			if (!_sIsInfiniteToolUseEnabled || !PlayerData.HasInstance)
			{
				return;
			}
			ToolItemsData tools = PlayerData.instance.Tools;
			foreach (ToolItem unlockedTool in GetUnlockedTools())
			{
				ToolItemsData.Data data = tools.GetData(unlockedTool.name);
				data.AmountLeft = GetToolStorageAmount(unlockedTool);
				tools.SetData(unlockedTool.name, data);
				AttackToolBinding? attackToolBinding = GetAttackToolBinding(unlockedTool);
				if (attackToolBinding.HasValue)
				{
					ReportBoundAttackToolUpdated(attackToolBinding.Value);
				}
			}
		}
	}

	public static int Version { get; private set; }

	public static event Action<AttackToolBinding> BoundAttackToolUpdated;

	public static event Action<AttackToolBinding> BoundAttackToolUsed;

	public static event Action<AttackToolBinding> BoundAttackToolFailed;

	public static event Action OnEquippedStateChanged;

	public static void IncrementVersion()
	{
		Version++;
	}

	protected override void Awake()
	{
		base.Awake();
		GameManager.instance.NextSceneWillActivate += OnDestroyPersonalPools;
		GameManager.instance.SceneInit += SceneInit;
		if (ManagerSingleton<ToolItemManager>.Instance == this)
		{
			_activeState = ToolsActiveStates.Active;
		}
		IncrementVersion();
		if (!(cursedCrest == null) || !(crestList != null))
		{
			return;
		}
		foreach (ToolCrest crest in crestList)
		{
			if (crest.name == "Cursed")
			{
				cursedCrest = crest;
				break;
			}
		}
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		if ((bool)GameManager.UnsafeInstance)
		{
			GameManager.UnsafeInstance.NextSceneWillActivate -= OnDestroyPersonalPools;
			GameManager.UnsafeInstance.SceneInit -= SceneInit;
		}
		ClearToolCache();
	}

	private void SceneInit()
	{
		if (toolOverrideReminder == null)
		{
			toolOverrideReminder = new ControlReminder.SingleConfig
			{
				AppearEvent = "SHOW CUSTOM TOOL REMINDER",
				DisappearEvent = "HIDE CUSTOM TOOL REMINDER",
				FadeInDelay = 8f,
				FadeInTime = 1f,
				FadeOutTime = 0.5f,
				DisappearOnButtonPress = true,
				Button = HeroActionButton.QUICK_CAST
			};
		}
		ControlReminder.AddReminder(toolOverrideReminder);
		LocalisedString text = new LocalisedString("Prompts", "THROW_TOOL_GENERIC");
		if (equipChangedToolSingleReminder == null)
		{
			equipChangedToolSingleReminder = new ControlReminder.SingleConfig
			{
				Text = text,
				FadeInDelay = 0.5f,
				FadeInTime = 1f,
				FadeOutTime = 0.5f,
				DisappearOnButtonPress = true,
				Button = HeroActionButton.QUICK_CAST
			};
		}
		ControlReminder.AddReminder(equipChangedToolSingleReminder);
		if (equipChangedToolModifierReminder == null)
		{
			equipChangedToolModifierReminder = new ControlReminder.DoubleConfig
			{
				Text = text,
				FadeInDelay = 0.5f,
				FadeInTime = 1f,
				FadeOutTime = 0.5f,
				Button1 = HeroActionButton.QUICK_CAST
			};
		}
		ControlReminder.AddReminder(equipChangedToolModifierReminder);
	}

	private void OnDestroyPersonalPools()
	{
		RefreshEquippedState();
		if (GameManager.instance.IsInSceneTransition)
		{
			ClearCustomToolOverride();
		}
	}

	public static IEnumerable<ToolItem> GetUnlockedTools()
	{
		if (!ManagerSingleton<ToolItemManager>.Instance)
		{
			return Enumerable.Empty<ToolItem>();
		}
		return ManagerSingleton<ToolItemManager>.Instance.toolItems.Where((ToolItem tool) => (bool)tool && tool.IsUnlockedNotHidden);
	}

	public static List<ToolCrest> GetAllCrests()
	{
		if ((bool)ManagerSingleton<ToolItemManager>.Instance)
		{
			return ManagerSingleton<ToolItemManager>.Instance.crestList.Where((ToolCrest crest) => crest != null).ToList();
		}
		return new List<ToolCrest>();
	}

	public static ToolCrest GetCrestByName(string name)
	{
		if (!ManagerSingleton<ToolItemManager>.Instance)
		{
			return null;
		}
		return ManagerSingleton<ToolItemManager>.Instance.crestList.GetByName(name);
	}

	public static ToolItem GetToolByName(string name)
	{
		if (!ManagerSingleton<ToolItemManager>.Instance)
		{
			return null;
		}
		return ManagerSingleton<ToolItemManager>.Instance.toolItems.GetByName(name);
	}

	public static List<ToolItem> GetEquippedToolsForCrest(string crestId)
	{
		if (string.IsNullOrEmpty(crestId))
		{
			return null;
		}
		return PlayerData.instance.ToolEquips.GetData(crestId).Slots?.Select((ToolCrestsData.SlotData slotInfo) => GetToolByName(slotInfo.EquippedTool)).ToList();
	}

	private static List<ToolItem> GetCurrentEquippedTools()
	{
		List<ToolItem> obj = GetEquippedToolsForCrest(PlayerData.instance.CurrentCrestID) ?? new List<ToolItem>();
		IEnumerable<ToolItem> collection = from data in PlayerData.instance.ExtraToolEquips.GetValidDatas((ToolCrestsData.SlotData data) => !string.IsNullOrEmpty(data.EquippedTool))
			select GetToolByName(data.EquippedTool);
		obj.AddRange(collection);
		return obj;
	}

	public static void SetEquippedTools(string crestId, List<string> equippedTools)
	{
		if (string.IsNullOrEmpty(crestId))
		{
			return;
		}
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		PlayerData instance2 = PlayerData.instance;
		ToolCrestsData.Data data = instance2.ToolEquips.GetData(crestId);
		if (data.Slots == null)
		{
			data.Slots = new List<ToolCrestsData.SlotData>(equippedTools.Count);
		}
		ToolCrest crestByName = GetCrestByName(crestId);
		ToolItem toolItem = null;
		bool flag = false;
		for (int i = 0; i < equippedTools.Count; i++)
		{
			string text = equippedTools[i];
			if (i >= data.Slots.Count || !(data.Slots[i].EquippedTool == text))
			{
				instance.queueEquipsChanged = true;
				ToolItem toolByName = GetToolByName(text);
				if ((bool)toolByName && toolByName.Type.IsAttackType())
				{
					toolItem = toolByName;
				}
				if (i < crestByName.Slots.Length && crestByName.Slots[i].Type.IsAttackType())
				{
					flag = true;
				}
			}
		}
		if (flag || (bool)toolItem)
		{
			instance.queueAttackToolsChanged = true;
			if ((bool)instance.lastEquippedAttackTool)
			{
				instance.equipChangedToolSingleReminder.Disappear(isInstant: true);
				instance.equipChangedToolModifierReminder.Disappear(isInstant: true);
				instance.lastEquippedAttackTool = null;
			}
			instance.lastEquippedAttackTool = (((bool)toolItem && toolItem.Type == ToolItemType.Red) ? toolItem : null);
		}
		for (int j = 0; j < equippedTools.Count; j++)
		{
			if (j < data.Slots.Count)
			{
				ToolCrestsData.SlotData value = data.Slots[j];
				value.EquippedTool = equippedTools[j];
				data.Slots[j] = value;
			}
			else
			{
				data.Slots.Add(new ToolCrestsData.SlotData
				{
					EquippedTool = equippedTools[j]
				});
			}
		}
		instance2.ToolEquips.SetData(crestId, data);
		ClearToolCache();
		UpdateToolCap();
	}

	public static void SetExtraEquippedTool(string slotId, ToolItem tool)
	{
		SetExtraEquippedTool(slotId, tool ? tool.name : string.Empty);
	}

	public static void SetExtraEquippedTool(string slotId, string toolName)
	{
		PlayerData instance = PlayerData.instance;
		ToolCrestsData.SlotData data = instance.ExtraToolEquips.GetData(slotId);
		if (!(data.EquippedTool == toolName))
		{
			data.EquippedTool = toolName;
			instance.ExtraToolEquips.SetData(slotId, data);
			ClearToolCache();
			UpdateToolCap();
			ManagerSingleton<ToolItemManager>.Instance.queueEquipsChanged = true;
		}
	}

	public static void SetEquippedCrest(string crestId)
	{
		if (string.IsNullOrEmpty(crestId))
		{
			ToolCrest toolCrest = ManagerSingleton<ToolItemManager>.Instance.crestList.FirstOrDefault((ToolCrest crest) => crest != null);
			if (!(toolCrest != null))
			{
				return;
			}
			crestId = toolCrest.name;
		}
		PlayerData instance = PlayerData.instance;
		List<ToolCrestsData.SlotData> slots = instance.ToolEquips.GetData(crestId).Slots;
		if (slots != null)
		{
			for (int num = 0; num < slots.Count; num++)
			{
				ToolCrestsData.SlotData value = slots[num];
				bool flag = false;
				foreach (ToolCrestsData.SlotData validData in instance.ExtraToolEquips.GetValidDatas())
				{
					if (!string.IsNullOrEmpty(value.EquippedTool) && !string.IsNullOrEmpty(validData.EquippedTool) && !(value.EquippedTool != validData.EquippedTool))
					{
						value.EquippedTool = string.Empty;
						flag = true;
					}
				}
				if (flag)
				{
					slots[num] = value;
				}
			}
		}
		if (!(instance.CurrentCrestID == crestId))
		{
			instance.PreviousCrestID = instance.CurrentCrestID;
			instance.CurrentCrestID = crestId;
			instance.IsCurrentCrestTemp = false;
			ClearToolCache();
			UpdateToolCap();
			if ((bool)ManagerSingleton<ToolItemManager>.Instance.cursedCrest)
			{
				IsCursed = ManagerSingleton<ToolItemManager>.Instance.cursedCrest.IsEquipped;
			}
			ToolItemManager instance2 = ManagerSingleton<ToolItemManager>.Instance;
			if ((bool)instance2)
			{
				instance2.queueEquipsChanged = true;
			}
		}
	}

	public static void SendEquippedChangedEvent(bool force = false)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!instance || (!instance.queueEquipsChanged && !force))
		{
			return;
		}
		RefreshEquippedState();
		HeroController instance2 = HeroController.instance;
		if ((bool)instance2)
		{
			instance2.RefreshSilk();
		}
		EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent);
		ToolItemManager.OnEquippedStateChanged?.Invoke();
		EventRegister.SendEvent(EventRegisterEvents.EquipsChangedPostEvent);
		instance.queueEquipsChanged = false;
		instance.queuedReminder = null;
		if (PlayerData.instance.SeenToolUsePrompt)
		{
			return;
		}
		ToolItem toolItem = instance.lastEquippedAttackTool;
		if (!toolItem)
		{
			return;
		}
		AttackToolBinding? attackToolBinding = GetAttackToolBinding(toolItem);
		if (attackToolBinding.HasValue)
		{
			switch (attackToolBinding.GetValueOrDefault())
			{
			case AttackToolBinding.Neutral:
				instance.queuedReminder = instance.equipChangedToolSingleReminder;
				break;
			case AttackToolBinding.Up:
				instance.equipChangedToolModifierReminder.Button2 = HeroActionButton.UP;
				instance.queuedReminder = instance.equipChangedToolModifierReminder;
				break;
			case AttackToolBinding.Down:
				instance.equipChangedToolModifierReminder.Button2 = HeroActionButton.DOWN;
				instance.queuedReminder = instance.equipChangedToolModifierReminder;
				break;
			default:
				throw new ArgumentOutOfRangeException();
			}
		}
	}

	public static void RefreshEquippedState()
	{
		ClearToolCache();
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!(instance == null) && (bool)instance.cursedCrest)
		{
			IsCursed = instance.cursedCrest.IsEquipped;
		}
	}

	private static void ClearToolCache()
	{
		toolCache.Clear();
		IncrementVersion();
	}

	public static void ShowQueuedReminder()
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if ((bool)instance && instance.queuedReminder != null)
		{
			instance.queuedReminder.DoAppear();
			instance.queuedReminder = null;
		}
	}

	public static bool IsToolEquipped(ToolItem tool, ToolEquippedReadSource readSource)
	{
		if (IsCursed)
		{
			return false;
		}
		if (!tool)
		{
			return false;
		}
		switch (ActiveState)
		{
		case ToolsActiveStates.Cutscene:
			if (readSource == ToolEquippedReadSource.Active)
			{
				return false;
			}
			break;
		case ToolsActiveStates.Disabled:
			return false;
		}
		if (ManagerSingleton<ToolItemManager>.Instance.customToolOverride == tool)
		{
			return true;
		}
		if (toolCache.TryGetValue(tool, out var value))
		{
			return value;
		}
		bool flag = IsToolEquipped(tool.name);
		toolCache.Add(tool, flag);
		return flag;
	}

	public static bool IsToolEquipped(string name)
	{
		if (CollectableItemManager.IsInHiddenMode())
		{
			return false;
		}
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		PlayerData instance = PlayerData.instance;
		foreach (string validName in instance.ExtraToolEquips.GetValidNames())
		{
			if (instance.ExtraToolEquips.GetData(validName).EquippedTool == name)
			{
				return true;
			}
		}
		string currentCrestID = instance.CurrentCrestID;
		List<ToolCrestsData.SlotData> slots = instance.ToolEquips.GetData(currentCrestID).Slots;
		if (slots == null)
		{
			return false;
		}
		foreach (ToolCrestsData.SlotData item in slots)
		{
			if (item.EquippedTool == name)
			{
				return true;
			}
		}
		return false;
	}

	public static void UnequipTool(ToolItem toolItem)
	{
		if ((bool)toolItem)
		{
			toolCache.Remove(toolItem);
			ReplaceToolEquips(toolItem.name, string.Empty);
		}
	}

	public static void ReplaceToolEquips(ToolItem oldTool, ToolItem newTool)
	{
		if ((bool)oldTool)
		{
			toolCache.Remove(oldTool);
			ReplaceToolEquips(oldTool.name, newTool ? newTool.name : string.Empty);
		}
	}

	private static void ReplaceToolEquips(string oldToolName, string newToolName)
	{
		if (string.IsNullOrEmpty(oldToolName))
		{
			return;
		}
		IncrementVersion();
		PlayerData instance = PlayerData.instance;
		foreach (string validName in instance.ExtraToolEquips.GetValidNames())
		{
			ToolCrestsData.SlotData data = instance.ExtraToolEquips.GetData(validName);
			string equippedTool = data.EquippedTool;
			if (!string.IsNullOrEmpty(equippedTool) && !(equippedTool != oldToolName))
			{
				data.EquippedTool = newToolName;
				instance.ExtraToolEquips.SetData(validName, data);
				ManagerSingleton<ToolItemManager>.Instance.queueEquipsChanged = true;
			}
		}
		string currentCrestID = instance.CurrentCrestID;
		if (string.IsNullOrEmpty(currentCrestID))
		{
			return;
		}
		List<ToolCrestsData.SlotData> slots = instance.ToolEquips.GetData(currentCrestID).Slots;
		if (slots == null)
		{
			return;
		}
		for (int i = 0; i < slots.Count; i++)
		{
			ToolCrestsData.SlotData value = slots[i];
			string equippedTool2 = value.EquippedTool;
			if (!string.IsNullOrEmpty(equippedTool2) && !(equippedTool2 != oldToolName))
			{
				value.EquippedTool = newToolName;
				slots[i] = value;
				ManagerSingleton<ToolItemManager>.Instance.queueEquipsChanged = true;
			}
		}
		UpdateToolCap();
	}

	private static void UpdateToolCap()
	{
		foreach (ToolItem allTool in GetAllTools())
		{
			ToolItemsData.Data savedData = allTool.SavedData;
			int toolStorageAmount = GetToolStorageAmount(allTool);
			if (savedData.AmountLeft > toolStorageAmount)
			{
				savedData.AmountLeft = toolStorageAmount;
				allTool.SavedData = savedData;
			}
		}
	}

	public static IEnumerable<ToolItem> GetAllTools()
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!instance || !instance.toolItems)
		{
			return Enumerable.Empty<ToolItem>();
		}
		return instance.toolItems;
	}

	public static void UnlockAllTools()
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if ((bool)instance && (bool)instance.toolItems)
		{
			instance.toolItems.UnlockAll();
		}
	}

	public static void UnlockAllCrests()
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if ((bool)instance && (bool)instance.crestList)
		{
			instance.crestList.UnlockAll();
			PlayerData instance2 = PlayerData.instance;
			instance2.UnlockedExtraBlueSlot = true;
			instance2.UnlockedExtraYellowSlot = true;
		}
	}

	public static ToolItem GetBoundAttackTool(AttackToolBinding binding, ToolEquippedReadSource readSource)
	{
		AttackToolBinding usedBinding;
		return GetBoundAttackTool(binding, readSource, out usedBinding);
	}

	public static ToolItem GetBoundAttackTool(AttackToolBinding binding, ToolEquippedReadSource readSource, out AttackToolBinding usedBinding)
	{
		usedBinding = binding;
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!instance || !instance.crestList)
		{
			return null;
		}
		ToolsActiveStates activeState = _activeState;
		if (activeState != ToolsActiveStates.Cutscene)
		{
			if (activeState == ToolsActiveStates.Disabled)
			{
				goto IL_0033;
			}
		}
		else if (readSource == ToolEquippedReadSource.Active)
		{
			goto IL_0033;
		}
		if (CollectableItemManager.IsInHiddenMode())
		{
			return null;
		}
		string currentCrestID = PlayerData.instance.CurrentCrestID;
		if (string.IsNullOrEmpty(currentCrestID))
		{
			return null;
		}
		ToolCrestsData.Data data = PlayerData.instance.ToolEquips.GetData(currentCrestID);
		if (instance.queueAttackToolsChanged || currentCrestID != instance.previousEquippedCrest)
		{
			ToolCrest byName = instance.crestList.GetByName(currentCrestID);
			if (!byName)
			{
				Debug.LogErrorFormat("Could not load crest {0}", currentCrestID);
				return null;
			}
			List<ToolCrest.SlotInfo> list = new List<ToolCrest.SlotInfo>(3);
			List<ToolItem> list2 = new List<ToolItem>(3);
			if (data.Slots != null && byName.Slots != null)
			{
				for (int i = 0; i < Mathf.Min(byName.Slots.Length, data.Slots.Count); i++)
				{
					string equippedTool = data.Slots[i].EquippedTool;
					if (!string.IsNullOrEmpty(equippedTool))
					{
						ToolItem toolByName = GetToolByName(equippedTool);
						if (!toolByName)
						{
							Debug.LogErrorFormat("Equipped tool {0} could not be found", equippedTool);
						}
						else if (toolByName.Type.IsAttackType())
						{
							list2.Add(toolByName);
							list.Add(byName.Slots[i]);
						}
					}
				}
			}
			ToolItemManager toolItemManager = instance;
			if (toolItemManager.boundAttackTools == null)
			{
				toolItemManager.boundAttackTools = new ToolItem[3];
			}
			if ((bool)instance.customToolOverride)
			{
				instance.boundAttackTools[0] = instance.customToolOverride;
				instance.boundAttackTools[1] = null;
				instance.boundAttackTools[2] = null;
			}
			else
			{
				instance.boundAttackTools[0] = GetToolForBinding(list2, list, AttackToolBinding.Neutral);
				instance.boundAttackTools[1] = GetToolForBinding(list2, list, AttackToolBinding.Up);
				instance.boundAttackTools[2] = GetToolForBinding(list2, list, AttackToolBinding.Down);
			}
			instance.queueAttackToolsChanged = false;
			instance.previousEquippedCrest = currentCrestID;
		}
		switch (readSource)
		{
		case ToolEquippedReadSource.Active:
		{
			if ((bool)instance.customToolOverride)
			{
				return instance.customToolOverride;
			}
			ToolItem toolItem = instance.boundAttackTools[(int)binding];
			if (!toolItem && binding != AttackToolBinding.Neutral)
			{
				usedBinding = AttackToolBinding.Neutral;
				return instance.boundAttackTools[0];
			}
			return toolItem;
		}
		case ToolEquippedReadSource.Hud:
			return instance.boundAttackTools[(int)binding];
		default:
			throw new ArgumentOutOfRangeException("readSource", readSource, null);
		}
		IL_0033:
		return null;
	}

	private static ToolItem GetToolForBinding(List<ToolItem> equippedAttackTools, List<ToolCrest.SlotInfo> crestSlots, AttackToolBinding binding)
	{
		for (int i = 0; i < equippedAttackTools.Count; i++)
		{
			if (crestSlots[i].AttackBinding == binding)
			{
				return equippedAttackTools[i];
			}
		}
		return null;
	}

	public static AttackToolBinding? GetAttackToolBinding(ToolItem tool)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if ((bool)instance.customToolOverride)
		{
			if (tool == instance.customToolOverride)
			{
				return AttackToolBinding.Neutral;
			}
			return null;
		}
		if (!tool || !tool.Type.IsAttackType())
		{
			return null;
		}
		string currentCrestID = PlayerData.instance.CurrentCrestID;
		if (string.IsNullOrEmpty(currentCrestID))
		{
			return null;
		}
		ToolCrest byName = instance.crestList.GetByName(currentCrestID);
		if (!byName)
		{
			Debug.LogErrorFormat("Could not load crest {0}", currentCrestID);
			return null;
		}
		List<ToolCrestsData.SlotData> slots = PlayerData.instance.ToolEquips.GetData(currentCrestID).Slots;
		if (slots == null)
		{
			return null;
		}
		for (int i = 0; i < Mathf.Min(slots.Count, byName.Slots.Length); i++)
		{
			string equippedTool = slots[i].EquippedTool;
			if (!string.IsNullOrEmpty(equippedTool) && equippedTool.Equals(tool.name))
			{
				return byName.Slots[i].AttackBinding;
			}
		}
		return null;
	}

	public static int GetToolStorageAmount(ToolItem tool)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (tool == instance.customToolOverride)
		{
			return instance.customToolAmount;
		}
		if (tool.PreventStorageIncrease)
		{
			return tool.BaseStorageAmount;
		}
		float num = (float)tool.BaseStorageAmount * Gameplay.ToolPouchUpgradeIncrease * (float)PlayerData.instance.ToolPouchUpgrades;
		if (Gameplay.ShellSatchelTool.IsEquippedHud)
		{
			num += (float)tool.BaseStorageAmount * Gameplay.ShellSatchelToolIncrease;
		}
		return tool.BaseStorageAmount + Mathf.FloorToInt(num);
	}

	public static bool TryReplenishTools(bool doReplenish, ReplenishMethod method)
	{
		if (string.IsNullOrEmpty(PlayerData.instance.CurrentCrestID))
		{
			return false;
		}
		bool flag = false;
		List<ToolItem> currentEquippedTools = GetCurrentEquippedTools();
		if (currentEquippedTools == null)
		{
			return false;
		}
		ArrayForEnumAttribute.EnsureArraySize(ref _startingCurrencyAmounts, typeof(CurrencyType));
		ArrayForEnumAttribute.EnsureArraySize(ref _endingCurrencyAmounts, typeof(CurrencyType));
		Array values = Enum.GetValues(typeof(CurrencyType));
		for (int i = 0; i < values.Length; i++)
		{
			CurrencyType type = (CurrencyType)values.GetValue(i);
			_endingCurrencyAmounts[i] = (_startingCurrencyAmounts[i] = CurrencyManager.GetCurrencyAmount(type));
		}
		_liquidCostsTemp?.Clear();
		bool flag2 = false;
		bool flag3 = true;
		currentEquippedTools.RemoveAll((ToolItem tool) => tool == null || !tool.IsAutoReplenished());
		while (flag3)
		{
			flag3 = false;
			foreach (ToolItem item in currentEquippedTools)
			{
				if ((method == ReplenishMethod.QuickCraft && item.ReplenishResource == ToolItem.ReplenishResources.None) || item.ReplenishUsage == ToolItem.ReplenishUsages.OneForOne)
				{
					continue;
				}
				ToolItemsData.Data toolData = PlayerData.instance.GetToolData(item.name);
				int toolStorageAmount = GetToolStorageAmount(item);
				if (toolData.AmountLeft >= toolStorageAmount)
				{
					continue;
				}
				float outCost = item.ReplenishUsage switch
				{
					ToolItem.ReplenishUsages.Percentage => 1f / (float)item.BaseStorageAmount * (float)Gameplay.ToolReplenishCost, 
					ToolItem.ReplenishUsages.OneForOne => 1f, 
					ToolItem.ReplenishUsages.Custom => 0f, 
					_ => throw new ArgumentOutOfRangeException(), 
				} * item.ReplenishUsageMultiplier;
				float inCost = outCost;
				int reserveCost;
				bool flag4 = item.TryReplenishSingle(doReplenish: false, outCost, out outCost, out reserveCost);
				if (flag4)
				{
					flag2 = true;
				}
				if (flag4 && doReplenish)
				{
					if (item.ReplenishResource != ToolItem.ReplenishResources.None && _endingCurrencyAmounts[(int)item.ReplenishResource] - outCost <= -0.5f)
					{
						continue;
					}
					flag4 = item.TryReplenishSingle(doReplenish: true, inCost, out outCost, out reserveCost);
				}
				bool flag5 = true;
				if (item is ToolItemStatesLiquid toolItemStatesLiquid)
				{
					if (_liquidCostsTemp == null)
					{
						_liquidCostsTemp = new Dictionary<ToolItemStatesLiquid, int>();
					}
					int valueOrDefault = _liquidCostsTemp.GetValueOrDefault(toolItemStatesLiquid, 0);
					int num = valueOrDefault + reserveCost;
					if ((num > 0 || toolItemStatesLiquid.HasInfiniteRefills) && toolItemStatesLiquid.LiquidSavedData.RefillsLeft >= num && !toolItemStatesLiquid.LiquidSavedData.UsedExtra)
					{
						valueOrDefault += reserveCost;
						_liquidCostsTemp[toolItemStatesLiquid] = valueOrDefault;
					}
					else
					{
						flag5 = false;
					}
				}
				if (!flag4)
				{
					continue;
				}
				if (outCost > 0f && item.ReplenishResource != ToolItem.ReplenishResources.None)
				{
					float num2 = _endingCurrencyAmounts[(int)item.ReplenishResource];
					if (num2 <= 0f || num2 - outCost <= -0.5f)
					{
						continue;
					}
					num2 -= outCost;
					_endingCurrencyAmounts[(int)item.ReplenishResource] = Mathf.Max(num2, 0f);
				}
				if (doReplenish && flag5)
				{
					toolData.AmountLeft++;
					if (toolData.AmountLeft < toolStorageAmount)
					{
						flag3 = true;
					}
					PlayerData.instance.SetToolData(item.name, toolData);
				}
				flag = true;
			}
		}
		if (!flag && method == ReplenishMethod.QuickCraft && Mathf.CeilToInt(_endingCurrencyAmounts[1]) > 0)
		{
			flag = true;
			if (flag2)
			{
				_endingCurrencyAmounts[1] = 0f;
			}
			else
			{
				float num3 = _endingCurrencyAmounts[1];
				num3 -= (float)Gameplay.ToolmasterQuickCraftNoneUsage;
				if (num3 < 0f)
				{
					num3 = 0f;
				}
				_endingCurrencyAmounts[1] = num3;
			}
		}
		if (!doReplenish)
		{
			return flag;
		}
		bool flag6 = method != ReplenishMethod.BenchSilent;
		for (int num4 = 0; num4 < values.Length; num4++)
		{
			int num5 = Mathf.RoundToInt(_startingCurrencyAmounts[num4] - _endingCurrencyAmounts[num4]);
			if (num5 > 0)
			{
				CurrencyManager.TakeCurrency(num5, (CurrencyType)values.GetValue(num4), flag6);
			}
		}
		if (_liquidCostsTemp != null)
		{
			foreach (var (toolItemStatesLiquid3, num7) in _liquidCostsTemp)
			{
				if (num7 <= 0)
				{
					if (flag6)
					{
						toolItemStatesLiquid3.ShowLiquidInfiniteRefills();
					}
				}
				else
				{
					toolItemStatesLiquid3.TakeLiquid(num7, flag6);
				}
			}
			_liquidCostsTemp.Clear();
		}
		ReportAllBoundAttackToolsUpdated();
		SendEquippedChangedEvent(force: true);
		return flag;
	}

	public static void ReportAllBoundAttackToolsUpdated()
	{
		foreach (AttackToolBinding item in Enum.GetValues(typeof(AttackToolBinding)).Cast<AttackToolBinding>())
		{
			ReportBoundAttackToolUpdated(item);
		}
		IncrementVersion();
	}

	public static void ReportBoundAttackToolUpdated(AttackToolBinding binding)
	{
		ToolItemManager.BoundAttackToolUpdated?.Invoke(binding);
	}

	public static void ReportBoundAttackToolUsed(AttackToolBinding binding)
	{
		ToolItemManager.BoundAttackToolUsed?.Invoke(binding);
		ToolItem boundAttackTool = GetBoundAttackTool(binding, ToolEquippedReadSource.Active);
		if ((bool)boundAttackTool)
		{
			if (IsInfiniteToolUseEnabled)
			{
				ToolItemsData.Data savedData = boundAttackTool.SavedData;
				savedData.AmountLeft = GetToolStorageAmount(boundAttackTool);
				boundAttackTool.SavedData = savedData;
			}
			ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
			if ((bool)instance && boundAttackTool == instance.lastEquippedAttackTool)
			{
				instance.equipChangedToolSingleReminder.Disappear(isInstant: false);
				instance.equipChangedToolModifierReminder.Disappear(isInstant: false);
				instance.lastEquippedAttackTool = null;
				PlayerData.instance.SeenToolUsePrompt = true;
				instance.queuedReminder = null;
			}
		}
	}

	public static void ReportBoundAttackToolFailed(AttackToolBinding binding)
	{
		ToolItemManager.BoundAttackToolFailed?.Invoke(binding);
	}

	public static void SetCustomToolOverride(ToolItem tool, int? amount)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		ToolItem toolItem = instance.customToolOverride;
		if ((bool)toolItem)
		{
			ToolItemsData.Data savedData = toolItem.SavedData;
			savedData.AmountLeft = 0;
			toolItem.SavedData = savedData;
		}
		instance.customToolOverride = null;
		instance.customToolAmount = amount ?? GetToolStorageAmount(tool);
		if ((bool)tool)
		{
			ToolItemsData.Data savedData2 = tool.SavedData;
			savedData2.AmountLeft = instance.customToolAmount;
			tool.SavedData = savedData2;
			EventRegister.SendEvent(EventRegisterEvents.ShowCustomToolReminder);
		}
		else
		{
			EventRegister.SendEvent(EventRegisterEvents.HideCustomToolReminder);
		}
		instance.customToolOverride = tool;
		instance.queueAttackToolsChanged = true;
		ReportAllBoundAttackToolsUpdated();
	}

	public static void SetCustomToolOverride(ToolItem tool, int? amount, LocalisedString promptText, float promptAppearDelay)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		instance.toolOverrideReminder.Text = promptText;
		instance.toolOverrideReminder.FadeInDelay = promptAppearDelay;
		SetCustomToolOverride(tool, amount);
	}

	public static void ClearCustomToolOverride()
	{
		SetCustomToolOverride(null, null);
	}

	public static void AutoEquip(ToolItem tool)
	{
		if (tool == null)
		{
			return;
		}
		string currentCrestID = PlayerData.instance.CurrentCrestID;
		ToolCrest crestByName = GetCrestByName(currentCrestID);
		List<ToolItem> equippedToolsForCrest = GetEquippedToolsForCrest(currentCrestID);
		List<string> list = new List<string>(crestByName.Slots.Length);
		for (int i = 0; i < crestByName.Slots.Length; i++)
		{
			ToolItem toolItem = ((equippedToolsForCrest != null && i < equippedToolsForCrest.Count) ? equippedToolsForCrest[i] : null);
			list.Add((toolItem != null) ? toolItem.name : string.Empty);
		}
		int num;
		if (tool.Type == ToolItemType.Skill)
		{
			num = -1;
			for (int j = 0; j < crestByName.Slots.Length; j++)
			{
				ToolCrest.SlotInfo slotInfo = crestByName.Slots[j];
				if (slotInfo.Type == ToolItemType.Skill && slotInfo.AttackBinding == AttackToolBinding.Neutral)
				{
					num = j;
					break;
				}
			}
		}
		else
		{
			int num2 = -1;
			int num3 = -1;
			for (int k = 0; k < crestByName.Slots.Length; k++)
			{
				if (crestByName.Slots[k].Type == tool.Type)
				{
					num2 = k;
					if (string.IsNullOrEmpty(list[k]))
					{
						num3 = k;
					}
				}
			}
			num = ((num3 >= 0) ? num3 : num2);
		}
		if (num >= 0)
		{
			list[num] = tool.name;
		}
		UnlockedTool = tool;
		InventoryPaneList.SetNextOpen("Tools");
		SetEquippedTools(currentCrestID, list);
		SendEquippedChangedEvent();
	}

	public static void AutoEquip(ToolCrest crest, bool markTemp, bool removeTools)
	{
		PlayerData instance = PlayerData.instance;
		if (crest == null)
		{
			crest = GetCrestByName(instance.PreviousCrestID);
			if (crest == null)
			{
				crest = GetAllCrests().FirstOrDefault((ToolCrest c) => c.IsVisible);
				if (crest == null)
				{
					return;
				}
			}
		}
		string currentCrestID = instance.CurrentCrestID;
		string text = crest.name;
		if (text != currentCrestID)
		{
			instance.PreviousCrestID = currentCrestID;
		}
		else
		{
			markTemp = false;
		}
		bool isCurrentCrestTemp = instance.IsCurrentCrestTemp;
		SetEquippedCrest(text);
		instance.IsCurrentCrestTemp = markTemp;
		List<ToolItem> equipPool;
		if (removeTools)
		{
			List<string> list = new List<string>(crest.Slots.Length);
			if (!markTemp && !isCurrentCrestTemp)
			{
				equipPool = GetEquippedToolsForCrest(currentCrestID) ?? new List<ToolItem>();
				RemoveNonSkills();
				if (equipPool.Count == 0)
				{
					equipPool = GetEquippedToolsForCrest(text) ?? new List<ToolItem>();
					RemoveNonSkills();
				}
				for (int num = 0; num < crest.Slots.Length; num++)
				{
					ToolCrest.SlotInfo slotInfo = crest.Slots[num];
					if (slotInfo.Type != ToolItemType.Skill)
					{
						list.Add(string.Empty);
					}
					else if (equipPool.Count > 0)
					{
						ToolItem toolItem = equipPool[0];
						list.Add(toolItem.name);
						equipPool.RemoveAt(0);
					}
					else
					{
						if (slotInfo.AttackBinding != AttackToolBinding.Neutral)
						{
							continue;
						}
						if (Gameplay.DefaultSkillTool.IsUnlocked)
						{
							list.Add(Gameplay.DefaultSkillTool.name);
							continue;
						}
						ToolItem toolItem2 = GetUnlockedTools().FirstOrDefault((ToolItem t) => t.Type == ToolItemType.Skill);
						if ((bool)toolItem2)
						{
							list.Add(toolItem2.name);
						}
					}
				}
			}
			SetEquippedTools(crest.name, list);
		}
		SendEquippedChangedEvent();
		InventoryPaneList.SetNextOpen("Tools");
		void RemoveNonSkills()
		{
			for (int num2 = equipPool.Count - 1; num2 >= 0; num2--)
			{
				ToolItem toolItem3 = equipPool[num2];
				if (!toolItem3 || toolItem3.Type != ToolItemType.Skill)
				{
					equipPool.RemoveAt(num2);
				}
			}
		}
	}

	public static void RemoveToolFromAllCrests(ToolItem tool)
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!tool || !instance)
		{
			return;
		}
		AttackToolBinding? attackToolBinding = GetAttackToolBinding(tool);
		ToolCrestsData toolEquips = PlayerData.instance.ToolEquips;
		foreach (ToolCrest crest in instance.crestList)
		{
			ToolCrestsData.Data data = toolEquips.GetData(crest.name);
			if (data.Slots == null)
			{
				continue;
			}
			bool flag = false;
			for (int i = 0; i < data.Slots.Count; i++)
			{
				ToolCrestsData.SlotData value = data.Slots[i];
				if (!(value.EquippedTool != tool.name))
				{
					value.EquippedTool = string.Empty;
					data.Slots[i] = value;
					flag = true;
				}
			}
			if (flag)
			{
				instance.queueEquipsChanged = true;
				toolEquips.SetData(crest.name, data);
			}
		}
		if (attackToolBinding.HasValue)
		{
			instance.queueAttackToolsChanged = true;
			ReportBoundAttackToolUpdated(attackToolBinding.Value);
		}
		SendEquippedChangedEvent();
	}

	[UsedImplicitly]
	public static void ResetPreviousCrest()
	{
		ToolItemManager instance = ManagerSingleton<ToolItemManager>.Instance;
		if (!instance)
		{
			return;
		}
		PlayerData instance2 = PlayerData.instance;
		ToolCrestsData toolEquips = instance2.ToolEquips;
		ToolCrestsData.Data data = toolEquips.GetData(instance2.PreviousCrestID);
		if (data.Slots == null)
		{
			return;
		}
		for (int i = 0; i < data.Slots.Count; i++)
		{
			ToolCrestsData.SlotData value = data.Slots[i];
			if (!string.IsNullOrEmpty(value.EquippedTool))
			{
				ToolItem toolByName = GetToolByName(value.EquippedTool);
				if ((bool)toolByName && toolByName.Type != ToolItemType.Skill)
				{
					value.EquippedTool = string.Empty;
					data.Slots[i] = value;
				}
			}
		}
		instance.queueEquipsChanged = true;
		toolEquips.SetData(instance2.PreviousCrestID, data);
		ReportAllBoundAttackToolsUpdated();
		SendEquippedChangedEvent();
	}

	public static int GetOwnedToolsCount(OwnToolsCheckFlags checkFlags)
	{
		return GetUnlockedTools().Count((ToolItem tool) => tool.Type switch
		{
			ToolItemType.Red => (checkFlags & OwnToolsCheckFlags.Red) == OwnToolsCheckFlags.Red, 
			ToolItemType.Blue => (checkFlags & OwnToolsCheckFlags.Blue) == OwnToolsCheckFlags.Blue, 
			ToolItemType.Yellow => (checkFlags & OwnToolsCheckFlags.Yellow) == OwnToolsCheckFlags.Yellow, 
			ToolItemType.Skill => (checkFlags & OwnToolsCheckFlags.Skill) == OwnToolsCheckFlags.Skill, 
			_ => false, 
		});
	}

	public static void SetIsInCutscene(bool value)
	{
		SetActiveState(value ? ToolsActiveStates.Cutscene : ToolsActiveStates.Active);
	}

	public static void SetActiveState(ToolsActiveStates value)
	{
		SetActiveState(value, skipAnims: true);
	}

	public static void SetActiveState(ToolsActiveStates value, bool skipAnims)
	{
		if (value == _activeState)
		{
			return;
		}
		try
		{
			IsChangingActiveState = true;
			_activeState = value;
			ReportAllBoundAttackToolsUpdated();
			SendEquippedChangedEvent(force: true);
			EventRegister.SendEvent("DELIVERY HUD REFRESH");
			if (!skipAnims)
			{
				EventRegister.SendEvent(EventRegisterEvents.InventoryOpenComplete);
			}
		}
		finally
		{
			IsChangingActiveState = false;
		}
	}

	public static void ReportToolUnlocked(ToolItemType type)
	{
		ReportToolUnlocked(type, queueAchievement: false);
	}

	public static void ReportToolUnlocked(ToolItemType type, bool queueAchievement)
	{
		GameManager instance = GameManager.instance;
		string key;
		string key2;
		Func<ToolItem, bool> condition;
		if (type == ToolItemType.Skill)
		{
			key = "FIRST_SILK_SKILL";
			key2 = "ALL_SILK_SKILLS";
			condition = (ToolItem tool) => tool.Type == ToolItemType.Skill;
		}
		else
		{
			key = "FIRST_TOOL";
			key2 = "ALL_TOOLS";
			condition = (ToolItem tool) => tool.Type != ToolItemType.Skill;
		}
		int count = GetCount(GetUnlockedTools(), condition);
		int count2 = GetCount(GetAllTools(), condition);
		if (queueAchievement)
		{
			instance.QueueAchievement(key);
			instance.QueueAchievementProgress(key2, count, count2);
		}
		else
		{
			instance.AwardAchievement(key);
			instance.UpdateAchievementProgress(key2, count, count2);
		}
	}

	public static int GetCount(IEnumerable<ToolItem> collection, Func<ToolItem, bool> condition)
	{
		return (from tool in collection
			where (condition == null || condition(tool)) && tool.IsCounted
			group tool by tool.CountKey).Count();
	}

	public static void ReportCrestUnlocked(bool reportAchievement)
	{
		if (reportAchievement)
		{
			GameManager instance = GameManager.instance;
			instance.AwardAchievement("FIRST_CREST");
			int unlockedCrestsCount = GetUnlockedCrestsCount();
			int max = GetAllCrests().Count((ToolCrest crest) => !crest.IsHidden && crest.IsBaseVersion);
			instance.UpdateAchievementProgress("ALL_CRESTS", unlockedCrestsCount, max);
		}
	}

	public static int GetUnlockedCrestsCount()
	{
		return GetAllCrests().Count((ToolCrest crest) => !crest.IsHidden && crest.IsBaseVersion && crest.IsUnlocked);
	}
}
