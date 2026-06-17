using System;
using GlobalEnums;
using GlobalSettings;
using UnityEngine;

public class RunEffects : MonoBehaviour
{
	[Serializable]
	private enum RunTypes
	{
		Run,
		Sprint
	}

	[Serializable]
	private class EffectsWrapper
	{
		public ParticleSystem[] Particles;

		[NonSerialized]
		public Color[] InitialParticleColors;

		[NonSerialized]
		public bool recordedColors;

		[NonSerialized]
		public bool didChangeColors;

		public void Play()
		{
			ParticleSystem[] particles = Particles;
			foreach (ParticleSystem particleSystem in particles)
			{
				if (particleSystem != null)
				{
					particleSystem.Play(withChildren: true);
				}
			}
		}

		public void Stop(bool clear)
		{
			ParticleSystem[] particles = Particles;
			foreach (ParticleSystem particleSystem in particles)
			{
				if (particleSystem != null)
				{
					particleSystem.Stop(withChildren: true, (!clear) ? ParticleSystemStopBehavior.StopEmitting : ParticleSystemStopBehavior.StopEmittingAndClear);
				}
			}
		}

		public bool IsAlive()
		{
			ParticleSystem[] particles = Particles;
			foreach (ParticleSystem particleSystem in particles)
			{
				if (particleSystem != null && particleSystem.IsAlive(withChildren: true))
				{
					return true;
				}
			}
			return false;
		}

		public void RecordColors()
		{
			if (InitialParticleColors == null || !recordedColors)
			{
				InitialParticleColors = new Color[Particles.Length];
				for (int i = 0; i < InitialParticleColors.Length; i++)
				{
					InitialParticleColors[i] = Particles[i].main.startColor.color;
				}
				recordedColors = true;
			}
		}

		public void RevertColors()
		{
			if (!didChangeColors)
			{
				return;
			}
			didChangeColors = false;
			if (recordedColors)
			{
				for (int i = 0; i < Particles.Length; i++)
				{
					ParticleSystem.MainModule main = Particles[i].main;
					ParticleSystem.MinMaxGradient startColor = main.startColor;
					startColor.color = InitialParticleColors[i];
					main.startColor = startColor;
				}
			}
		}
	}

	[Serializable]
	private class RunEffectsWrapper
	{
		[ArrayForEnum(typeof(EnvironmentTypes))]
		public EffectsWrapper[] Effects;

		public ParticleSystem[] AllEffects;

		public ParticleSystem[] SprintmasterEffects;

		public RandomAudioClipTable SprintmasterEmitAudio;

		private bool started;

		public void StartEffect()
		{
			started = true;
		}

		public bool IsAlive()
		{
			if (!started)
			{
				return false;
			}
			EffectsWrapper[] effects = Effects;
			for (int i = 0; i < effects.Length; i++)
			{
				if (effects[i].IsAlive())
				{
					return true;
				}
			}
			ParticleSystem[] allEffects = AllEffects;
			foreach (ParticleSystem particleSystem in allEffects)
			{
				if (particleSystem != null && particleSystem.IsAlive(withChildren: true))
				{
					return true;
				}
			}
			allEffects = SprintmasterEffects;
			foreach (ParticleSystem particleSystem2 in allEffects)
			{
				if (particleSystem2 != null && particleSystem2.IsAlive(withChildren: true))
				{
					return true;
				}
			}
			return false;
		}
	}

	[SerializeField]
	[ArrayForEnum(typeof(RunTypes))]
	private RunEffectsWrapper[] runTypes;

	private EffectsWrapper currentEffects;

	private RunTypes currentRunType;

	private RunTypes? previousRunType;

	private bool isActive;

	private bool isHeroEffect;

	private bool isHeroSprintmasterEffect;

	private HeroController hc;

	private bool hasHC;

	private float sprintmasterEmitSoundDelayLeft;

	private bool isSprintMasterEffectActive;

	private bool hasChangedColor;

	private EnvironmentTypes previousEnvironmentType;

