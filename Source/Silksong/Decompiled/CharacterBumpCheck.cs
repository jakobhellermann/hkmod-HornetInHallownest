using System;
using GlobalEnums;
using UnityEngine;

public class CharacterBumpCheck : MonoBehaviour
{
	private LayerMask groundMask;

	private Rigidbody2D body;

	private Collider2D collider;

	private Func<bool> getIsFacingRight;

	private readonly RaycastHit2D[] bumpHits = new RaycastHit2D[10];

	private int bumpHitCount;

	private readonly RaycastHit2D[] wallHits = new RaycastHit2D[10];

	private int wallHitCount;

	private int wallHitHighCount;

	private void Awake()
	{
		groundMask = 256;
		body = GetComponent<Rigidbody2D>();
		collider = GetComponent<Collider2D>();
		getIsFacingRight = () => base.transform.localScale.x > 0f;
	}

	public static CharacterBumpCheck Add(GameObject target, LayerMask groundMask, Rigidbody2D body, Collider2D collider, Func<bool> getIsFacingRight)
	{
		CharacterBumpCheck characterBumpCheck = target.AddComponent<CharacterBumpCheck>();
		characterBumpCheck.groundMask = groundMask;
		characterBumpCheck.body = body;
		characterBumpCheck.collider = collider;
		characterBumpCheck.getIsFacingRight = getIsFacingRight;
		return characterBumpCheck;
	}

