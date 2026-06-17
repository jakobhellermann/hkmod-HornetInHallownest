using System;
using GlobalSettings;
using UnityEngine;

namespace Silksong;

public class SilkChunk : MonoBehaviour
{
	[SerializeField]
	private AudioSource regeneratingAudioLoop;

	[SerializeField]
	private AudioEvent regeneratedSound;

	[SerializeField]
	private AudioEvent takenCutSound;

	[Header("Regular Appear")]
	[SerializeField]
	private Color regularTint;

	[Header("Moss Appear")]
	[SerializeField]
	private Color mossTint;

	[SerializeField]
	private float mossTintFadeTime;

	[SerializeField]
	private GameObject mossAppearEffect;

	[SerializeField]
	private GameObject mossAppearEffectHalf;

	[SerializeField]
	private string[] mossAnims;

	[SerializeField]
	private AudioEvent[] mossAudio;

	[Header("Pilgrim Appear")]
	[SerializeField]
	private string[] pilgrimAnims;

	[Header("Acid Use")]
	[SerializeField]
	private string acidUseAnim;

	[SerializeField]
	private Color acidUseColor;

	[SerializeField]
	private PlayParticleEffects acidUseEffect;

	[SerializeField]
	private float acidFadeBackTime;

	[Space]
	[SerializeField]
	private Color maggotedColor;

	[SerializeField]
	[Range(0f, 1f)]
	private float bindableColourLerp = 0.3f;

	[SerializeField]
	private Color maggotUseColor;

	[SerializeField]
	private float maggotFadeTime;

	[SerializeField]
	private PlayParticleEffects maggotUseEffect;

	[Space]
	[SerializeField]
	private string voidUseAnim;

	[SerializeField]
	private PlayParticleEffects voidUseEffect;

	[SerializeField]
	private GameObject voidProtectEffect;

	[Header("Cursed")]
	[SerializeField]
	private string[] cursedUseAnims;

	[SerializeField]
	private PlayParticleEffects cursedBurstEffect;

	[Header("Silk Defeat Death")]
	[SerializeField]
	private string silkDefeatAnim;

	[Header("Take Effects")]
	[SerializeField]
	private PlayParticleEffects wispUseEffect;

	[SerializeField]
	private PlayParticleEffects drainEffect;

	private tk2dSpriteAnimator animator;

	private tk2dSprite sprite;

	private float removeCounter;

	private Coroutine colorFadeRoutine;

	private bool waitForAcidParticles;

	private bool isExtraChunk;

	private bool isGlowing;

	private bool isUsing;

	private bool isUsingAcid;

	private bool isMaggoted;

	private string upToGlowAnim;

	private string upAnim;

	private string downAnim;

	private string downAcidAnim;

	private string idleAnim;

	private string glowAnim;

	private int previousPartsAnimIndex;

	private Color fadeStartColor;

	private Color fadeTargetColor;

	private Action queuedUseStart;

	private HeroController hc;

	private GameCameras gc;

	public bool IsRegen { get; private set; }

	private Color BaseTint
	{
		get
		{
			if (!isMaggoted)
			{
				return regularTint;
			}
			return maggotedColor;
		}
	}

	private void Awake()
	{
		animator = GetComponent<tk2dSpriteAnimator>();
		sprite = GetComponent<tk2dSprite>();
		if ((bool)voidProtectEffect)
		{
			voidProtectEffect.SetActive(value: false);
		}
	}

	private void OnEnable()
	{
		hc = HeroController.SilentInstance;
		gc = GameCameras.SilentInstance;
		ResetAppearance();
		SetAnims();
	}

	private void OnDestroy()
	{
		gc = null;
		hc = null;
	}

	public void Update()
	{
		if (removeCounter > 0f)
		{
			removeCounter -= Time.deltaTime;
			if (removeCounter <= 0f)
			{
				base.gameObject.Recycle();
			}
		}
		else
		{
			if (isExtraChunk)
			{
				return;
			}
			if (isUsing || isUsingAcid)
			{
				if (queuedUseStart != null && !animator.IsPlaying(upAnim) && !animator.IsPlaying(upToGlowAnim))
				{
					queuedUseStart();
					queuedUseStart = null;
				}
			}
			else
			{
				if (IsRegen || animator.IsPlaying(upAnim) || animator.IsPlaying(upToGlowAnim))
				{
					return;
				}
				HeroController heroController = hc;
				if ((object)heroController != null && heroController.cState.isMaggoted)
				{
					if (!isMaggoted)
					{
						isMaggoted = true;
						FadeToCorrectColor();
					}
				}
				else if (isMaggoted)
				{
					isMaggoted = false;
					FadeToCorrectColor();
				}
			}
		}
	}

	private void FadeToCorrectColor()
	{
		FadeToColor(color: (!isMaggoted) ? regularTint : (isGlowing ? Color.Lerp(maggotedColor, regularTint, bindableColourLerp) : maggotedColor), delay: 0f, fadeTime: maggotFadeTime);
	}

	private void OnDisable()
	{
		StopAllCoroutines();
		ResetAppearance();
	}