	private bool registeredSprintMaster;

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref runTypes, typeof(RunTypes));
		for (int i = 0; i < runTypes.Length; i++)
		{
			if (runTypes[i] == null)
			{
				runTypes[i] = new RunEffectsWrapper();
			}
			ArrayForEnumAttribute.EnsureArraySize(ref runTypes[i].Effects, typeof(EnvironmentTypes));
			for (int j = 0; j < runTypes[i].Effects.Length; j++)
			{
				if (runTypes[i].Effects[j] == null)
				{
					runTypes[i].Effects[j] = new EffectsWrapper();
				}
			}
		}
	}

	private void Awake()
	{
		OnValidate();
	}

	private void OnDisable()
	{
		Transform obj = base.transform;
		Vector3 localScale = obj.localScale;
		localScale.x = Mathf.Abs(localScale.x);
		obj.localScale = localScale;
		UnregisterSprintMasterEffect();
	}

	private void OnEnable()
	{
		isActive = true;
		RunEffectsWrapper[] array = runTypes;
		foreach (RunEffectsWrapper runEffectsWrapper in array)
		{
			EffectsWrapper[] effects = runEffectsWrapper.Effects;
			for (int j = 0; j < effects.Length; j++)
			{
				effects[j].Stop(clear: true);
			}
			ParticleSystem[] allEffects = runEffectsWrapper.AllEffects;
			for (int j = 0; j < allEffects.Length; j++)
			{
				allEffects[j].Stop(withChildren: true);
			}
			allEffects = runEffectsWrapper.SprintmasterEffects;
			for (int j = 0; j < allEffects.Length; j++)
			{
				allEffects[j].Stop(withChildren: true);
			}
		}
		currentEffects = null;
		previousRunType = null;
		sprintmasterEmitSoundDelayLeft = 0f;
	}

	private void Update()
	{
		if (!isHeroSprintmasterEffect || (isHeroEffect && hasHC && !hc.IsSprintMasterActive))
		{
			ParticleSystem[] sprintmasterEffects = runTypes[(int)currentRunType].SprintmasterEffects;
			foreach (ParticleSystem particleSystem in sprintmasterEffects)
			{
				if (particleSystem.isPlaying)
				{
					particleSystem.Stop();
				}
			}
			isSprintMasterEffectActive = false;
			sprintmasterEmitSoundDelayLeft = 0f;
		}
		if (isActive)
		{
			if (!isHeroSprintmasterEffect || !isHeroEffect || isSprintMasterEffectActive || !hasHC || !hc.IsSprintMasterActive)
			{
				return;
			}
			ParticleSystem[] sprintmasterEffects = runTypes[(int)currentRunType].SprintmasterEffects;
			foreach (ParticleSystem particleSystem2 in sprintmasterEffects)
			{
				if (!particleSystem2.isPlaying)
				{
					particleSystem2.Play();
				}
			}
			SprintmasterEmitSound();
			isSprintMasterEffectActive = true;
			return;
		}
		RunEffectsWrapper[] array = runTypes;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].IsAlive())
			{
				return;
			}
		}
		base.gameObject.Recycle();
	}

	private void RegisterSprintMasterEffect()
	{
		if (!registeredSprintMaster && hasHC && !(hc.AudioCtrl == null))
		{
			hc.AudioCtrl.OnPlayFootstep += SprintmasterEmitSound;
			registeredSprintMaster = true;
		}
	}

	private void UnregisterSprintMasterEffect()
	{
		if (registeredSprintMaster)
		{
			registeredSprintMaster = false;
			if (hc != null && hc.AudioCtrl != null)
			{
				hc.AudioCtrl.OnPlayFootstep -= SprintmasterEmitSound;
			}
		}
	}

	public void StartEffect(bool isHero, bool doSprintMasterEffect = false)
	{
		isHeroEffect = isHero;
		isHeroSprintmasterEffect = doSprintMasterEffect;
		hc = (isHeroEffect ? HeroController.instance : null);
		hasHC = hc;
		if (isHeroSprintmasterEffect)
		{
			RegisterSprintMasterEffect();
		}
		UpdateEffects();
	}

	public void UpdateEffects()
	{
		if (!isActive)
		{
			return;
		}
		EnviroRegionListener componentInParent = GetComponentInParent<EnviroRegionListener>();
		if (componentInParent == null)
		{
			return;
		}
		if (currentEffects != null)
		{
			currentEffects.Stop(clear: false);
		}
		if (hasHC && hc.cState.isBackScuttling)
		{
			currentRunType = RunTypes.Sprint;
			base.transform.FlipLocalScale(x: true);
		}
		else
		{
			currentRunType = (componentInParent.IsSprinting ? RunTypes.Sprint : RunTypes.Run);
		}
		RunEffectsWrapper runEffectsWrapper = runTypes[(int)currentRunType];
		runEffectsWrapper.StartEffect();
		if (!previousRunType.HasValue || currentRunType != previousRunType.Value)
		{
			if (previousRunType.HasValue)
			{
				ParticleSystem[] allEffects = runTypes[(int)previousRunType.Value].AllEffects;
				for (int i = 0; i < allEffects.Length; i++)
				{
					allEffects[i].Stop(withChildren: true);
				}
			}
			if (isHeroEffect)
			{
				ParticleSystem[] allEffects = runEffectsWrapper.AllEffects;
				for (int i = 0; i < allEffects.Length; i++)
				{
					allEffects[i].Play(withChildren: true);
				}
				if (Gameplay.SprintmasterTool.IsEquipped)
				{
					isSprintMasterEffectActive = true;
					allEffects = runEffectsWrapper.SprintmasterEffects;
					for (int i = 0; i < allEffects.Length; i++)
					{
						allEffects[i].Play(withChildren: true);
					}
					SprintmasterEmitSound();
				}
				else
				{
					sprintmasterEmitSoundDelayLeft = 0f;
				}
			}
			else
			{
				sprintmasterEmitSoundDelayLeft = 0f;
			}
			previousRunType = currentRunType;
		}
		EnvironmentTypes currentEnvironmentType = componentInParent.CurrentEnvironmentType;
		if (hasChangedColor && currentEnvironmentType != previousEnvironmentType)
		{
			RunEffectsWrapper[] array = runTypes;
			for (int i = 0; i < array.Length; i++)
			{
				EffectsWrapper[] effects = array[i].Effects;
				for (int j = 0; j < effects.Length; j++)
				{
					effects[j].RevertColors();
				}
			}
			hasChangedColor = false;
		}
		previousEnvironmentType = currentEnvironmentType;
		currentEffects = runEffectsWrapper.Effects[(int)currentEnvironmentType];
		if (currentEnvironmentType == EnvironmentTypes.Moss || currentEnvironmentType == EnvironmentTypes.ShallowWater || currentEnvironmentType == EnvironmentTypes.RunningWater || currentEnvironmentType == EnvironmentTypes.WetMetal || currentEnvironmentType == EnvironmentTypes.WetWood)
		{
			currentEffects.RecordColors();
			AreaEffectTint.IsActive(base.transform.position, out var tintColor);
			for (int k = 0; k < currentEffects.Particles.Length; k++)
			{
				ParticleSystem.MainModule main = currentEffects.Particles[k].main;
				ParticleSystem.MinMaxGradient startColor = main.startColor;
				startColor.color = currentEffects.InitialParticleColors[k] * tintColor;
				main.startColor = startColor;
			}
			hasChangedColor = true;
			currentEffects.didChangeColors = true;
		}
		currentEffects.Play();
	}

	private void SprintmasterEmitSound()
	{
		if (!hasHC || hc.IsSprintMasterActive)
		{
			RunEffectsWrapper runEffectsWrapper = runTypes[(int)currentRunType];
			if (runEffectsWrapper.SprintmasterEffects.Length != 0)
			{
				runEffectsWrapper.SprintmasterEmitAudio.SpawnAndPlayOneShot(base.transform.position);
			}
		}
	}

	public void Stop()
	{
		if (isActive)
		{
			if (currentEffects != null)
			{
				currentEffects.Stop(clear: false);
			}
			ParticleSystem[] allEffects = runTypes[(int)currentRunType].AllEffects;
			for (int i = 0; i < allEffects.Length; i++)
			{
				allEffects[i].Stop(withChildren: true);
			}
			allEffects = runTypes[(int)currentRunType].SprintmasterEffects;
			for (int i = 0; i < allEffects.Length; i++)
			{
				allEffects[i].Stop(withChildren: true);
			}
			isActive = false;
			sprintmasterEmitSoundDelayLeft = 0f;
			UnregisterSprintMasterEffect();
			base.transform.SetParent(null, worldPositionStays: true);
		}
	}
}
