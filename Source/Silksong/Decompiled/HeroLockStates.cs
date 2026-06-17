using System;

namespace GlobalEnums;

[Flags]
public enum HeroLockStates
{
	None = 0,
	AnimationLocked = 1,
	ControlLocked = 2,
	GravityLocked = 4,
	All = -1
}
