using System;
using System.Collections;
using System.Collections.Generic;
using GlobalEnums;
using TeamCherry.NestedFadeGroup;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

public class StatusVignette : MonoBehaviour
{
	private enum Vignettes
	{
		Maggot,
		Flame,
		Frost,
		FrostWater,
		Fury,
		Void,
		LavaBell
	}

	public enum StatusTypes
	{
		InMaggotRegion,
		Maggoted,
		InFrostWater,
		InRageMode,
		Voided,
		InCoalRegion
	}

	public enum TempStatusTypes
	{
		FlameDamage,
		FlameDamageLavaBell
	}

	[Serializable]
	private class FadeParams
	{
		public float AppearTargetAlpha = 1f;

		public AnimationCurve AppearCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		public float AppearDuration;

		public MinMaxFloat LoopAlphaRange = new MinMaxFloat(0f, 1f);

		public AnimationCurve LoopCurve = AnimationCurve.Constant(0f, 1f, 1f);

		public AnimationCurve LoopFadeCurve = AnimationCurve.Constant(0f, 1f, 1f);

		public float LoopDuration;

		public AnimationCurve DisappearCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		public float DisappearDuration;

		public AudioSource AudioSource;

		public AudioClip EnterClip;

		public AudioClip ExitClip;
	}

	[Serializable]
	private class TempFadeParams
	{
		public float TargetAlpha = 1f;

		public AnimationCurve Curve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

		public float Duration;
	}

	[SerializeField]
	[ArrayForEnum(typeof(Vignettes))]
	private Animator[] vignettes;

	[SerializeField]
	[ArrayForEnum(typeof(StatusTypes))]
	private FadeParams[] statuses;

	[SerializeField]
	[ArrayForEnum(typeof(TempStatusTypes))]
	private TempFadeParams[] tempStatuses;

	[SerializeField]
	private float frostVignetteLerpSpeed;

	[SerializeField]
	private NestedFadeGroupBase frostVignetteFadeGroup;

	private Coroutine[] fadeRoutines;

	private FadeParams[] previousFadeParams;

	private List<StatusTypes>[] currentStatuses;

	private Coroutine[] tempFadeRoutines;

	private bool[] currentTempStatuses;

	private float[,] mixTValues;

	private float initialYScale;

	private float frostVignetteTargetValue;

	private GameManager gm;

	private bool isSuppressed;

	private static readonly int _playbackTimeProp = Animator.StringToHash("Playback Time");

