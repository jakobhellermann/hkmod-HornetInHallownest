using UnityEngine;

public sealed class TouchGroundResult : FixedUpdateCache
{
	private Vector3 lastPosition;

	public bool IsTouchingGround { get; private set; }

	public void Update(Collider2D collider2D, bool forced)
	{
		Vector3 position = collider2D.transform.position;
		float rayLengthY;
		if (forced || ShouldUpdate() || position != lastPosition)
		{
			lastPosition = position;
			Bounds bounds = collider2D.bounds;
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;
			Vector3 center = bounds.center;
			Vector2 origin = new Vector2(min.x, center.y);
			Vector2 origin2 = center;
			Vector2 origin3 = new Vector2(max.x, center.y);
			rayLengthY = bounds.extents.y + 0.16f;
			Physics2D.SyncTransforms();
			IsTouchingGround = IsRayHitting(origin) || IsRayHitting(origin2) || IsRayHitting(origin3);
		}
		bool IsRayHitting(Vector2 origin4)
		{
			if (!Helper.IsRayHittingNoTriggers(origin4, Vector2.down, rayLengthY, 8448, out var closestHit))
			{
				return false;
			}
			return !SteepSlope.IsSteepSlope(closestHit.collider);
		}
	}
}
