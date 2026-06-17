using System.Linq;
using TeamCherry.NestedFadeGroup;
using TeamCherry.SharedUtils;
using UnityEngine;
using UnityEngine.Events;

namespace Silksong;

public class PlayParticleEffects : MonoBehaviour
{
	[SerializeField]
	private ParticleSystem[] particleEffects;

	[SerializeField]
	private bool useChildren;

	[SerializeField]
	private bool deparentOnPlay;

	[SerializeField]
	private bool useEmission;

	[SerializeField]
	private bool useCollider;

	[Space]
	[SerializeField]
	private NestedFadeGroupBase fadeGroup;

	[SerializeField]
	private float fadeInDuration;

	[SerializeField]
	private float fadeOutDuration;

	[Space]
	public UnityEvent OnPlay;

	public UnityEvent OnStop;

	private float clearTimeLeft;

	private void Awake()
	{
		if (useChildren)
		{
			particleEffects = (from ps in GetComponentsInChildren<ParticleSystem>()
				where !particleEffects.Contains(ps)
				select ps).Concat(particleEffects).ToArray();
		}
	}

	private void OnEnable()
	{
		ComponentSingleton<PlayParticleEffectsCallbackHooks>.Instance.OnUpdate += OnUpdate;
	}

	private void OnDisable()
	{
		ComponentSingleton<PlayParticleEffectsCallbackHooks>.Instance.OnUpdate -= OnUpdate;
	}

	private void OnUpdate()
	{
		if (clearTimeLeft > 0f)
		{
			clearTimeLeft -= Time.deltaTime;
			if (clearTimeLeft <= 0f)
			{
				ClearParticleSystems();
			}
		}
	}

	public void PlayParticleSystems()
	{
		if ((bool)fadeGroup)
		{
			fadeGroup.FadeTo(1f, fadeInDuration);
		}
		clearTimeLeft = 0f;
		ParticleSystem[] array = particleEffects;
		foreach (ParticleSystem particleSystem in array)
		{
			if (deparentOnPlay)
			{
				particleSystem.transform.SetParent(null, worldPositionStays: true);
			}
			if (useEmission)
			{
				ParticleSystem.EmissionModule emission = particleSystem.emission;
				emission.enabled = true;
			}
			else
			{
				particleSystem.Play();
			}
		}
		OnPlay.Invoke();
	}

	public void StopParticleSystems()
	{
		if ((bool)fadeGroup)
		{
			fadeGroup.FadeTo(0f, fadeOutDuration);
		}
		ParticleSystem[] array = particleEffects;
		foreach (ParticleSystem particleSystem in array)
		{
			if (useEmission)
			{
				ParticleSystem.EmissionModule emission = particleSystem.emission;
				emission.enabled = false;
			}
			else
			{
				particleSystem.Stop();
				clearTimeLeft = fadeOutDuration;
			}
		}
		OnStop.Invoke();
	}

	public void ClearParticleSystems()
	{
		ParticleSystem[] array = particleEffects;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Clear();
		}
	}

	private void OnTriggerEnter2D(Collider2D collision)
	{
		if (useCollider)
		{
			PlayParticleSystems();
		}
	}

	private void OnTriggerExit2D(Collider2D collision)
	{
		if (useCollider)
		{
			StopParticleSystems();
		}
	}

	public bool IsAlive()
	{
		ParticleSystem[] array = particleEffects;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].IsAlive(withChildren: true))
			{
				return true;
			}
		}
		return false;
	}

	public bool IsPlaying()
	{
		ParticleSystem[] array = particleEffects;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].isPlaying)
			{
				return true;
			}
		}
		return false;
	}
}
