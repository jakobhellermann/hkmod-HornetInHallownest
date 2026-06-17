using System;
using UnityEngine;

namespace Silksong;

public class Downspike : NailAttackBase
{
	[SerializeField]
	private string animName;

	[SerializeField]
	private float leftExtraDirection;

	[SerializeField]
	private float rightExtraDirection;

	[SerializeField]
	private bool waitForBounceTrigger;

	[Space]
	[SerializeField]
	private bool useKnockbackDamagers;

	[SerializeField]
	private DamageEnemies horizontalKnockbackDamager;

	[SerializeField]
	private DamageEnemies verticalKnockbackDamager;

	[SerializeField]
	private int knockbackDamagerActiveSteps = 3;

	private int currentKnockbackDamagerSteps;

	[SerializeField]
	private HeroBox heroBox;

	private HeroController heroCtrl;

	private tk2dSpriteAnimator anim;

	private MeshRenderer mesh;

	private float slashAngle;

	private bool queuedBounce;

	private bool bounceTriggerHit;

	private PolygonCollider2D poly;

	private int polyCounter;

	private AudioSource audio;

	protected override void Awake()
	{
		base.Awake();
		Transform root = base.transform.root;
		try
		{
			heroCtrl = root.GetComponent<HeroController>();
		}
		catch (NullReferenceException ex)
		{
			Debug.LogError("NailSlash: could not find HeroController on parent: " + root.name + " " + ex);
		}
		audio = GetComponent<AudioSource>();
		anim = GetComponent<tk2dSpriteAnimator>();
		poly = GetComponent<PolygonCollider2D>();
		mesh = GetComponent<MeshRenderer>();
		poly.enabled = false;
		mesh.enabled = false;
		horizontalKnockbackDamager.gameObject.SetActive(value: false);
		verticalKnockbackDamager.gameObject.SetActive(value: false);
		if ((bool)enemyDamager)
		{
			enemyDamager.WillDamageEnemyCollider += OnDamagingEnemy;
			enemyDamager.WillDamageEnemy += OnEnemyDamaged;
			enemyDamager.ParriedEnemy += OnEnemyParried;
		}
	}

	public void StartSlash()
	{
		enemyDamager.ExtraUpDirection = ((heroCtrl.transform.localScale.x < 0f) ? rightExtraDirection : leftExtraDirection);
		OnSlashStarting();
		audio.Play();
		PlayVibration();
		anim.PlayFromFrame(animName, 0);
		anim.AnimationEventTriggered = OnAnimationEventTriggered;
		mesh.enabled = true;
		if ((bool)clashTinkPoly)
		{
			clashTinkPoly.enabled = true;
		}
		base.CanDamageEnemies = true;
		base.IsDamagerActive = true;
		queuedBounce = false;
		bounceTriggerHit = false;
		heroBox.HeroBoxDownspike();
		OnPlaySlash();
		poly.enabled = true;
	}

	public void CancelAttack()
	{
		poly.enabled = false;
		mesh.enabled = false;
		if ((bool)clashTinkPoly)
		{
			clashTinkPoly.enabled = false;
		}
		heroBox.HeroBoxNormal();
		OnCancelAttack();
	}

	public void CancelAttackAndDamage()
	{
		base.IsDamagerActive = false;
		CancelAttack();
	}

	private void OnDamagingEnemy(Collider2D col)
	{
		if (enemyDamager.damageDealt > 0 || enemyDamager.useNailDamage)
		{
			horizontalKnockbackDamager.PreventDamage(col);
			verticalKnockbackDamager.PreventDamage(col);
		}
	}

	private void OnEnemyDamaged()
	{
		if ((enemyDamager.damageDealt > 0 || enemyDamager.useNailDamage) && useKnockbackDamagers)
		{
			currentKnockbackDamagerSteps = knockbackDamagerActiveSteps;
			horizontalKnockbackDamager.direction = ((heroCtrl.transform.localScale.x > 0f) ? 180 : 0);
			horizontalKnockbackDamager.gameObject.SetActive(value: true);
			verticalKnockbackDamager.gameObject.SetActive(value: true);
		}
	}

	private void OnEnemyParried()
	{
		base.CanDamageEnemies = false;
		horizontalKnockbackDamager.gameObject.SetActive(value: false);
		verticalKnockbackDamager.gameObject.SetActive(value: false);
	}

	protected override void OnAttackCancelled()
	{
		horizontalKnockbackDamager.gameObject.SetActive(value: false);
		verticalKnockbackDamager.gameObject.SetActive(value: false);
	}

	private void FixedUpdate()
	{
		if (currentKnockbackDamagerSteps > 0)
		{
			currentKnockbackDamagerSteps--;
			if (currentKnockbackDamagerSteps <= 0)
			{
				horizontalKnockbackDamager.gameObject.SetActive(value: false);
				verticalKnockbackDamager.gameObject.SetActive(value: false);
			}
		}
	}

	private void OnAnimationEventTriggered(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip, int frame)
	{
		base.IsDamagerActive = false;
		bounceTriggerHit = true;
		TryDownBounce();
	}

	private void TryDownBounce()
	{
		if (queuedBounce && (!waitForBounceTrigger || bounceTriggerHit) && heroCtrl.CanCustomRecoil())
		{
			heroCtrl.DownspikeBounce(harpoonRecoil: false);
		}
	}

	public override void QueueBounce()
	{
		base.QueueBounce();
		queuedBounce = true;
		TryDownBounce();
	}
}
