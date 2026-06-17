using System.Collections.Generic;
using UnityEngine;

namespace Silksong;

public class TagDamageTaker : MonoBehaviour
{
	private bool ignoreColliderState;

	private readonly Dictionary<DamageTag, DamageTagInfo> taggedDamage = new Dictionary<DamageTag, DamageTagInfo>();

	private readonly Queue<DamageTag> taggedDamageRemove = new Queue<DamageTag>();

	private ITagDamageTakerOwner owner;

	private Collider2D collider;

	private bool isEnemy;

	public IReadOnlyDictionary<DamageTag, DamageTagInfo> TaggedDamage => taggedDamage;

	public bool IsTagged => taggedDamage.Count > 0;

	private void Awake()
	{
		collider = GetComponent<Collider2D>();
		isEnemy = base.gameObject.layer == 11;
	}

	public static TagDamageTaker Add(GameObject gameObject, ITagDamageTakerOwner newOwner)
	{
		TagDamageTaker tagDamageTaker = gameObject.AddComponent<TagDamageTaker>();
		tagDamageTaker.owner = newOwner;
		return tagDamageTaker;
	}

	public void AddDamageTagToStack(DamageTag damageTag, int hitAmountOverride = 0)
	{
		if (!damageTag)
		{
			Debug.LogError("DamageTag was null", this);
			return;
		}
		if (!collider)
		{
			collider = GetComponent<Collider2D>();
		}
		if (taggedDamage.TryGetValue(damageTag, out var value))
		{
			value.HitsLeft = ((hitAmountOverride > 0) ? Mathf.Max(value.HitsLeft, hitAmountOverride) : damageTag.TotalHitLimit);
			if (value.RemoveAfterNextHit)
			{
				value.RemoveAfterNextHit = false;
				damageTag.OnHit(owner);
				value.Stacked--;
			}
			if (value.hasLerpEmission)
			{
				value.RefreshEmission(Mathf.Max(0f, (float)(value.NextHitTime - Time.timeAsDouble)) + damageTag.DelayPerHit * (float)damageTag.TotalHitLimit);
			}
		}
		else
		{
			damageTag.OnBegin(owner, out var spawnedLoopEffect);
			value = new DamageTagInfo
			{
				NextHitTime = Time.timeAsDouble + (double)damageTag.StartDelay,
				SpawnedLoopEffect = spawnedLoopEffect,
				HitsLeft = ((hitAmountOverride > 0) ? hitAmountOverride : damageTag.TotalHitLimit)
			};
			value.CheckLerpEmission();
			taggedDamage[damageTag] = value;
		}
		if ((bool)damageTag.DamageCooldownTimer)
		{
			value.NextHitTime = Helper.Max(value.NextHitTime, damageTag.DamageCooldownTimer.EndTime);
		}
		value.Stacked++;
	}

	public void RemoveDamageTagFromStack(DamageTag damageTag, bool oneMoreHit = false)
	{
		if (!damageTag)
		{
			Debug.LogError("DamageTag was null", this);
		}
		else
		{
			if (!taggedDamage.TryGetValue(damageTag, out var value))
			{
				return;
			}
			if (oneMoreHit)
			{
				value.RemoveAfterNextHit = true;
				return;
			}
			value.Stacked--;
			if (value.Stacked <= 0)
			{
				taggedDamageRemove.Enqueue(damageTag);
				value.StopLoopEffect();
			}
		}
	}

	public void Tick(bool canTakeDamage)
	{
		ApplyQueued();
		if (taggedDamage.Count <= 0)
		{
			return;
		}
		bool flag = !canTakeDamage || (isEnemy && base.gameObject.layer != 11) || (!ignoreColliderState && (bool)collider && !collider.enabled);
		foreach (KeyValuePair<DamageTag, DamageTagInfo> item in taggedDamage)
		{
			DamageTag damageTag = item.Key;
			DamageTagInfo info = item.Value;
			if (flag && (damageTag.TotalHitLimit > 0 || info.RemoveAfterNextHit))
			{
				Remove();
			}
			else if (!(Time.timeAsDouble < info.NextHitTime))
			{
				damageTag.OnHit(owner);
				info.NextHitTime = Time.timeAsDouble + (double)damageTag.DelayPerHit;
				if ((bool)damageTag.DamageCooldownTimer)
				{
					info.NextHitTime = Helper.Max(info.NextHitTime, damageTag.DamageCooldownTimer.EndTime);
				}
				int hitsLeft = info.HitsLeft;
				if (hitsLeft > 0)
				{
					info.HitsLeft--;
				}
				if ((info.HitsLeft <= 0 && hitsLeft > 0) || info.RemoveAfterNextHit)
				{
					Remove();
				}
			}
			void Remove()
			{
				taggedDamageRemove.Enqueue(damageTag);
				info.StopLoopEffect();
			}
		}
		ApplyQueued();
	}

	private void ApplyQueued()
	{
		while (taggedDamageRemove.Count > 0)
		{
			taggedDamage.Remove(taggedDamageRemove.Dequeue());
		}
		taggedDamageRemove.Clear();
	}

	public void ClearTagDamage()
	{
		taggedDamageRemove.Clear();
		foreach (KeyValuePair<DamageTag, DamageTagInfo> item in taggedDamage)
		{
			item.Value.StopLoopEffect();
		}
		taggedDamage.Clear();
	}

	public void SetIgnoreColliderState(bool state)
	{
		ignoreColliderState = state;
	}
}
