using System;
using System.Collections.Generic;
using GlobalSettings;
using UnityEngine;

public class SilkSpool : MonoBehaviour
{
	public enum SilkAddSource
	{
		Normal,
		Moss
	}

	public enum SilkTakeSource
	{
		Normal,
		Wisp,
		Curse,
		Drain,
		ActiveUse,
		Parts
	}

	[Flags]
	public enum SilkUsingFlags
	{
		None = 0,
		Normal = 1,
		Acid = 2,
		Maggot = 4,
		Void = 8,
		Curse = 0x10,
		Drain = 0x20
	}

	public const string SILK_REFRESHED_EVENT = "SILK REFRESHED";

	public float firstChunk_x = 0.27f;

	public float chunkDistance_x = 0.18f;

	[SerializeField]
	private Transform chunkParent;

	[SerializeField]
	private Transform capR;

	[SerializeField]
	private Transform capRAnchored;

	[SerializeField]
	private Transform seg1;

	[SerializeField]
	private GameObject bindNotch;

	[SerializeField]
	private GameObject silkChunkPrefab;

	[SerializeField]
	private PlayMakerFSM bindOrb;

	[Space]
	[SerializeField]
	private Animator silkFailedAnimator;

	[SerializeField]
	private RandomAudioClipTable silkFailedAudioTable;

	[Space]
	[SerializeField]
	private GameObject spoolParent;

	[SerializeField]
	private GameObject activeParent;

	[SerializeField]
	private GameObject brokenParent;

	[SerializeField]
	private GameObject cursedParent;

	[SerializeField]
	private tk2dSpriteAnimator cursedAnimator;

	[SerializeField]
	private AudioEvent cursedAudio;

	[Space]
	[SerializeField]
	private GameObject silkFinalCutsceneBurst;

	[Space]
	[SerializeField]
	private GameObject act3EndingParent;

	[SerializeField]
	private Transform act3EndingBarScaler;

	[SerializeField]
	private Transform[] act3EndingBarInverseScalers;

	private readonly List<SilkChunk> silkChunks = new List<SilkChunk>();

	private bool hasDrawnSpool;

	private SilkAddSource? queuedAddSource;

	private SilkTakeSource? queuedTakeSource;

	private bool wasInSilkFinalCutscene;

	private readonly List<SilkUsingFlags> usingSilk = new List<SilkUsingFlags>();

	private SilkChunk wasUsingChunk;

	private List<SilkChunk> mossChunks;

	private SilkChunk partsChunk;

	private readonly int animFailedTrigger = Animator.StringToHash("Failed");

	private readonly int appearAnimId = Animator.StringToHash("Appear");

	private readonly int disappearAnimId = Animator.StringToHash("Disappear");

	public static SilkSpool Instance { get; private set; }

	public static float BindCost
	{
		get
		{
			if (PlayerData.instance.IsAnyCursed)
			{
				return float.MaxValue;
			}
			_ = Gameplay.WitchCrest.IsEquipped;
			return 9f;
		}
	}

