using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace Silksong;

public class InteractManager : ManagerSingleton<InteractManager>
{
	private class InteractableReference
	{
		public readonly InteractableBase Interactable;

		public readonly Transform Transform;

		public readonly Vector3 PromptOffset;

		public PromptMarker Prompt;

		public InteractableReference(InteractableBase interactable, Transform transform, Vector3 promptOffset)
		{
			Interactable = interactable;
			Transform = transform;
			PromptOffset = promptOffset;
		}
	}

	[SerializeField]
	private PromptMarker interactPromptPrefab;

	private InteractableBase blockingInteractable;

	private bool isDisabled;

	private double canInteractTime;

	private int canInteractFrames;

	private const int INTERACT_COOLDOWN_FRAMES = 5;

	private readonly List<InteractableReference> allInteractables = new List<InteractableReference>();

	private InteractableReference currentInteractable;

	private Transform player;

	private InputHandler inputHandler;

	public static bool CanInteract
	{
		get
		{
			InteractManager instance = ManagerSingleton<InteractManager>.Instance;
			if ((bool)instance && Time.timeAsDouble < instance.canInteractTime)
			{
				return false;
			}
			HeroController silentInstance = HeroController.SilentInstance;
			if (!silentInstance || silentInstance.IsPaused() || silentInstance.IsInputBlocked())
			{
				return false;
			}
			if (BlockingInteractable == null)
			{
				return !IsDisabled;
			}
			return false;
		}
	}

	[UsedImplicitly]
	public static bool AlwaysShowPrompts { get; set; }

	public static bool IsDisabled
	{
		get
		{
			if ((bool)ManagerSingleton<InteractManager>.Instance)
			{
				return ManagerSingleton<InteractManager>.Instance.isDisabled;
			}
			return true;
		}
		set
		{
			if ((bool)ManagerSingleton<InteractManager>.Instance)
			{
				ManagerSingleton<InteractManager>.Instance.isDisabled = value;
			}
		}
	}

	public static InteractableBase BlockingInteractable
	{
		get
		{
			if (!ManagerSingleton<InteractManager>.Instance)
			{
				return null;
			}
			return ManagerSingleton<InteractManager>.Instance.blockingInteractable;
		}
		set
		{
			if ((bool)ManagerSingleton<InteractManager>.Instance)
			{
				ManagerSingleton<InteractManager>.Instance.blockingInteractable = value;
			}
		}
	}

	public static bool IsPromptVisible
	{
		get
		{
			InteractManager instance = ManagerSingleton<InteractManager>.Instance;
			if (!instance)
			{
				return false;
			}
			if (instance.currentInteractable == null || !instance.currentInteractable.Prompt)
			{
				return false;
			}
			return instance.currentInteractable.Prompt.IsVisible;
		}
	}

	protected override void Awake()
	{
		base.Awake();
		EventRegister.GetRegisterGuaranteed(base.gameObject, "HERO DAMAGED").ReceivedEvent += OnHeroInterrupted;
		EventRegister.GetRegisterGuaranteed(base.gameObject, "FSM CANCEL").ReceivedEvent += OnHeroInterrupted;
	}

	private void Start()
	{
		inputHandler = ManagerSingleton<InputHandler>.Instance;
		GameManager.instance.NextSceneWillActivate += ResetState;
	}

	private void OnDisable()
	{
		if ((bool)GameManager.instance)
		{
			GameManager.instance.NextSceneWillActivate -= ResetState;
		}
	}

	private void ResetState()
	{
		ClearCurrentInteractible();
		allInteractables.Clear();
		blockingInteractable = null;
		isDisabled = false;
	}

