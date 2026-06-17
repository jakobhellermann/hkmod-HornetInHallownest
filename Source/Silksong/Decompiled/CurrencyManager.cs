using System.Collections.Generic;
using UnityEngine;

namespace Silksong;

public sealed class CurrencyManager : ManagerSingleton<CurrencyManager>
{
	private sealed class CurrencyQueue
	{
		public int amount;

		public bool showCounter;

		public void Reset()
		{
			amount = 0;
			showCounter = false;
		}
	}

	private static readonly List<CurrencyQueue> currencyQueue = new List<CurrencyQueue>
	{
		new CurrencyQueue(),
		new CurrencyQueue()
	};

	private static bool hasInstance;

	private static bool updateQueued;

	private static PlayerData playerData => PlayerData.instance;

	protected override void Awake()
	{
		base.Awake();
		if (ManagerSingleton<CurrencyManager>.UnsafeInstance == this)
		{
			hasInstance = true;
		}
	}

	protected override void OnDestroy()
	{
		if (ManagerSingleton<CurrencyManager>.UnsafeInstance == this)
		{
			hasInstance = false;
		}
		base.OnDestroy();
	}

	private void LateUpdate()
	{
		updateQueued = false;
		for (int i = 0; i < CurrencyManager.currencyQueue.Count; i++)
		{
			CurrencyQueue currencyQueue = CurrencyManager.currencyQueue[i];
			if (currencyQueue.amount != 0)
			{
				ProcessCurrency(currencyQueue.amount, (CurrencyType)i, currencyQueue.showCounter);
			}
			currencyQueue.Reset();
		}
		base.enabled = updateQueued;
	}

	public static void AddGeo(int amount)
	{
		ChangeCurrency(amount, CurrencyType.Money);
	}

	public static void ToZero()
	{
		foreach (CurrencyQueue item in currencyQueue)
		{
			item.Reset();
		}
		CurrencyCounter.ToZero(CurrencyType.Money);
	}

	public static void AddGeoQuietly(int amount)
	{
		playerData.AddGeo(amount);
	}

	public static void AddGeoToCounter(int amount)
	{
		CurrencyCounter.Add(amount, CurrencyType.Money);
	}

	public static void TakeGeo(int amount)
	{
		ChangeCurrency(-amount, CurrencyType.Money);
	}

	public static void AddShards(int amount)
	{
		ChangeCurrency(amount, CurrencyType.Shard);
	}

	public static void TakeShards(int amount)
	{
		ChangeCurrency(-amount, CurrencyType.Shard);
	}

	public static int GetCurrencyAmount(CurrencyType type)
	{
		return type switch
		{
			CurrencyType.Money => playerData.geo, 
			CurrencyType.Shard => playerData.ShellShards, 
			_ => 0, 
		};
	}

	public static void TempStoreCurrency()
	{
		if (playerData.geo > 0)
		{
			playerData.TempGeoStore += playerData.geo;
			playerData.geo = 0;
		}
		if (playerData.ShellShards > 0)
		{
			playerData.TempShellShardStore += playerData.ShellShards;
			playerData.ShellShards = 0;
		}
	}

	public static void RestoreTempStoredCurrency()
	{
		if (playerData.TempGeoStore > 0)
		{
			ProcessAddCurrency(playerData.TempGeoStore, CurrencyType.Money);
			playerData.TempGeoStore = 0;
		}
		if (playerData.TempShellShardStore > 0)
		{
			ProcessAddCurrency(playerData.TempShellShardStore, CurrencyType.Shard);
			playerData.TempShellShardStore = 0;
		}
	}

	public static void AddCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		ChangeCurrency(amount, type, showCounter);
	}

	public static void TakeCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		ChangeCurrency(-amount, type, showCounter);
	}

	public static void ChangeCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		CurrencyQueue currencyQueue = CurrencyManager.currencyQueue[(int)type];
		currencyQueue.amount += amount;
		if (showCounter)
		{
			currencyQueue.showCounter = true;
		}
		if (hasInstance && !updateQueued && amount != 0)
		{
			ManagerSingleton<CurrencyManager>.UnsafeInstance.enabled = (updateQueued = true);
		}
	}

	private static void ProcessCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		if (amount > 0)
		{
			ProcessAddCurrency(amount, type, showCounter);
		}
		else
		{
			ProcessTakeCurrency(amount, type, showCounter);
		}
	}

	private static void ProcessAddCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		CurrencyCounter.RefreshStartCount(type);
		switch (type)
		{
		case CurrencyType.Money:
		{
			int geo = playerData.geo;
			playerData.AddGeo(amount);
			amount = playerData.geo - geo;
			break;
		}
		case CurrencyType.Shard:
		{
			int shellShards = playerData.ShellShards;
			playerData.AddShards(amount);
			amount = playerData.ShellShards - shellShards;
			break;
		}
		default:
			Debug.LogError($"Unknown currency type: {type}");
			break;
		}
		if (showCounter)
		{
			CurrencyCounter.Add(amount, type);
		}
	}

	public static void ProcessTakeCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		amount = Mathf.Abs(amount);
		CurrencyCounter.RefreshStartCount(type);
		switch (type)
		{
		case CurrencyType.Money:
			playerData.TakeGeo(amount);
			break;
		case CurrencyType.Shard:
			playerData.TakeShards(amount);
			break;
		default:
			Debug.LogError($"Unknown currency type: {type}");
			break;
		}
		if (showCounter)
		{
			CurrencyCounter.Take(amount, type);
		}
	}
}