	private static StatusVignette _instance;

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref vignettes, typeof(Vignettes));
		ArrayForEnumAttribute.EnsureArraySize(ref statuses, typeof(StatusTypes));
		ArrayForEnumAttribute.EnsureArraySize(ref tempStatuses, typeof(TempStatusTypes));
	}

	private void Awake()
	{
		OnValidate();
		if (!_instance)
		{
			_instance = this;
			fadeRoutines = new Coroutine[vignettes.Length];
			previousFadeParams = new FadeParams[vignettes.Length];
			currentStatuses = new List<StatusTypes>[vignettes.Length];
			for (int i = 0; i < vignettes.Length; i++)
			{
				currentStatuses[i] = new List<StatusTypes>();
				vignettes[i].gameObject.SetActive(value: false);
			}
			currentTempStatuses = new bool[tempStatuses.Length];
			tempFadeRoutines = new Coroutine[tempStatuses.Length];
			mixTValues = new float[vignettes.Length, 2];
			initialYScale = base.transform.localScale.y;
		}
	}

	private void OnEnable()
	{
		ForceCameraAspect.MainCamHeightMultChanged += OnMainCamHeightMultChanged;
		OnMainCamHeightMultChanged(ForceCameraAspect.CurrentMainCamHeightMult);
		ForceCameraAspect.ViewportAspectChanged += OnCameraAspectChanged;
		OnCameraAspectChanged(ForceCameraAspect.CurrentViewportAspect);
		if (!gm)
		{
			gm = GameManager.instance;
			gm.GameStateChange += OnGameStateChanged;
		}
		Animator[] array = vignettes;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetFloat(_playbackTimeProp, 0f);
		}
		for (int j = 0; j < vignettes.Length; j++)
		{
			RefreshStatus((Vignettes)j);
		}
	}

	private void OnDisable()
	{
		ForceCameraAspect.MainCamHeightMultChanged -= OnMainCamHeightMultChanged;
		ForceCameraAspect.ViewportAspectChanged -= OnCameraAspectChanged;
		if ((bool)gm)
		{
			gm.GameStateChange -= OnGameStateChanged;
			gm = null;
		}
		for (int i = 0; i < fadeRoutines.Length; i++)
		{
			Coroutine coroutine = fadeRoutines[i];
			if (coroutine != null)
			{
				StopCoroutine(coroutine);
				fadeRoutines[i] = null;
			}
		}
	}

	private void OnDestroy()
	{
		if (_instance == this)
		{
			_instance = null;
		}
	}

	private void OnMainCamHeightMultChanged(float heightMult)
	{
		Transform obj = base.transform;
		Vector3 localScale = obj.localScale;
		localScale.y = initialYScale * heightMult;
		obj.localScale = localScale;
	}

	private void OnCameraAspectChanged(float aspect)
	{
		Transform obj = base.transform;
		Vector3 localScale = obj.localScale;
		localScale.x = localScale.y * aspect;
		obj.localScale = localScale;
	}

	public static void AddStatus(StatusTypes status)
	{
		if ((bool)_instance)
		{
			Vignettes vignetteForStatus = GetVignetteForStatus(status);
			if (_instance.currentStatuses[(int)vignetteForStatus].AddIfNotPresent(status))
			{
				_instance.RefreshStatus(vignetteForStatus);
			}
		}
	}

	public static void RemoveStatus(StatusTypes status)
	{
		if ((bool)_instance)
		{
			Vignettes vignetteForStatus = GetVignetteForStatus(status);
			if (_instance.currentStatuses[(int)vignetteForStatus].Remove(status))
			{
				_instance.RefreshStatus(vignetteForStatus);
			}
		}
	}

	public static void AddTempStatus(TempStatusTypes status)
	{
		if ((bool)_instance)
		{
			if (_instance.currentTempStatuses[(int)status])
			{
				_instance.StopCoroutine(_instance.tempFadeRoutines[(int)status]);
			}
			else
			{
				_instance.currentTempStatuses[(int)status] = true;
			}
			_instance.tempFadeRoutines[(int)status] = _instance.StartCoroutine(_instance.TempFadeRoutine(status));
		}
	}

	private static Vignettes GetVignetteForStatus(StatusTypes statusType)
	{
		switch (statusType)
		{
		case StatusTypes.InMaggotRegion:
		case StatusTypes.Maggoted:
			return Vignettes.Maggot;
		case StatusTypes.InFrostWater:
			return Vignettes.FrostWater;
		case StatusTypes.InRageMode:
			return Vignettes.Fury;
		case StatusTypes.Voided:
			return Vignettes.Void;
		case StatusTypes.InCoalRegion:
			return Vignettes.Flame;
		default:
			throw new ArgumentOutOfRangeException("statusType", statusType, null);
		}
	}

	private static Vignettes GetVignetteForTempStatus(TempStatusTypes statusType)
	{
		return statusType switch
		{
			TempStatusTypes.FlameDamage => Vignettes.Flame, 
			TempStatusTypes.FlameDamageLavaBell => Vignettes.LavaBell, 
			_ => throw new ArgumentOutOfRangeException("statusType", statusType, null), 
		};
	}

	private void OnGameStateChanged(GameState gameState)
	{
		bool flag = isSuppressed;
		bool flag2 = gameState == GameState.CUTSCENE;
		isSuppressed = flag2;
		if (isSuppressed != flag)
		{
			for (int i = 0; i < vignettes.Length; i++)
			{
				RefreshStatus((Vignettes)i);
			}
		}
	}

	private void RefreshStatus(Vignettes vignetteType)
	{
		Coroutine coroutine = fadeRoutines[(int)vignetteType];
		if (coroutine != null)
		{
			StopCoroutine(coroutine);
			fadeRoutines[(int)vignetteType] = null;
		}
		FadeParams fadeParams = previousFadeParams[(int)vignetteType];
		List<StatusTypes> list = currentStatuses[(int)vignetteType];
		if (!isSuppressed && list.Count > 0)
		{
			StatusTypes statusTypes = list[list.Count - 1];
			fadeParams = (previousFadeParams[(int)vignetteType] = statuses[(int)statusTypes]);
			fadeRoutines[(int)vignetteType] = StartCoroutine(FadeRoutine(isFadingUp: true, fadeParams, vignetteType));
		}
		else if (fadeParams != null)
		{
			fadeRoutines[(int)vignetteType] = StartCoroutine(FadeRoutine(isFadingUp: false, fadeParams, vignetteType));
		}
	}

	private IEnumerator FadeRoutine(bool isFadingUp, FadeParams fadeParams, Vignettes vignetteType)
	{
		int vignetteIndex = (int)vignetteType;
		Animator vignette = vignettes[vignetteIndex];
		float duration;
		AnimationCurve curve;
		float targetAlpha;
		if (isFadingUp)
		{
			duration = fadeParams.AppearDuration;
			curve = fadeParams.AppearCurve;
			targetAlpha = fadeParams.AppearTargetAlpha;
		}
		else
		{
			duration = fadeParams.DisappearDuration;
			curve = fadeParams.DisappearCurve;
			targetAlpha = 0f;
		}
		float startAlpha = (vignette.gameObject.activeInHierarchy ? vignette.GetFloat(_playbackTimeProp) : 0f);
		vignette.gameObject.SetActive(value: true);
		if ((bool)fadeParams.AudioSource)
		{
			fadeParams.AudioSource.Play();
			if ((bool)fadeParams.EnterClip)
			{
				fadeParams.AudioSource.PlayOneShot(fadeParams.EnterClip);
			}
		}
		for (float elapsed = 0f; elapsed <= duration; elapsed += Time.deltaTime)
		{
			float time = elapsed / duration;
			float t = curve.Evaluate(time);
			SetT(Mathf.LerpUnclamped(startAlpha, targetAlpha, t));
			yield return null;
		}
		SetT(Mathf.LerpUnclamped(startAlpha, targetAlpha, curve.Evaluate(1f)));
		if (isFadingUp)
		{
			float elapsedLoop = 0f;
			while (true)
			{
				float time2 = elapsedLoop % fadeParams.LoopDuration / fadeParams.LoopDuration;
				float t2 = fadeParams.LoopCurve.Evaluate(time2);
				float num = fadeParams.LoopFadeCurve.Evaluate(elapsedLoop / fadeParams.LoopDuration);
				SetT(fadeParams.LoopAlphaRange.GetLerpUnclampedValue(t2) * num);
				yield return null;
				elapsedLoop += Time.deltaTime;
			}
		}
		vignette.gameObject.SetActive(value: false);
		if ((bool)fadeParams.AudioSource)
		{
			fadeParams.AudioSource.Stop();
			if ((bool)fadeParams.ExitClip)
			{
				fadeParams.AudioSource.PlayOneShot(fadeParams.ExitClip);
			}
		}
		previousFadeParams[vignetteIndex] = null;
		fadeRoutines[vignetteIndex] = null;
		void SetT(float num2)
		{
			SetVignetteTValue(vignetteType, 0, num2);
			if ((bool)fadeParams.AudioSource)
			{
				fadeParams.AudioSource.volume = num2;
			}
		}
	}

	private IEnumerator TempFadeRoutine(TempStatusTypes statusType)
	{
		Vignettes vignetteType = GetVignetteForTempStatus(statusType);
		TempFadeParams fadeParams = tempStatuses[(int)statusType];
		for (float elapsed = 0f; elapsed <= fadeParams.Duration; elapsed += Time.unscaledDeltaTime)
		{
			float time = elapsed / fadeParams.Duration;
			float num = fadeParams.Curve.Evaluate(time);
			SetVignetteTValue(vignetteType, 1, num * fadeParams.TargetAlpha);
			yield return null;
		}
		SetVignetteTValue(vignetteType, 1, 0f);
		currentTempStatuses[(int)statusType] = false;
		tempFadeRoutines[(int)statusType] = null;
	}

	private void SetVignetteTValue(Vignettes vignetteType, int mixIndex, float value)
	{
		mixTValues[(int)vignetteType, mixIndex] = value;
		float num = 0f;
		for (int i = 0; i < 2; i++)
		{
			num = Mathf.Max(num, mixTValues[(int)vignetteType, i]);
		}
		Animator obj = vignettes[(int)vignetteType];
		obj.gameObject.SetActive(num > Mathf.Epsilon);
		obj.SetFloat(_playbackTimeProp, num);
	}

	public static void SetFrostVignetteAmount(float percentage)
	{
		if ((bool)_instance && !(Math.Abs(_instance.frostVignetteTargetValue - percentage) <= Mathf.Epsilon))
		{
			_instance.frostVignetteTargetValue = percentage;
			ref Coroutine reference = ref _instance.fadeRoutines[2];
			if (reference == null)
			{
				reference = _instance.StartCoroutine(_instance.FrostVignetteLerp());
			}
		}
	}

	private IEnumerator FrostVignetteLerp()
	{
		Animator vignette = _instance.vignettes[2];
		if (!vignette)
		{
			yield break;
		}
		float previousTargetValue = frostVignetteTargetValue;
		bool wasFadingOut = false;
		frostVignetteFadeGroup.AlphaSelf = 1f;
		vignette.SetFloat(_playbackTimeProp, 0f);
		while (true)
		{
			float num = frostVignetteTargetValue - previousTargetValue;
			bool flag = num < 0f || (wasFadingOut && num == 0f);
			float num2 = Mathf.Max(Mathf.Epsilon, 1E-05f);
			if (flag && frostVignetteFadeGroup.AlphaSelf <= num2)
			{
				break;
			}
			if (!vignette.gameObject.activeSelf)
			{
				vignette.gameObject.SetActive(value: true);
			}
			if (flag)
			{
				frostVignetteFadeGroup.AlphaSelf = Mathf.Lerp(frostVignetteFadeGroup.AlphaSelf, frostVignetteTargetValue, frostVignetteLerpSpeed * Time.unscaledDeltaTime);
			}
			else
			{
				frostVignetteFadeGroup.AlphaSelf = Mathf.Lerp(frostVignetteFadeGroup.AlphaSelf, 1f, frostVignetteLerpSpeed * Time.deltaTime);
				float value = Mathf.Lerp(vignette.GetFloat(_playbackTimeProp), frostVignetteTargetValue, frostVignetteLerpSpeed * Time.deltaTime);
				vignette.SetFloat(_playbackTimeProp, value);
			}
			previousTargetValue = frostVignetteTargetValue;
			wasFadingOut = flag;
			yield return null;
		}
		if (vignette.gameObject.activeSelf)
		{
			vignette.gameObject.SetActive(value: false);
		}
		_instance.fadeRoutines[2] = null;
	}
}