	private void Update()
	{
		if (!player && (bool)GameManager.instance && (bool)GameManager.instance.hero_ctrl)
		{
			player = GameManager.instance.hero_ctrl.transform;
		}
		bool flag = (bool)player && CanInteract && HeroController.instance.CanInteract();
		if ((bool)player)
		{
			InteractableReference interactableReference = null;
			float num = float.MaxValue;
			InteractableReference interactableReference2 = null;
			float num2 = float.MaxValue;
			InteractableReference interactableReference3 = null;
			float num3 = float.MaxValue;
			if (flag || AlwaysShowPrompts)
			{
				foreach (InteractableReference allInteractable in allInteractables)
				{
					if (allInteractable.Interactable.IsBlocked)
					{
						continue;
					}
					float num4 = Vector3.Distance(allInteractable.Transform.position, player.position);
					if (num4 < num)
					{
						interactableReference = allInteractable;
						num = num4;
					}
					switch (allInteractable.Interactable.Priority)
					{
					case InteractPriority.Regular:
						if (num4 < num2)
						{
							interactableReference2 = allInteractable;
							num2 = num4;
						}
						break;
					case InteractPriority.HighPriority:
						if (num4 < num3)
						{
							interactableReference3 = allInteractable;
							num3 = num4;
						}
						break;
					default:
						Debug.LogError($"InteractPriority \"{allInteractable.Interactable}\" not implemented");
						break;
					case InteractPriority.LowPriority:
						break;
					}
				}
			}
			if (canInteractFrames > 0)
			{
				canInteractFrames--;
			}
			InteractableReference interactableReference4 = interactableReference3 ?? interactableReference2 ?? interactableReference;
			if (interactableReference4 != currentInteractable)
			{
				ClearCurrentInteractible();
				if (canInteractFrames <= 0 && CanInteract)
				{
					if (interactableReference4 != null)
					{
						currentInteractable = interactableReference4;
						currentInteractable.Interactable.CanInteract();
						if ((bool)interactPromptPrefab)
						{
							PromptMarker promptMarker = interactPromptPrefab.Spawn();
							promptMarker.SetLabel(currentInteractable.Interactable.InteractLabelDisplay);
							promptMarker.SetOwner(currentInteractable.Transform.gameObject);
							promptMarker.SetFollowing(currentInteractable.Transform, currentInteractable.PromptOffset);
							promptMarker.Show();
							currentInteractable.Prompt = promptMarker;
						}
					}
				}
				else if (!CanInteract)
				{
					canInteractFrames = 5;
				}
			}
			else if (!CanInteract)
			{
				canInteractFrames = 5;
			}
		}
		HeroActions inputActions = inputHandler.inputActions;
		if (flag && currentInteractable != null && (inputActions.Up.WasPressed || inputActions.Down.WasPressed) && !inputActions.Left.IsPressed && !inputActions.Right.IsPressed)
		{
			currentInteractable.Interactable.QueueInteraction();
		}
	}

	private void ClearCurrentInteractible()
	{
		if (currentInteractable != null)
		{
			if (currentInteractable.Prompt != null)
			{
				currentInteractable.Prompt.Hide();
				currentInteractable.Prompt = null;
			}
			currentInteractable.Interactable.CanNotInteract();
			currentInteractable = null;
		}
	}

	public static void AddInteractible(InteractableBase interactible, Transform transform, Vector3 promptOffset)
	{
		if (ManagerSingleton<InteractManager>.Instance == null)
		{
			return;
		}
		foreach (InteractableReference allInteractable in ManagerSingleton<InteractManager>.UnsafeInstance.allInteractables)
		{
			if (allInteractable.Interactable == interactible)
			{
				return;
			}
		}
		ManagerSingleton<InteractManager>.UnsafeInstance.allInteractables.Add(new InteractableReference(interactible, transform, promptOffset));
	}

	public static bool RemoveInteractible(InteractableBase interactible)
	{
		if (ManagerSingleton<InteractManager>.UnsafeInstance == null)
		{
			return false;
		}
		InteractableReference interactableReference = null;
		foreach (InteractableReference allInteractable in ManagerSingleton<InteractManager>.UnsafeInstance.allInteractables)
		{
			if (allInteractable.Interactable == interactible)
			{
				interactableReference = allInteractable;
			}
		}
		if (interactableReference != null)
		{
			ManagerSingleton<InteractManager>.UnsafeInstance.allInteractables.Remove(interactableReference);
			if (ManagerSingleton<InteractManager>.UnsafeInstance.currentInteractable == interactableReference)
			{
				ManagerSingleton<InteractManager>.UnsafeInstance.ClearCurrentInteractible();
			}
			return true;
		}
		return false;
	}

	private void OnHeroInterrupted()
	{
		if ((bool)blockingInteractable)
		{
			PlayMakerNPC playMakerNPC = blockingInteractable as PlayMakerNPC;
			if (playMakerNPC != null)
			{
				playMakerNPC.ForceEndDialogue();
			}
			else
			{
				DialogueBox.EndConversation();
			}
		}
	}

	public static void SetEnabledDelay(float delay)
	{
		InteractManager instance = ManagerSingleton<InteractManager>.Instance;
		if ((bool)instance)
		{
			instance.canInteractTime = Time.timeAsDouble + (double)delay;
		}
	}
}
