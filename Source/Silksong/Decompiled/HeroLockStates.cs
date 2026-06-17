using System;

namespace Silksong;

[Flags]
public enum HeroLockStates
{
	None = 0,
	AnimationLocked = 1,
	ControlLocked = 2,
	GravityLocked = 4,
	All = -1
}