	public void CheckForBump(CollisionSide side, out bool hitBump, out bool hitWall, out bool hitHighWall)
	{
		float num = 0.03f;
		Vector2 linearVelocity = body.linearVelocity;
		float num2;
		switch (side)
		{
		case CollisionSide.left:
		case CollisionSide.right:
			num2 = linearVelocity.x;
			break;
		case CollisionSide.top:
		case CollisionSide.bottom:
			num2 = linearVelocity.y;
			break;
		default:
			throw new ArgumentOutOfRangeException("side", side, null);
		}
		float num3 = num2;
		float num4 = num + Mathf.Abs(num3 * Time.deltaTime);
		float distance = 0.1f + num4;
		float num5 = Physics2D.defaultContactOffset * 2f + 0.001f;
		Bounds bounds = collider.bounds;
		Vector3 min = bounds.min;
		Vector3 center = bounds.center;
		Vector3 max = bounds.max;
		ContactFilter2D contactFilter = new ContactFilter2D
		{
			useTriggers = false,
			useLayerMask = true,
			layerMask = groundMask
		};
		bool flag = getIsFacingRight();
		switch (side)
		{
		case CollisionSide.left:
		case CollisionSide.right:
		{
			Vector2 origin3 = new Vector2(min.x + 0.1f, min.y + 0.2f);
			Vector2 origin4 = new Vector2(min.x + 0.1f, max.y + num5);
			Vector2 origin5 = new Vector2(min.x + 0.1f, min.y - num5);
			Vector2 origin6 = new Vector2(max.x - 0.1f, min.y + 0.2f);
			Vector2 origin7 = new Vector2(max.x - 0.1f, max.y + num5);
			Vector2 origin8 = new Vector2(max.x - 0.1f, min.y - num5);
			if (side == CollisionSide.left)
			{
				wallHitCount = Physics2D.Raycast(origin3, Vector2.left, contactFilter, wallHits, distance);
				wallHitHighCount = Physics2D.Raycast(origin4, Vector2.left, contactFilter, wallHits, distance);
				bumpHitCount = Physics2D.Raycast(origin5, Vector2.left, contactFilter, bumpHits, distance);
			}
			else
			{
				wallHitCount = Physics2D.Raycast(origin6, Vector2.right, contactFilter, wallHits, distance);
				wallHitHighCount = Physics2D.Raycast(origin7, Vector2.right, contactFilter, wallHits, distance);
				bumpHitCount = Physics2D.Raycast(origin8, Vector2.right, contactFilter, bumpHits, distance);
			}
			break;
		}
		case CollisionSide.top:
		{
			Vector2 origin;
			Vector2 origin2;
			if (flag)
			{
				origin = new Vector2(max.x - 0.2f, max.y - 0.1f);
				origin2 = new Vector2(max.x + num5, max.y - 0.1f);
			}
			else
			{
				origin = new Vector2(min.x + 0.2f, max.y - 0.1f);
				origin2 = new Vector2(min.x - num5, max.y - 0.1f);
			}
			wallHitCount = Physics2D.Raycast(origin, Vector2.up, contactFilter, wallHits, distance);
			wallHitHighCount = 0;
			bumpHitCount = Physics2D.Raycast(origin2, Vector2.up, contactFilter, bumpHits, distance);
			break;
		}
		default:
			throw new ArgumentOutOfRangeException("side", side, null);
		}
		hitBump = false;
		hitHighWall = wallHitHighCount > 0;
		hitWall = hitHighWall || wallHitCount > 0;
		float num6 = 0f;
		for (int i = 0; i < bumpHitCount; i++)
		{
			RaycastHit2D raycastHit2D = bumpHits[i];
			if (raycastHit2D.collider != null && !hitWall)
			{
				if (SteepSlope.IsSteepSlope(raycastHit2D.collider))
				{
					continue;
				}
				Vector2 origin9;
				Vector2 origin10;
				Vector2 direction;
				switch (side)
				{
				case CollisionSide.left:
					origin9 = raycastHit2D.point + new Vector2(-0.1f, 1f);
					origin10 = new Vector2(max.x - 0.001f, raycastHit2D.point.y + 1f);
					direction = Vector2.down;
					break;
				case CollisionSide.right:
					origin9 = raycastHit2D.point + new Vector2(0.1f, 1f);
					origin10 = new Vector2(min.x + 0.001f, raycastHit2D.point.y + 1f);
					direction = Vector2.down;
					break;
				case CollisionSide.top:
					if (flag)
					{
						origin9 = raycastHit2D.point + new Vector2(-1f, 0.1f);
						origin10 = new Vector2(raycastHit2D.point.x - 1f, min.y - 0.001f);
						direction = Vector2.right;
					}
					else
					{
						origin9 = raycastHit2D.point + new Vector2(1f, 0.1f);
						origin10 = new Vector2(raycastHit2D.point.x + 1f, center.y - 0.001f);
						direction = Vector2.left;
					}
					break;
				default:
					throw new ArgumentOutOfRangeException("side", side, null);
				}
				Helper.IsRayHittingNoTriggers(origin9, direction, 1.2f, groundMask, out var closestHit);
				Helper.IsRayHittingNoTriggers(origin10, direction, 1.2f, groundMask, out var closestHit2);
				if ((bool)closestHit)
				{
					if ((bool)closestHit2)
					{
						switch (side)
						{
						case CollisionSide.left:
						case CollisionSide.right:
							num2 = closestHit.point.y - closestHit2.point.y;
							break;
						case CollisionSide.top:
							num2 = (flag ? (closestHit2.point.x - closestHit.point.x) : (closestHit.point.x - closestHit2.point.x));
							break;
						default:
							throw new ArgumentOutOfRangeException("side", side, null);
						}
						float num7 = num2;
						float num8 = ((!(num7 >= Physics2D.defaultContactOffset)) ? Physics2D.defaultContactOffset : num7);
						if (num8 > num6)
						{
							num6 = num8;
						}
						hitBump = true;
					}
					else
					{
						hitBump = true;
					}
				}
			}
			if (hitBump)
			{
				break;
			}
		}
		if (hitBump && !(num6 <= 0f))
		{
			float y;
			float x;
			switch (side)
			{
			case CollisionSide.left:
			case CollisionSide.right:
				y = num6 + Physics2D.defaultContactOffset;
				x = Physics2D.defaultContactOffset * (float)(flag ? 1 : (-1));
				break;
			case CollisionSide.top:
				y = Physics2D.defaultContactOffset;
				x = (num6 + Physics2D.defaultContactOffset) * (float)((!flag) ? 1 : (-1));
				break;
			default:
				throw new ArgumentOutOfRangeException("side", side, null);
			}
			Vector2 vector = new Vector2(x, y);
			Vector2 position = body.position + vector;
			body.position = position;
			Vector2 linearVelocity2 = body.linearVelocity;
			if (linearVelocity2.y < 0f)
			{
				linearVelocity2.y = 0f;
				body.linearVelocity = linearVelocity2;
			}
		}
	}
}
