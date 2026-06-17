using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Silksong;

public static class Helper
{
	private static RaycastHit2D[] _rayHitStore;

	private static readonly RaycastHit2D BLANK_HIT = default(RaycastHit2D);

	private static ContactFilter2D LEGACY_FILTER = CreateLegacyFilter(-1);

	private static StringBuilder _tempStringBuilder;

	public static int GetCollidingLayerMaskForLayer(int layer)
	{
		int num = 0;
		for (int i = 0; i < 32; i++)
		{
			if (!Physics2D.GetIgnoreLayerCollision(layer, i))
			{
				num |= 1 << i;
			}
		}
		return num;
	}

	public static float GetReflectedAngle(float angle, bool reflectHorizontal, bool reflectVertical, bool disallowNegative = false)
	{
		if (reflectHorizontal)
		{
			angle = 180f - angle;
		}
		if (reflectVertical)
		{
			angle = 0f - angle;
		}
		while (angle > 360f)
		{
			angle -= 360f;
		}
		if (disallowNegative)
		{
			while (angle < 0f)
			{
				angle += 360f;
			}
		}
		else
		{
			while (angle < -360f)
			{
				angle += 360f;
			}
		}
		return angle;
	}

	public static Vector3 GetRandomVector3InRange(Vector3 min, Vector3 max)
	{
		float x = ((min.x != max.x) ? UnityEngine.Random.Range(min.x, max.x) : min.x);
		float y = ((min.y != max.y) ? UnityEngine.Random.Range(min.y, max.y) : min.y);
		float z = ((min.z != max.z) ? UnityEngine.Random.Range(min.z, max.z) : min.z);
		return new Vector3(x, y, z);
	}

	public static Vector2 GetRandomVector2InRange(Vector2 min, Vector2 max)
	{
		float x = ((min.x != max.x) ? UnityEngine.Random.Range(min.x, max.x) : min.x);
		float y = ((min.y != max.y) ? UnityEngine.Random.Range(min.y, max.y) : min.y);
		return new Vector2(x, y);
	}

	public static bool IsRayHittingNoTriggers(Vector2 origin, Vector2 direction, float length, int layerMask, Func<Collider2D, bool> predicate, out RaycastHit2D closestHit)
	{
		IsHittingNoTriggersPre();
		int hitCount = Physics2D.RaycastNonAlloc(origin, direction, _rayHitStore, length, layerMask);
		return IsHittingNoTriggersPost(predicate, out closestHit, hitCount);
	}

	public static bool IsLineHittingNoTriggers(Vector2 from, Vector2 to, int layerMask, Func<Collider2D, bool> predicate, out RaycastHit2D closestHit)
	{
		IsHittingNoTriggersPre();
		int hitCount = Physics2D.LinecastNonAlloc(from, to, _rayHitStore, layerMask);
		return IsHittingNoTriggersPost(predicate, out closestHit, hitCount);
	}

	private static void IsHittingNoTriggersPre()
	{
		if (_rayHitStore == null)
		{
			_rayHitStore = new RaycastHit2D[10];
		}
	}

