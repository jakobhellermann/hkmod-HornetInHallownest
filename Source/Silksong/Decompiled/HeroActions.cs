using InControl;

namespace Silksong;

public class HeroActions : PlayerActionSet
{
	public readonly PlayerAction Left;

	public readonly PlayerAction Right;

	public readonly PlayerAction Up;

	public readonly PlayerAction Down;

	public readonly PlayerAction MenuSubmit;

	public readonly PlayerAction MenuCancel;

	public readonly PlayerAction MenuExtra;

	public readonly PlayerAction MenuSuper;

	public readonly PlayerTwoAxisAction MoveVector;

	public readonly PlayerAction RsUp;

	public readonly PlayerAction RsDown;

	public readonly PlayerAction RsLeft;

	public readonly PlayerAction RsRight;

	public readonly PlayerTwoAxisAction RightStick;

	public readonly PlayerAction Jump;

	public readonly PlayerAction Evade;

	public readonly PlayerAction Dash;

	public readonly PlayerAction SuperDash;

	public readonly PlayerAction DreamNail;

	public readonly PlayerAction Attack;

	public readonly PlayerAction Cast;

	public readonly PlayerAction QuickMap;

	public readonly PlayerAction QuickCast;

	public readonly PlayerAction Taunt;

	public readonly PlayerAction PaneRight;

	public readonly PlayerAction PaneLeft;

	public readonly PlayerAction OpenInventory;

	public readonly PlayerAction OpenInventoryMap;

	public readonly PlayerAction OpenInventoryJournal;

	public readonly PlayerAction OpenInventoryTools;

	public readonly PlayerAction OpenInventoryQuests;

	public readonly PlayerAction SwipeInventoryMap;

	public readonly PlayerAction SwipeInventoryJournal;

	public readonly PlayerAction SwipeInventoryTools;

	public readonly PlayerAction SwipeInventoryQuests;

	public readonly PlayerAction Pause;

	public HeroActions()
	{
		MenuSubmit = CreatePlayerAction("Submit");
		MenuCancel = CreatePlayerAction("Cancel");
		MenuExtra = CreatePlayerAction("Menu Extra");
		MenuSuper = CreatePlayerAction("Menu Super");
		Left = CreatePlayerAction("Left");
		Left.StateThreshold = 0.3f;
		Right = CreatePlayerAction("Right");
		Right.StateThreshold = 0.3f;
		Up = CreatePlayerAction("Up");
		Up.StateThreshold = 0.5f;
		Down = CreatePlayerAction("Down");
		Down.StateThreshold = 0.5f;
		MoveVector = CreateTwoAxisPlayerAction(Left, Right, Down, Up);
		MoveVector.LowerDeadZone = 0.15f;
		MoveVector.UpperDeadZone = 0.95f;
		RsUp = CreatePlayerAction("RS_Up");
		RsUp.StateThreshold = 0.5f;
		RsDown = CreatePlayerAction("RS_Down");
		RsDown.StateThreshold = 0.5f;
		RsLeft = CreatePlayerAction("RS_Left");
		RsLeft.StateThreshold = 0.3f;
		RsRight = CreatePlayerAction("RS_Right");
		RsRight.StateThreshold = 0.3f;
		RightStick = CreateTwoAxisPlayerAction(RsLeft, RsRight, RsDown, RsUp);
		RightStick.LowerDeadZone = 0.15f;
		RightStick.UpperDeadZone = 0.95f;
		Jump = CreatePlayerAction("Jump");
		Attack = CreatePlayerAction("Attack");
		Evade = CreatePlayerAction("Evade");
		Dash = CreatePlayerAction("Dash");
		SuperDash = CreatePlayerAction("Super Dash");
		DreamNail = CreatePlayerAction("Dream Nail");
		Cast = CreatePlayerAction("Cast");
		QuickMap = CreatePlayerAction("Quick Map");
		QuickCast = CreatePlayerAction("Quick Cast");
		Taunt = CreatePlayerAction("Taunt");
		PaneRight = CreatePlayerAction("Pane Right");
		PaneLeft = CreatePlayerAction("Pane Left");
		OpenInventory = CreatePlayerAction("openInventory");
		OpenInventoryMap = CreatePlayerAction("openInventoryMap");
		OpenInventoryJournal = CreatePlayerAction("openInventoryJournal");
		OpenInventoryTools = CreatePlayerAction("openInventoryTools");
		OpenInventoryQuests = CreatePlayerAction("openInventoryQuests");
		SwipeInventoryMap = CreatePlayerAction("swipeInventoryMap");
		SwipeInventoryJournal = CreatePlayerAction("swipeInventoryJournal");
		SwipeInventoryTools = CreatePlayerAction("swipeInventoryTools");
		SwipeInventoryQuests = CreatePlayerAction("swipeInventoryQuests");
		Pause = CreatePlayerAction("Pause");
	}
}
