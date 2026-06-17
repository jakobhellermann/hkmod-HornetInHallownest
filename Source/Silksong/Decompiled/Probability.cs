using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Silksong;

public static class Probability
{
	[Serializable]
	public abstract class ProbabilityBase<T>
	{
		[Tooltip("If probability = 0, it will be considered 1.")]
		[FormerlySerializedAs("probability")]
		public float Probability = 1f;

		public abstract T Item { get; }
	}

	[Serializable]
	public class ProbabilityGameObject : ProbabilityBase<GameObject>
	{
		[FormerlySerializedAs("prefab")]
		public GameObject Prefab;

		public override GameObject Item => Prefab;
	}

	[Serializable]
	public class ProbabilityInt : ProbabilityBase<int>
	{
		public int Value;

		public override int Item => Value;
	}

	private struct SortedPair
	{
		public float Probability;

		public int Index;
	}

	private class ProbabilityBoxed : ProbabilityBase<object>
	{
		public override object Item { get; }

		public ProbabilityBoxed(object value, float probability)
		{
			Item = value;
			Probability = probability;
		}
	}

	private const int TEMP_ARRAY_START_CAPACITY = 100;

	private static readonly List<float> _currentProbabilities = new List<float>(100);

	private static readonly List<SortedPair> _currentPairs = new List<SortedPair>(100);

	private static readonly List<KeyValuePair<ProbabilityBoxed, SortedPair>> _currentKvps = new List<KeyValuePair<ProbabilityBoxed, SortedPair>>(100);

	public static TVal GetRandomItemByProbability<TProb, TVal>(TProb[] array, float[] overrideProbabilities = null) where TProb : ProbabilityBase<TVal>
	{
		int chosenIndex;
		return GetRandomItemByProbability<TProb, TVal>(array, out chosenIndex, overrideProbabilities);
	}

	public static TVal GetRandomItemByProbability<TProb, TVal>(TProb[] array, out int chosenIndex, float[] overrideProbabilities = null, IReadOnlyList<bool> conditions = null) where TProb : ProbabilityBase<TVal>
	{
		ProbabilityBase<TVal> randomItemRootByProbability = GetRandomItemRootByProbability<TProb, TVal>(array, out chosenIndex, overrideProbabilities, conditions);
		if (randomItemRootByProbability == null)
		{
			return default(TVal);
		}
		return randomItemRootByProbability.Item;
	}

	public static ProbabilityBase<TVal> GetRandomItemRootByProbability<TProb, TVal>(TProb[] array, float[] overrideProbabilities = null) where TProb : ProbabilityBase<TVal>
	{
		int chosenIndex;
		return GetRandomItemRootByProbability<TProb, TVal>(array, out chosenIndex, overrideProbabilities);
	}

	private static ProbabilityBase<TVal> GetRandomItemRootByProbability<TProb, TVal>(IReadOnlyList<TProb> array, out int chosenIndex, float[] overrideProbabilities = null, IReadOnlyList<bool> conditions = null) where TProb : ProbabilityBase<TVal>
	{
		int index = -1;
		int count = array.Count;
		if (count <= 1)
		{
			if (count == 1)
			{
				chosenIndex = 0;
				return array[0];
			}
			chosenIndex = -1;
			return null;
		}
		int count2 = array.Count;
		if (overrideProbabilities != null)
		{
			foreach (float item in overrideProbabilities)
			{
				_currentProbabilities.Add(item);
			}
			for (int j = _currentProbabilities.Count; j < count2; j++)
			{
				_currentProbabilities.Add(array[j].Probability);
			}
		}
		else
		{
			foreach (TProb item2 in array)
			{
				_currentProbabilities.Add(item2.Probability);
			}
		}
		if (_currentPairs.Capacity < count2)
		{
			_currentPairs.Capacity = count2;
		}
		for (int k = 0; k < count2; k++)
		{
			if (conditions == null || k >= conditions.Count || conditions[k])
			{
				_currentPairs.Add(new SortedPair
				{
					Probability = _currentProbabilities[k],
					Index = k
				});
			}
		}
		count2 = _currentPairs.Count;
		if (count2 == 0)
		{
			chosenIndex = -1;
			return null;
		}
		for (int l = 0; l < count2; l++)
		{
			TProb val = array[l];
			ProbabilityBoxed key = new ProbabilityBoxed(val.Item, val.Probability);
			_currentKvps.Add(new KeyValuePair<ProbabilityBoxed, SortedPair>(key, _currentPairs[l]));
		}
		_currentKvps.Sort((KeyValuePair<ProbabilityBoxed, SortedPair> x, KeyValuePair<ProbabilityBoxed, SortedPair> y) => x.Value.Probability.CompareTo(y.Value.Probability));
		float num = 0f;
		foreach (KeyValuePair<ProbabilityBoxed, SortedPair> currentKvp in _currentKvps)
		{
			num += ((Math.Abs(currentKvp.Value.Probability) > 0.0001f) ? currentKvp.Value.Probability : 1f);
		}
		float num2 = UnityEngine.Random.Range(0f, num);
		float num3 = 0f;
		for (int num4 = 0; num4 < count2; num4++)
		{
			if (num2 >= num3)
			{
				index = num4;
			}
			num3 += _currentKvps[num4].Value.Probability;
		}
		chosenIndex = _currentKvps[index].Value.Index;
		_currentProbabilities.Clear();
		_currentPairs.Clear();
		_currentKvps.Clear();
		return array[chosenIndex];
	}

	public static GameObject GetRandomGameObjectByProbability(ProbabilityGameObject[] array)
	{
		return GetRandomItemByProbability<ProbabilityGameObject, GameObject>(array);
	}

	public static TVal GetRandomItemByProbabilityFair<TProb, TVal>(TProb[] items, ref float[] storeProbabilities, float multiplier = 2f) where TProb : ProbabilityBase<TVal>
	{
		int chosenIndex;
		return GetRandomItemByProbabilityFair<TProb, TVal>(items, out chosenIndex, ref storeProbabilities, multiplier);
	}

	public static TVal GetRandomItemByProbabilityFair<TProb, TVal>(TProb[] items, out int chosenIndex, ref float[] storeProbabilities, float multiplier = 2f, IReadOnlyList<bool> conditions = null) where TProb : ProbabilityBase<TVal>
	{
		if (storeProbabilities == null || storeProbabilities.Length != items.Length)
		{
			storeProbabilities = new float[items.Length];
			for (int i = 0; i < items.Length; i++)
			{
				storeProbabilities[i] = items[i].Probability;
			}
		}
		TVal randomItemByProbability = GetRandomItemByProbability<TProb, TVal>(items, out chosenIndex, storeProbabilities, conditions);
		for (int j = 0; j < storeProbabilities.Length; j++)
		{
			storeProbabilities[j] = ((j == chosenIndex) ? items[j].Probability : (storeProbabilities[j] * multiplier));
		}
		return randomItemByProbability;
	}
}