	private static bool IsHittingNoTriggersPost(Func<Collider2D, bool> predicate, out RaycastHit2D closestHit, int hitCount)
	{
		bool flag = predicate == null;
		bool flag2 = false;
		closestHit = default(RaycastHit2D);
		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit2D raycastHit2D = _rayHitStore[i];
			Collider2D collider = raycastHit2D.collider;
			if (!collider.isTrigger && (flag || predicate(collider)))
			{
				if (!flag2 || raycastHit2D.distance < closestHit.distance)
				{
					closestHit = raycastHit2D;
				}
				flag2 = true;
			}
			_rayHitStore[i] = default(RaycastHit2D);
		}
		return flag2;
	}

	public static bool IsRayHittingNoTriggers(Vector2 origin, Vector2 direction, float length, int layerMask, out RaycastHit2D closestHit)
	{
		return IsRayHittingNoTriggers(origin, direction, length, layerMask, null, out closestHit);
	}

	public static bool IsRayHittingNoTriggers(Vector2 origin, Vector2 direction, float length, int layerMask)
	{
		RaycastHit2D closestHit;
		return IsRayHittingNoTriggers(origin, direction, length, layerMask, out closestHit);
	}

	public static RaycastHit2D Raycast2D(Vector2 origin, Vector2 direction, float distance)
	{
		IsHittingNoTriggersPre();
		if (Physics2D.RaycastNonAlloc(origin, direction, _rayHitStore, distance) == 0)
		{
			return BLANK_HIT;
		}
		return _rayHitStore[0];
	}

	public static RaycastHit2D Raycast2D(Vector2 origin, Vector2 direction, float distance, int layerMask)
	{
		IsHittingNoTriggersPre();
		if (Physics2D.RaycastNonAlloc(origin, direction, _rayHitStore, distance, layerMask) == 0)
		{
			return BLANK_HIT;
		}
		return _rayHitStore[0];
	}

	public static bool Raycast2DHit(Vector2 origin, Vector2 direction, float distance, int layerMask, out RaycastHit2D hit)
	{
		IsHittingNoTriggersPre();
		if (Physics2D.RaycastNonAlloc(origin, direction, _rayHitStore, distance, layerMask) == 0)
		{
			hit = BLANK_HIT;
			return false;
		}
		hit = _rayHitStore[0];
		return true;
	}

	public static RaycastHit2D Raycast2D(Vector2 origin, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth)
	{
		IsHittingNoTriggersPre();
		if (Physics2D.RaycastNonAlloc(origin, direction, _rayHitStore, distance, layerMask, minDepth, maxDepth) == 0)
		{
			return BLANK_HIT;
		}
		return _rayHitStore[0];
	}

	public static ContactFilter2D CreateLegacyFilter(int layerMask, float minDepth, float maxDepth)
	{
		ContactFilter2D result = default(ContactFilter2D);
		result.useTriggers = Physics2D.queriesHitTriggers;
		result.SetLayerMask(layerMask);
		result.SetDepth(minDepth, maxDepth);
		return result;
	}

	public static ContactFilter2D CreateLegacyFilter(int layerMask)
	{
		ContactFilter2D result = default(ContactFilter2D);
		result.useTriggers = Physics2D.queriesHitTriggers;
		result.SetLayerMask(layerMask);
		return result;
	}

	public static RaycastHit2D LineCast2D(Vector2 start, Vector2 end, int layerMask)
	{
		IsHittingNoTriggersPre();
		ContactFilter2D lEGACY_FILTER = LEGACY_FILTER;
		lEGACY_FILTER.SetLayerMask(layerMask);
		if (Physics2D.Linecast(start, end, lEGACY_FILTER, _rayHitStore) > 0)
		{
			return _rayHitStore[0];
		}
		return BLANK_HIT;
	}

	public static bool LineCast2DHit(Vector2 start, Vector2 end, int layerMask, out RaycastHit2D hit)
	{
		IsHittingNoTriggersPre();
		ContactFilter2D lEGACY_FILTER = LEGACY_FILTER;
		lEGACY_FILTER.SetLayerMask(layerMask);
		if (Physics2D.Linecast(start, end, lEGACY_FILTER, _rayHitStore) > 0)
		{
			hit = _rayHitStore[0];
			return true;
		}
		hit = BLANK_HIT;
		return false;
	}

	public static string CombinePaths(string path1, params string[] paths)
	{
		if (path1 == null)
		{
			throw new ArgumentNullException("path1");
		}
		if (paths == null)
		{
			throw new ArgumentNullException("paths");
		}
		return paths.Aggregate(path1, (string acc, string p) => Path.Combine(acc, p));
	}

	public static bool FileOrFolderExists(string path)
	{
		if (!File.Exists(path))
		{
			return Directory.Exists(path);
		}
		return true;
	}

	public static void DeleteFileOrFolder(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.Directory) == FileAttributes.Directory)
		{
			Directory.Delete(path, recursive: true);
		}
		else
		{
			File.Delete(path);
		}
	}

	public static void CopyFileOrFolder(string src, string dest)
	{
		if ((File.GetAttributes(src) & FileAttributes.Directory) == FileAttributes.Directory)
		{
			DirectoryInfo source = new DirectoryInfo(src);
			DirectoryInfo target = (Directory.Exists(dest) ? new DirectoryInfo(dest) : Directory.CreateDirectory(dest));
			DeepCopy(source, target);
		}
		else
		{
			File.Copy(src, dest);
		}
	}

	public static void DeepCopy(DirectoryInfo source, DirectoryInfo target)
	{
		DirectoryInfo[] directories = source.GetDirectories();
		foreach (DirectoryInfo directoryInfo in directories)
		{
			DeepCopy(directoryInfo, target.CreateSubdirectory(directoryInfo.Name));
		}
		FileInfo[] files = source.GetFiles();
		foreach (FileInfo fileInfo in files)
		{
			fileInfo.CopyTo(Path.Combine(target.FullName, fileInfo.Name));
		}
	}

	public static void MoveFileOrFolder(string src, string dest)
	{
		if ((File.GetAttributes(src) & FileAttributes.Directory) == FileAttributes.Directory)
		{
			Directory.Move(src, dest);
		}
		else
		{
			File.Copy(src, dest);
		}
	}

	public static bool CheckMatchingSearchFilter(string text, string filter)
	{
		text = text.ToLower();
		filter = filter.ToLower().Replace('_', ' ');
		return filter.Split(' ').All((string f) => text.Contains(f));
	}

	public static string ParseSearchString(string original)
	{
		if (string.IsNullOrEmpty(original))
		{
			return null;
		}
		return original.Trim().ToLower().Replace(" ", "");
	}

	public static float LinearToDecibel(float sliderValue)
	{
		return LinearToDecibelExponential(sliderValue);
	}

	public static float DecibelToLinear(float dB)
	{
		return DecibelToLinearExponential(dB);
	}

	private static float LinearToDecibelLog(float sliderValue)
	{
		sliderValue = Mathf.Clamp(sliderValue, 0.0001f, 1.2f);
		if (sliderValue > 1f)
		{
			return Mathf.Clamp((sliderValue - 1f) * 100f, 0f, 20f);
		}
		return Mathf.Clamp(Mathf.Log10(sliderValue) * 20f, -80f, 0f);
	}

	private static float DecibelToLinearLog(float dB)
	{
		if (dB > 0f)
		{
			return Mathf.Clamp(1f + dB / 100f, 1f, 1.2f);
		}
		return Mathf.Clamp01(Mathf.Pow(10f, dB / 20f));
	}

	public static float LinearToDecibelExponential(float sliderValue)
	{
		sliderValue = Mathf.Clamp(sliderValue, 0.0001f, 1.2f);
		if (sliderValue > 1f)
		{
			sliderValue -= 1f;
			return sliderValue * 100f;
		}
		return Mathf.Lerp(-80f, 0f, Mathf.Sqrt(sliderValue));
	}

	public static float DecibelToLinearExponential(float dB)
	{
		if (dB > 0f)
		{
			return Mathf.Clamp(1f + dB / 100f, 1f, 1.2f);
		}
		return Mathf.Clamp01(Mathf.Pow((dB + 80f) / 80f, 2f));
	}

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	public static void RecordUndoChanges(this UnityEngine.Object obj, string name = "Undo")
	{
	}

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	public static void RegisterCreatedObjectUndo(this UnityEngine.Object obj, string name = "Undo")
	{
	}

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	public static void ApplyPrefabInstanceModifications(this UnityEngine.Object obj)
	{
	}

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	public static void SetAssetDirty(this UnityEngine.Object obj)
	{
	}

	public static Color SetAlpha(this Color color, float alpha)
	{
		color.a = alpha;
		return color;
	}

	public static int GetClosestOffsetToIndex(int targetIndex, int currentIndex, int arrayLength)
	{
		if (arrayLength == 0)
		{
			UnityEngine.Debug.LogError("Array length cannot be zero.");
			return 0;
		}
		int num = (targetIndex - currentIndex + arrayLength) % arrayLength;
		int num2 = (currentIndex - targetIndex + arrayLength) % arrayLength;
		if (num >= num2)
		{
			return -num2;
		}
		return num;
	}

	public static int GetContentHash<T>(this ICollection<T> collection)
	{
		if (collection == null)
		{
			return 0;
		}
		int num = 17;
		foreach (T item in collection)
		{
			num = num * 31 + (item?.GetHashCode() ?? 0);
		}
		return num;
	}

	public static int GetContentHash<T>(this List<T> collection)
	{
		if (collection == null)
		{
			return 0;
		}
		int num = 17;
		for (int i = 0; i < collection.Count; i++)
		{
			num = num * 31 + (collection[i]?.GetHashCode() ?? 0);
		}
		return num;
	}

	public static int GetContentHash<T>(this T[] collection)
	{
		if (collection == null)
		{
			return 0;
		}
		int num = 17;
		for (int i = 0; i < collection.Length; i++)
		{
			T val = collection[i];
			num = num * 31 + (val?.GetHashCode() ?? 0);
		}
		return num;
	}

	public static T[] SafeCastToArray<T>(this object[] source) where T : class
	{
		if (source == null)
		{
			return null;
		}
		List<T> list = new List<T>(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			if (source[i] is T item)
			{
				list.Add(item);
			}
		}
		return list.ToArray();
	}

	public static StringBuilder GetTempStringBuilder()
	{
		return GetTempStringBuilder(string.Empty);
	}

	public static StringBuilder GetTempStringBuilder(string initialString)
	{
		if (_tempStringBuilder == null)
		{
			_tempStringBuilder = new StringBuilder(initialString);
		}
		else
		{
			_tempStringBuilder.Clear();
			_tempStringBuilder.Append(initialString);
		}
		return _tempStringBuilder;
	}

	public static double Max(double a, double b)
	{
		if (!(a > b))
		{
			return b;
		}
		return a;
	}
}
