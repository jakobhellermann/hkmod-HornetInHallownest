using System.Collections.Generic;
using GlobalSettings;
using JetBrains.Annotations;
using UnityEngine;

namespace Silksong;

[CreateAssetMenu(menuName = "Hornet/Damage Tag")]
public class DamageTag : ScriptableObject
{
	public enum SpecialDamageTypes
	{
		[UsedImplicitly]
		None,
		Frost,
		Lightning
	}

	public struct DamageTagInstance
	{
		public int amount;

		public SpecialDamageTypes specialDamageType;

		public bool isHeroDamage;

		public NailElements nailElements;
	}

	[SerializeField]
	private int damageAmount;

	[SerializeField]
	private SpecialDamageTypes specialDamageType;

	[SerializeField]
	private NailElements nailElement;

	[SerializeField]
	private bool isToolDamage;

	[SerializeField]
	private float startDelay;

	[SerializeField]
	private float delayPerHit;

	[SerializeField]
	private int totalHitLimit;

	[SerializeField]
	private TimerGroup damageCooldownTimer;

	[Space]
	[SerializeField]
	private GameObject startEffect;

	[SerializeField]
	private GameObject tagLoopEffect;

	[SerializeField]
	private GameObject spawnHitEffect;

	[SerializeField]
	private AudioEvent hitSound;

	[SerializeField]
	private List<GameObject> deathBurstEffects = new List<GameObject>();

	[SerializeField]
	private bool doFlash;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("doFlash", true, false, false)]
	private SpriteFlash.FlashConfig flashConfig;

	[Space]
	[SerializeField]
	private ParticleEffectsLerpEmission corpseBurnEffect;

	public float StartDelay => startDelay;

	public float DelayPerHit => delayPerHit;

	public int TotalHitLimit => totalHitLimit;

	public TimerGroup DamageCooldownTimer => damageCooldownTimer;

	public ParticleEffectsLerpEmission CorpseBurnEffect => corpseBurnEffect;

	public SpecialDamageTypes SpecialDamageType => specialDamageType;

	public NailElements NailElement => nailElement;

	public void OnBegin(ITagDamageTakerOwner owner, out GameObject spawnedLoopEffect)
	{
		Vector2 tagDamageEffectPos = owner.TagDamageEffectPos;
		Vector2 original = owner.transform.TransformPoint(tagDamageEffectPos);
		if ((bool)startEffect)
		{
			startEffect.Spawn(original.ToVector3(startEffect.transform.localPosition.z));
		}
		if ((bool)tagLoopEffect)
		{
			spawnedLoopEffect = tagLoopEffect.Spawn(owner.transform, tagDamageEffectPos);
			spawnedLoopEffect.transform.SetPositionZ(tagLoopEffect.transform.localPosition.z);
			FollowTransform component = spawnedLoopEffect.GetComponent<FollowTransform>();
			if ((bool)component && component.Target != null)
			{
				component.Target = null;
			}
			ParticleEffectsLerpEmission component2 = spawnedLoopEffect.GetComponent<ParticleEffectsLerpEmission>();
			if ((bool)component2)
			{
				float duration = StartDelay + DelayPerHit * (float)TotalHitLimit;
				component2.Play(duration);
			}
		}
		else
		{
			spawnedLoopEffect = null;
		}
	}

	public void OnHit(ITagDamageTakerOwner owner)
	{
		int num = damageAmount;
		if (isToolDamage)
		{
			float num2 = num;
			float num3 = 1f + (float)PlayerData.instance.ToolKitUpgrades * Gameplay.ToolKitDamageIncrease;
			num = Mathf.RoundToInt(num2 * num3);
		}
		DamageTagInstance damageTagInstance = new DamageTagInstance
		{
			amount = num,
			specialDamageType = specialDamageType,
			isHeroDamage = isToolDamage,
			nailElements = nailElement
		};
		if (!owner.ApplyTagDamage(damageTagInstance))
		{
			return;
		}
		if (doFlash)
		{
			SpriteFlash spriteFlash = owner.SpriteFlash;
			if ((bool)spriteFlash)
			{
				spriteFlash.Flash(flashConfig);
			}
		}
		Vector2 tagDamageEffectPos = owner.TagDamageEffectPos;
		Vector3 position = (Vector2)owner.transform.TransformPoint(tagDamageEffectPos);
		if ((bool)spawnHitEffect)
		{
			position.z = spawnHitEffect.transform.localPosition.z;
			spawnHitEffect.Spawn(position);
		}
		hitSound.SpawnAndPlayOneShot(position);
	}

	public void SpawnDeathEffects(Vector3 spawnPosition)
	{
		deathBurstEffects.RemoveAll((GameObject o) => o == null);
		foreach (GameObject deathBurstEffect in deathBurstEffects)
		{
			deathBurstEffect.Spawn(spawnPosition);
		}
	}
}
