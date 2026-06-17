using GlobalEnums;
using UnityEngine;

public sealed class WallTouchCache : FixedUpdateCache
{
	public sealed class HitInfo
	{
		public RaycastHit2D hit;

		private bool hasCollider2D;

		private bool cachedCollider2D;

		private bool cachedSlope;

		private bool cachedSlider;

		private Collider2D collider2D;

		private bool isSteepSlope;

		private bool isNonSlider;

		public bool HasCollider
		{
			get
			{
				if (!cachedCollider2D)
				{
					cachedCollider2D = true;
					collider2D = hit.collider;
					hasCollider2D = collider2D;
				}
				return hasCollider2D;
			}
		}

		public Collider2D Collider2D
		{
			get
			{
				if (!cachedCollider2D)
				{
					cachedCollider2D = true;
					collider2D = hit.collider;
					hasCollider2D = collider2D;
				}
				return collider2D;
			}
		}

		public bool IsSteepSlope
		{
			get
			{
				if (!cachedSlope)
				{
					cachedSlope = true;
					if (HasCollider)
					{
						isSteepSlope = SteepSlope.IsSteepSlope(Collider2D);
					}
				}
				return isSteepSlope;
			}
		}

		public bool IsNonSlider
		{
			get
			{
				if (!cachedSlider)
				{
					cachedSlider = true;
					if (HasCollider)
					{
						isNonSlider = NonSlider.TryGetNonSlider(Collider2D, out var nonSlider) && nonSlider.IsActive;
					}
				}
				return isNonSlider;
			}
		}

		public void Reset()
		{
			hasCollider2D = false;
			cachedCollider2D = false;
			cachedSlope = false;
			cachedSlider = false;
			collider2D = null;
			isSteepSlope = false;
			isNonSlider = false;
		}
	}

	public readonly HitInfo top = new HitInfo();

	public readonly HitInfo mid = new HitInfo();

	public readonly HitInfo bottom = new HitInfo();

	private Vector3 lastPosition;

	private const float rayLength = 0.1f;

	public void Update(Collider2D collider2D, CollisionSide side, bool force)
	{
		Vector3 position = collider2D.transform.position;
		if (force || ShouldUpdate() || position != lastPosition)
		{
			lastPosition = position;
			Bounds bounds = collider2D.bounds;
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;
			Vector3 center = bounds.center;
			top.Reset();
			mid.Reset();
			bottom.Reset();
			Physics2D.SyncTransforms();
			switch (side)
			{
			case CollisionSide.left:
			{
				Vector2 origin4 = new Vector2(min.x, max.y);
				Vector2 origin5 = new Vector2(min.x, center.y);
				Vector2 origin6 = new Vector2(min.x, min.y);
				Helper.IsRayHittingNoTriggers(origin4, Vector2.left, 0.1f, 8448, out var closestHit4);
				Helper.IsRayHittingNoTriggers(origin5, Vector2.left, 0.1f, 8448, out var closestHit5);
				Helper.IsRayHittingNoTriggers(origin6, Vector2.left, 0.1f, 8448, out var closestHit6);
				top.hit = closestHit4;
				mid.hit = closestHit5;
				bottom.hit = closestHit6;
				break;
			}
			case CollisionSide.right:
			{
				Vector2 origin = new Vector2(max.x, max.y);
				Vector2 origin2 = new Vector2(max.x, center.y);
				Vector2 origin3 = new Vector2(max.x, min.y);
				Helper.IsRayHittingNoTriggers(origin, Vector2.right, 0.1f, 8448, out var closestHit);
				Helper.IsRayHittingNoTriggers(origin2, Vector2.right, 0.1f, 8448, out var closestHit2);
				Helper.IsRayHittingNoTriggers(origin3, Vector2.right, 0.1f, 8448, out var closestHit3);
				top.hit = closestHit;
				mid.hit = closestHit2;
				bottom.hit = closestHit3;
				break;
			}
			}
		}
	}

	public void Reset()
	{
		top.Reset();
		mid.Reset();
		bottom.Reset();
	}
}
