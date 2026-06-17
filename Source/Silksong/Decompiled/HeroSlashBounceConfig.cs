using UnityEngine;

namespace Silksong;

[CreateAssetMenu(menuName = "Profiles/Hero Slash Bounce")]
public class HeroSlashBounceConfig : ScriptableObject
{
	[SerializeField]
	private int jumpSteps = 4;

	[SerializeField]
	private int jumpedSteps = -20;

	[SerializeField]
	private bool hideSlashOnBounceCancel = true;

	private static HeroSlashBounceConfig _default;

	public int JumpSteps => jumpSteps;

	public int JumpedSteps => jumpedSteps;

	public bool HideSlashOnBounceCancel => hideSlashOnBounceCancel;

	public static HeroSlashBounceConfig Default
	{
		get
		{
			if (!_default)
			{
				_default = ScriptableObject.CreateInstance<HeroSlashBounceConfig>();
			}
			return _default;
		}
	}
}
