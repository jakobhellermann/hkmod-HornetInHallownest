using System;
using System.Collections.Generic;
using GlobalSettings;
using UnityEngine;

namespace Silksong;

public class NailAttackBase : MonoBehaviour
{
	[SerializeField]
	protected DamageEnemies enemyDamager;

	[SerializeField]
	private string imbuedSlashAnimName;

	[SerializeField]
	private tk2dSpriteAnimator imbuedSlashAnim;

	[SerializeField]
	private bool canHitSpikes = true;

	[SerializeField]
	private VibrationDataAsset vibration;

	[SerializeField]
	protected bool cancelVibrationOnExit;

	[SerializeField]
	private GameObject[] activateOnSlash;

	[Space]
	[SerializeField]
	private Vector3 scale = Vector3.one;

	[SerializeField]
	private bool overrideLongNeedleScale;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("overrideLongNeedleScale", true, false, false)]
	private Vector3 longNeedleScale = Vector3.one;

	protected HeroController hc;

	private tk2dSprite slashSprite;

	private MeshRenderer imbuedSlashMesh;

	private tk2dSprite imbuedSlashSprite;

	private bool isNailImbued;

	private NailImbuementConfig nailImbuement;

	private List<DamageEnemies> damagers;

	private VibrationEmission emission;

	protected PolygonCollider2D clashTinkPoly;

	protected GameObject ExtraDamager { get; private set; }

	public DamageEnemies EnemyDamager => enemyDamager;

	public bool CanHitSpikes => canHitSpikes;

	public bool IsDamagerActive { get; protected set; }

	public bool CanDamageEnemies { get; protected set; }

	public event Action AttackStarting;

	public event Action<bool> EndedDamage;

	protected virtual void Awake()
	{
		hc = GetComponentInParent<HeroController>();
		slashSprite = GetComponent<tk2dSprite>();
		if ((bool)imbuedSlashAnim)
		{
			imbuedSlashMesh = imbuedSlashAnim.GetComponent<MeshRenderer>();
			imbuedSlashMesh.enabled = false;
			imbuedSlashSprite = imbuedSlashAnim.GetComponent<tk2dSprite>();
		}
		Transform transform = base.transform.Find("Clash Tink");
		if ((bool)transform)
		{
			clashTinkPoly = transform.GetComponent<PolygonCollider2D>();
		}
		if ((bool)clashTinkPoly)
		{
			clashTinkPoly.enabled = false;
		}
		damagers = new List<DamageEnemies> { enemyDamager };
		Transform transform2 = base.transform.Find("Extra Damager");
		if ((bool)transform2)
		{
			ExtraDamager = transform2.gameObject;
			DamageEnemies component = ExtraDamager.GetComponent<DamageEnemies>();
			damagers.Add(component);
		}
		foreach (DamageEnemies damager in damagers)
		{
			if (!damager)
			{
				continue;
			}
			if (!damager.manualTrigger)
			{
				damager.EndedDamage += OnEndedDamage;
			}
			foreach (DamageEnemies damager2 in damagers)
			{
				if (!(damager == damager2))
				{
					DamageEnemies dmg = damager;
					damager2.WillDamageEnemyCollider += delegate(Collider2D col)
					{
						dmg.PreventDamage(col);
					};
				}
			}
		}
		CanDamageEnemies = true;
		IsDamagerActive = true;
		activateOnSlash.SetAllActive(value: false);
	}

	public virtual void OnSlashStarting()
	{
		NailImbuementConfig currentImbuement = hc.NailImbuement.CurrentImbuement;
		if (currentImbuement != null)
		{
			SetNailImbuement(currentImbuement, hc.NailImbuement.CurrentElement);
		}
		this.AttackStarting?.Invoke();
		if ((bool)ExtraDamager)
		{
			ExtraDamager.SetActive(value: false);
		}
		activateOnSlash.SetAllActive(value: true);
		PlayVibration();
		if (Gameplay.LongNeedleTool.IsEquipped)
		{
			if (overrideLongNeedleScale)
			{
				base.transform.localScale = longNeedleScale;
				return;
			}
			Vector2 longNeedleMultiplier = Gameplay.LongNeedleMultiplier;
			Vector2 vector = (hc.cState.upAttacking ? new Vector2(longNeedleMultiplier.y, longNeedleMultiplier.x) : longNeedleMultiplier);
			base.transform.localScale = new Vector3(scale.x * vector.x, scale.y * vector.y, scale.z);
		}
		else
		{
			base.transform.localScale = scale;
		}
	}

	public void SetLongNeedleHandled()
	{
		overrideLongNeedleScale = true;
		longNeedleScale = scale;
	}

	public void OnCancelAttack()
	{
		foreach (DamageEnemies damager in damagers)
		{
			if ((bool)damager)
			{
				damager.EndDamage();
				damager.NailElement = NailElements.None;
				damager.NailImbuement = null;
			}
		}
		OnAttackCancelled();
		if ((bool)imbuedSlashAnim)
		{
			imbuedSlashMesh.enabled = false;
		}
		if ((bool)slashSprite)
		{
			slashSprite.color = Color.white;
		}
		isNailImbued = false;
		nailImbuement = null;
		if ((bool)ExtraDamager)
		{
			ExtraDamager.SetActive(value: false);
		}
		if (cancelVibrationOnExit)
		{
			StopVibration();
		}
	}

	protected virtual void OnAttackCancelled()
	{
	}

	public void FullCancelAttack()
	{
		foreach (DamageEnemies damager in damagers)
		{
			if ((bool)damager && (bool)damager)
			{
				HealthManager.CancelAllLagHitsForSource(damager.gameObject);
			}
		}
	}

	public void OnPlaySlash()
	{
		foreach (DamageEnemies damager in damagers)
		{
			if (!damager)
			{
				return;
			}
			damager.CopyDamagePropsFrom(enemyDamager);
			damager.StartDamage();
		}
		if (!isNailImbued)
		{
			return;
		}
		GameObject slashEffect = nailImbuement.SlashEffect;
		if ((bool)slashEffect)
		{
			Transform transform = slashEffect.Spawn(base.transform, Vector3.zero, Quaternion.identity).transform;
			transform.localScale = Vector3.one;
			switch (DirectionUtils.GetCardinalDirection(enemyDamager.direction))
			{
			case 1:
				transform.localEulerAngles = new Vector3(0f, 0f, -90f);
				break;
			case 3:
				transform.localEulerAngles = new Vector3(0f, 0f, 45f);
				break;
			}
		}
		nailImbuement.ExtraSlashAudio.SpawnAndPlayOneShot(base.transform.position);
	}

	public void SetNailImbuement(NailImbuementConfig config, NailElements element)
	{
		isNailImbued = true;
		if ((bool)slashSprite)
		{
			slashSprite.color = config.NailTintColor;
		}
		if ((bool)imbuedSlashAnim)
		{
			imbuedSlashSprite.color = config.NailTintColor;
		}
		foreach (DamageEnemies damager in damagers)
		{
			if ((bool)damager)
			{
				damager.NailElement = element;
				damager.NailImbuement = config;
			}
		}
		nailImbuement = config;
	}

	private void OnEndedDamage(bool didHit)
	{
		IsDamagerActive = false;
		if (this.EndedDamage != null)
		{
			this.EndedDamage(didHit);
		}
		hc.SilkTauntEffectConsume();
	}

	public virtual void QueueBounce()
	{
	}

	protected void PlayVibration()
	{
		if ((bool)vibration)
		{
			emission = VibrationManager.PlayVibrationClipOneShot(vibration.VibrationData);
		}
	}

	protected void StopVibration()
	{
		if (emission != null)
		{
			emission.Stop();
			emission = null;
		}
	}
}