	public void Add(bool glowing)
	{
		IsRegen = false;
		if (TryIsBigSilk())
		{
			isGlowing = true;
		}
		else if (glowing)
		{
			animator.Play(upToGlowAnim);
			isGlowing = true;
		}
		else
		{
			animator.Play(upAnim);
			isGlowing = false;
		}
		ResetAppearance();
	}

	private bool TryIsBigSilk()
	{
		if (PlayerData.instance.UnlockSilkFinalCutscene)
		{
			animator.PlayFromFrame(silkDefeatAnim, 0);
			return true;
		}
		return false;
	}

	private void ResetAppearance()
	{
		removeCounter = 0f;
		if ((bool)mossAppearEffect)
		{
			mossAppearEffect.SetActive(value: false);
		}
		if ((bool)mossAppearEffectHalf)
		{
			mossAppearEffectHalf.SetActive(value: false);
		}
		if ((bool)drainEffect)
		{
			drainEffect.StopParticleSystems();
		}
		previousPartsAnimIndex = -1;
		base.transform.localScale = new Vector3(1f, 1f, 1f);
		StopColorFade();
		isExtraChunk = false;
		isMaggoted = false;
		sprite.color = BaseTint;
		queuedUseStart = null;
	}

	public void SetUsing(SilkSpool.SilkUsingFlags usingFlags)
	{
		ResetAppearance();
		isUsing = true;
		if ((usingFlags & (SilkSpool.SilkUsingFlags.Normal | SilkSpool.SilkUsingFlags.Maggot)) == (SilkSpool.SilkUsingFlags.Normal | SilkSpool.SilkUsingFlags.Maggot))
		{
			animator.Play("SilkChunk Temp Use");
			sprite.color = maggotUseColor;
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Drain) != SilkSpool.SilkUsingFlags.None)
		{
			animator.Play("SilkChunk Temp Use");
			if ((bool)drainEffect)
			{
				drainEffect.PlayParticleSystems();
			}
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Normal) != SilkSpool.SilkUsingFlags.None)
		{
			animator.Play("SilkChunk Temp Use");
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Acid) != SilkSpool.SilkUsingFlags.None)
		{
			isUsingAcid = true;
			queuedUseStart = delegate
			{
				animator.Play(acidUseAnim);
				sprite.color = acidUseColor;
				if ((bool)acidUseEffect)
				{
					acidUseEffect.PlayParticleSystems();
				}
			};
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Maggot) != SilkSpool.SilkUsingFlags.None)
		{
			isUsingAcid = true;
			queuedUseStart = delegate
			{
				animator.Play(acidUseAnim);
				StopColorFade();
				sprite.color = maggotUseColor;
				if ((bool)maggotUseEffect)
				{
					maggotUseEffect.PlayParticleSystems();
				}
			};
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Void) != SilkSpool.SilkUsingFlags.None)
		{
			isUsingAcid = true;
			queuedUseStart = delegate
			{
				animator.Play(voidUseAnim);
				sprite.color = regularTint;
				if ((bool)voidUseEffect)
				{
					voidUseEffect.PlayParticleSystems();
				}
			};
		}
		else if ((usingFlags & SilkSpool.SilkUsingFlags.Curse) != SilkSpool.SilkUsingFlags.None)
		{
			animator.Play(cursedUseAnims[UnityEngine.Random.Range(0, cursedUseAnims.Length)]);
		}
	}

	public void SetRegen(bool isUpgraded)
	{
		animator.Play("SilkChunk Temp Get");
		IsRegen = true;
		if (gc.IsHudVisible)
		{
			regeneratingAudioLoop.Play();
		}
		ResetAppearance();
	}

	public void ResumeRegenAudioLoop()
	{
		if (IsRegen && base.gameObject.activeInHierarchy && gc.IsHudVisible)
		{
			regeneratingAudioLoop.Play();
		}
	}

	public void StopRegenAudioLoop()
	{
		regeneratingAudioLoop.Stop();
	}

	public void EndedRegen()
	{
		GameCameras instance = GameCameras.instance;
		if (!(instance != null) || instance.IsHudVisible)
		{
			regeneratedSound.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
		}
	}

	public void Remove(SilkSpool.SilkTakeSource takeSource)
	{
		switch (takeSource)
		{
		case SilkSpool.SilkTakeSource.Normal:
			animator.Play(isUsingAcid ? downAcidAnim : downAnim);
			removeCounter = 0.135f;
			break;
		case SilkSpool.SilkTakeSource.ActiveUse:
			animator.Play(isUsingAcid ? downAcidAnim : downAnim);
			removeCounter = 0.135f;
			if (gc.IsHudVisible)
			{
				regeneratedSound.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
			}
			break;
		case SilkSpool.SilkTakeSource.Wisp:
			animator.Play(downAnim);
			wispUseEffect.PlayParticleSystems();
			removeCounter = 0.85f;
			break;
		case SilkSpool.SilkTakeSource.Curse:
			animator.Play("SilkChunk Gone");
			cursedBurstEffect.PlayParticleSystems();
			removeCounter = 1.1f;
			break;
		case SilkSpool.SilkTakeSource.Drain:
			animator.Play(downAnim);
			removeCounter = 0.135f;
			if (gc.IsHudVisible)
			{
				takenCutSound.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
			}
			break;
		case SilkSpool.SilkTakeSource.Parts:
			animator.Play("SilkChunk Pilgrim Disperse");
			removeCounter = 0.15f;
			break;
		default:
			throw new ArgumentOutOfRangeException("takeSource", takeSource, null);
		}
	}

	public void StartGlow()
	{
		if (!TryIsBigSilk() && !isGlowing)
		{
			isGlowing = true;
			PlayIdle();
			FadeToCorrectColor();
		}
	}

	public void EndGlow()
	{
		if (!TryIsBigSilk() && isGlowing)
		{
			isGlowing = false;
			PlayIdle();
			FadeToCorrectColor();
		}
	}

	public void PlayIdle()
	{
		FadeToColor(0f, acidFadeBackTime, BaseTint);
		animator.Play(isGlowing ? glowAnim : idleAnim);
		if (!isUsing)
		{
			return;
		}
		isUsing = false;
		isUsingAcid = false;
		if ((bool)acidUseEffect)
		{
			acidUseEffect.StopParticleSystems();
		}
		if ((bool)maggotUseEffect)
		{
			maggotUseEffect.StopParticleSystems();
		}
		if ((bool)voidUseEffect && voidUseEffect.IsPlaying())
		{
			voidUseEffect.StopParticleSystems();
			if ((bool)voidProtectEffect && (bool)hc && hc.playerData.HasWhiteFlower)
			{
				voidProtectEffect.SetActive(value: true);
			}
		}
		if ((bool)drainEffect)
		{
			drainEffect.StopParticleSystems();
		}
	}

	private void SetAnims()
	{
		int num = UnityEngine.Random.Range(1, 90);
		if (num < 30)
		{
			upToGlowAnim = "SilkChunk UpToGlow1";
			upAnim = "SilkChunk Up1";
			downAnim = "SilkChunk Down1";
			downAcidAnim = "SilkChunk Down Acid1";
			idleAnim = "SilkChunk Idle 1";
			glowAnim = "SilkChunk Glow 1";
		}
		else if (num < 60)
		{
			upToGlowAnim = "SilkChunk UpToGlow2";
			upAnim = "SilkChunk Up2";
			downAnim = "SilkChunk Down2";
			downAcidAnim = "SilkChunk Down Acid2";
			idleAnim = "SilkChunk Idle 2";
			glowAnim = "SilkChunk Glow 2";
		}
		else
		{
			upToGlowAnim = "SilkChunk UpToGlow3";
			upAnim = "SilkChunk Up3";
			downAnim = "SilkChunk Down3";
			downAcidAnim = "SilkChunk Down Acid3";
			idleAnim = "SilkChunk Idle 3";
			glowAnim = "SilkChunk Glow 3";
		}
	}

	public void SetMossState(int index)
	{
		sprite.color = mossTint;
		if (index >= 0)
		{
			if (index < mossAnims.Length)
			{
				animator.Play(mossAnims[index]);
			}
			if (index < mossAudio.Length)
			{
				mossAudio[index].SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
			}
		}
		isExtraChunk = true;
		if ((bool)mossAppearEffectHalf)
		{
			mossAppearEffectHalf.SetActive(value: true);
		}
	}

	public void FinishMossState()
	{
		float delay = 0f;
		if (mossAnims.Length != 0)
		{
			delay = animator.PlayAnimGetTime(mossAnims[^1]);
		}
		if (mossAudio.Length != 0)
		{
			mossAudio[^1].SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, base.transform.position);
		}
		FadeToColor(delay, mossTintFadeTime, BaseTint);
		if ((bool)mossAppearEffect)
		{
			mossAppearEffect.SetActive(value: true);
		}
		isExtraChunk = false;
	}

	private void FadeToColor(float delay, float fadeTime, Color color)
	{
		StopColorFade();
		fadeStartColor = sprite.color;
		fadeTargetColor = color;
		if (!base.isActiveAndEnabled)
		{
			sprite.color = fadeTargetColor;
			return;
		}
		colorFadeRoutine = this.StartTimerRoutine(delay, fadeTime, delegate(float t)
		{
			sprite.color = Color.Lerp(fadeStartColor, fadeTargetColor, t);
		}, delegate
		{
			animator.Play(isGlowing ? glowAnim : idleAnim);
		});
	}

	public void SetPartsState(int index)
	{
		sprite.color = BaseTint;
		if (index != previousPartsAnimIndex && index >= 0 && index < pilgrimAnims.Length)
		{
			animator.Play(pilgrimAnims[index]);
			previousPartsAnimIndex = index;
			isExtraChunk = true;
		}
	}

	public void FinishPartsState()
	{
		int num = pilgrimAnims.Length;
		if (num > 0)
		{
			animator.Play(pilgrimAnims[num - 1]);
		}
		previousPartsAnimIndex = -1;
		isExtraChunk = false;
	}

	private void StopColorFade()
	{
		if (colorFadeRoutine != null)
		{
			StopCoroutine(colorFadeRoutine);
		}
	}
}