	private void Awake()
	{
		if ((bool)Instance)
		{
			return;
		}
		Instance = this;
		EventRegister.GetRegisterGuaranteed(base.gameObject, "TOOL EQUIPS CHANGED").ReceivedEvent += delegate
		{
			RefreshSilk();
			if (!Gameplay.MossCreep1Tool.IsEquippedHud && !Gameplay.MossCreep2Tool.IsEquippedHud)
			{
				CancelMossChunk();
			}
		};
		EventRegister.GetRegisterGuaranteed(base.gameObject, "BIND FAILED NOT ENOUGH").ReceivedEvent += delegate
		{
			if ((bool)silkFailedAnimator)
			{
				silkFailedAnimator.SetTrigger(animFailedTrigger);
			}
			silkFailedAudioTable.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
		};
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void Start()
	{
		if ((bool)spoolParent)
		{
			spoolParent.SetActive(value: false);
		}
	}

	public void DrawSpool()
	{
		DrawSpool(0);
	}

	public void DrawSpool(int maxOffset)
	{
		PlayerData instance = PlayerData.instance;
		int num = instance.CurrentSilkMaxBasic + maxOffset;
		int num2 = Mathf.Max(num, 9);
		float x = firstChunk_x + chunkDistance_x * (float)(num2 - 1) + 0.15f;
		Vector3 localPosition = capR.localPosition;
		localPosition = (capR.localPosition = new Vector3(x, localPosition.y, localPosition.z));
		capRAnchored.localPosition = localPosition;
		Vector3 localScale = seg1.localScale;
		seg1.localScale = new Vector3(x, localScale.y, localScale.z);
		bool flag = instance.IsSilkSpoolBroken && !instance.UnlockSilkFinalCutscene;
		bool isAnyCursed = instance.IsAnyCursed;
		if ((bool)activeParent)
		{
			bool flag2 = !flag && !isAnyCursed;
			bool activeInHierarchy = activeParent.activeInHierarchy;
			activeParent.SetActive(flag2);
			if (!activeInHierarchy && flag2 && !Gameplay.SpoolExtenderTool.Status.IsEquipped)
			{
				EventRegister.SendEvent("SPOOL CAP FIX");
			}
		}
		if ((bool)brokenParent)
		{
			brokenParent.SetActive(flag && !isAnyCursed);
		}
		if ((bool)cursedParent)
		{
			bool activeInHierarchy2 = cursedParent.activeInHierarchy;
			cursedParent.SetActive(isAnyCursed);
			if (isAnyCursed && !activeInHierarchy2)
			{
				cursedAnimator.Play("Spool Cursed Appear");
				cursedAudio.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
			}
		}
		if ((bool)spoolParent && !spoolParent.activeSelf)
		{
			spoolParent.SetActive(value: true);
		}
		RefreshBindNotch(num);
		hasDrawnSpool = true;
		RefreshSilk(queuedAddSource.GetValueOrDefault(), queuedTakeSource.GetValueOrDefault());
		queuedAddSource = null;
		queuedTakeSource = null;
		if (act3EndingParent.activeSelf)
		{
			float num3 = (float)num2 / 9f;
			act3EndingBarScaler.SetScaleX(num3);
			float newXScale = 1f / num3;
			Transform[] array = act3EndingBarInverseScalers;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].SetScaleX(newXScale);
			}
		}
	}

	public void ChangeSilk(int silk, int silkParts, SilkAddSource addSource, SilkTakeSource takeSource)
	{
		PlayerData instance = PlayerData.instance;
		if ((float)silk >= BindCost)
		{
			for (int i = 0; i < silkChunks.Count; i++)
			{
				SilkChunk silkChunk = silkChunks[i];
				if ((float)i < BindCost)
				{
					if (silkChunk.IsRegen)
					{
						silkChunk.Add(glowing: true);
					}
					else
					{
						silkChunk.StartGlow();
					}
				}
				else if (silkChunk.IsRegen)
				{
					silkChunk.Add(glowing: false);
				}
				else
				{
					silkChunk.EndGlow();
				}
			}
			bindOrb.enabled = true;
			bindOrb.SendEvent("BIND ACTIVE");
		}
		else if ((float)silk < BindCost)
		{
			for (int j = 0; j < silkChunks.Count; j++)
			{
				if (j + 1 <= silk)
				{
					SilkChunk silkChunk2 = silkChunks[j];
					if (silkChunk2.IsRegen)
					{
						silkChunk2.Add(glowing: false);
					}
					else
					{
						silkChunk2.EndGlow();
					}
				}
			}
			bindOrb.enabled = true;
			bindOrb.SendEvent("BIND INACTIVE");
		}
		if (silk > silkChunks.Count)
		{
			for (int num = silk - silkChunks.Count; num > 0; num--)
			{
				if (mossChunks != null && num <= mossChunks.Count && addSource == SilkAddSource.Moss)
				{
					int index = num - 1;
					SilkChunk silkChunk3 = mossChunks[index];
					silkChunk3.Add(IsGlowing(silk));
					silkChunk3.FinishMossState();
					silkChunks.Add(silkChunk3);
					mossChunks.RemoveAt(index);
				}
				else if (num == 1 && addSource == SilkAddSource.Normal && silkParts == 0 && partsChunk != null)
				{
					bool flag = IsGlowing(silk);
					partsChunk.Add(flag);
					if (!flag)
					{
						partsChunk.FinishPartsState();
					}
					silkChunks.Add(partsChunk);
					partsChunk = null;
				}
				else
				{
					SilkChunk silkChunk4 = SpawnNewChunk(addToList: true);
					if ((bool)silkChunk4)
					{
						silkChunk4.Add(IsGlowing(silk));
					}
				}
			}
		}
		if (silk < silkChunks.Count)
		{
			int num2 = silkChunks.Count - silk;
			int num3 = 1;
			if (num2 > 0)
			{
				List<SilkChunk> list = silkChunks;
				int num4 = num3;
				if (list[list.Count - num4].IsRegen)
				{
					num2--;
					num3++;
				}
			}
			while (num2 > 0)
			{
				List<SilkChunk> list2 = silkChunks;
				int num4 = num3;
				list2[list2.Count - num4].Remove(takeSource);
				silkChunks.RemoveAt(silkChunks.Count - num3);
				num2--;
			}
		}
		if (silkParts > 0)
		{
			if (partsChunk == null)
			{
				partsChunk = SpawnNewChunk(addToList: false);
			}
			partsChunk.SetPartsState(silkParts - 1);
		}
		else if ((bool)partsChunk)
		{
			partsChunk.Remove(SilkTakeSource.Parts);
			partsChunk = null;
			EvaluatePositions();
		}
		if (addSource == SilkAddSource.Moss)
		{
			CancelMossChunk();
		}
		EvaluatePositions();
		if (instance.UnlockSilkFinalCutscene && !wasInSilkFinalCutscene && (bool)silkFinalCutsceneBurst)
		{
			silkFinalCutsceneBurst.SetActive(value: false);
			silkFinalCutsceneBurst.SetActive(value: true);
		}
		wasInSilkFinalCutscene = instance.UnlockSilkFinalCutscene;
	}

	private bool IsGlowing(int silk)
	{
		if ((float)silk >= BindCost)
		{
			return (float)silkChunks.Count <= BindCost;
		}
		return false;
	}

	public void RefreshSilk()
	{
		RefreshSilk(SilkAddSource.Normal, SilkTakeSource.Normal);
		RefreshBindNotch(PlayerData.instance.CurrentSilkMaxBasic);
	}

	public void RefreshSilk(SilkAddSource addSource, SilkTakeSource takeSource)
	{
		EventRegister.SendEvent("SILK REFRESHED");
		if (!hasDrawnSpool)
		{
			queuedAddSource = addSource;
			queuedTakeSource = takeSource;
			return;
		}
		if ((bool)wasUsingChunk)
		{
			wasUsingChunk.PlayIdle();
			wasUsingChunk = null;
		}
		PlayerData instance = PlayerData.instance;
		ChangeSilk(instance.silk, instance.silkParts, addSource, takeSource);
		if (silkChunks.Count == 0)
		{
			return;
		}
		SilkUsingFlags silkUsingFlags = SilkUsingFlags.None;
		foreach (SilkUsingFlags item in usingSilk)
		{
			silkUsingFlags |= item;
		}
		if (silkUsingFlags == SilkUsingFlags.None)
		{
			return;
		}
		for (int num = silkChunks.Count - 1; num >= 0; num--)
		{
			SilkChunk silkChunk = silkChunks[num];
			if (!silkChunk.IsRegen)
			{
				silkChunk.SetUsing(silkUsingFlags);
				wasUsingChunk = silkChunk;
				break;
			}
		}
	}

	private void RefreshBindNotch(int currentSilkMax)
	{
		if ((bool)bindNotch)
		{
			float bindCost = BindCost;
			if ((float)currentSilkMax > bindCost)
			{
				bindNotch.SetActive(value: true);
				bindNotch.transform.SetLocalPositionX(firstChunk_x + chunkDistance_x * bindCost);
			}
			else
			{
				bindNotch.SetActive(value: false);
			}
		}
	}

	private SilkChunk SpawnNewChunk(bool addToList)
	{
		if (!silkChunkPrefab)
		{
			return null;
		}
		SilkChunk component = silkChunkPrefab.Spawn(chunkParent).GetComponent<SilkChunk>();
		if (addToList)
		{
			silkChunks.Add(component);
		}
		return component;
	}

	public bool AddUsing(SilkUsingFlags usingFlags, int amount = 1)
	{
		for (int i = 0; i < amount; i++)
		{
			usingSilk.Add(usingFlags);
		}
		if ((usingFlags & SilkUsingFlags.Curse) == SilkUsingFlags.Curse)
		{
			cursedAnimator.Play("Spool Cursed Bobbing");
		}
		RefreshSilk();
		return true;
	}

	public bool RemoveUsing(SilkUsingFlags usingFlags, int amount = 1)
	{
		bool result = false;
		for (int i = 0; i < amount; i++)
		{
			if (usingSilk.Remove(usingFlags))
			{
				result = true;
			}
		}
		if ((usingFlags & SilkUsingFlags.Curse) == SilkUsingFlags.Curse)
		{
			cursedAnimator.Play("Spool Cursed");
		}
		RefreshSilk();
		return result;
	}

	public void SetRegen(int amount, bool isUpgraded)
	{
		for (int i = 0; i < silkChunks.Count; i++)
		{
			SilkChunk silkChunk = silkChunks[i];
			if (silkChunk.IsRegen)
			{
				silkChunk.EndedRegen();
				silkChunk.gameObject.Recycle();
				silkChunks.RemoveAt(i);
			}
		}
		for (int j = 0; j < amount; j++)
		{
			SilkChunk silkChunk2 = SpawnNewChunk(addToList: true);
			if ((bool)silkChunk2)
			{
				silkChunk2.SetRegen(isUpgraded);
			}
		}
		EvaluatePositions();
	}

	public static void ResumeSilkAudio()
	{
		SilkSpool instance = Instance;
		if (instance == null)
		{
			return;
		}
		foreach (SilkChunk silkChunk in instance.silkChunks)
		{
			silkChunk.ResumeRegenAudioLoop();
		}
	}

	public static void EndSilkAudio()
	{
		SilkSpool instance = Instance;
		if (instance == null)
		{
			return;
		}
		foreach (SilkChunk silkChunk in instance.silkChunks)
		{
			silkChunk.StopRegenAudioLoop();
		}
	}

	private void EvaluatePositions()
	{
		float num = firstChunk_x;
		for (int i = 0; i < silkChunks.Count; i++)
		{
			SilkChunk silkChunk = silkChunks[i];
			silkChunk.transform.localPosition = new Vector3(num, 0f, -0.002f - (float)i * 1E-05f);
			if (!silkChunk.IsRegen)
			{
				num += chunkDistance_x;
			}
		}
		if ((bool)partsChunk)
		{
			partsChunk.transform.localPosition = new Vector3(num, 0f, -0.0015f);
			num += chunkDistance_x;
		}
		if (mossChunks == null)
		{
			return;
		}
		foreach (SilkChunk mossChunk in mossChunks)
		{
			mossChunk.transform.localPosition = new Vector3(num, 0f, -0.001f);
			num += chunkDistance_x;
		}
	}

	public void SetMossState(int state, int count = 1)
	{
		int num = state - 1;
		if (num < 0)
		{
			CancelMossChunk();
			return;
		}
		if (mossChunks == null)
		{
			mossChunks = new List<SilkChunk>(count);
		}
		while (mossChunks.Count < count)
		{
			SilkChunk item = SpawnNewChunk(addToList: false);
			mossChunks.Add(item);
			EvaluatePositions();
		}
		foreach (SilkChunk mossChunk in mossChunks)
		{
			mossChunk.SetMossState(num);
		}
	}

	private void CancelMossChunk()
	{
		if (mossChunks == null)
		{
			return;
		}
		foreach (SilkChunk mossChunk in mossChunks)
		{
			mossChunk.gameObject.Recycle();
		}
		mossChunks.Clear();
		EvaluatePositions();
	}
}
