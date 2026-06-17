using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace Silksong;

public class HeroNailImbuement : MonoBehaviour
{
	[SerializeField]
	private SpriteRenderer imbuedHeroLightRenderer;

	[SerializeField]
	private NestedFadeGroupBase imbuedHeroLightGroup;

	[SerializeField]
	private float imbuedHeroLightFadeInDuration;

	[SerializeField]
	private float imbuedHeroLightFadeOutDuration;

	[Space]
	[SerializeField]
	[ArrayForEnum(typeof(NailElements))]
	private NailImbuementConfig[] nailConfigs;

	private NailImbuementConfig currentImbuement;

	private float imbuementTimeLeft;

	private PlayParticleEffects spawnedParticles;

	private SpriteFlash spriteFlash;

	private SpriteFlash.FlashHandle flashingHandle;

	public NailElements CurrentElement { get; private set; }

	public NailImbuementConfig CurrentImbuement => currentImbuement;

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref nailConfigs, typeof(NailElements));
	}

	private void Awake()
	{
		OnValidate();
		spriteFlash = GetComponent<SpriteFlash>();
		if (nailConfigs != null)
		{
			for (int i = 0; i < nailConfigs.Length; i++)
			{
				NailImbuementConfig nailImbuementConfig = nailConfigs[i];
				if (!(nailImbuementConfig == null))
				{
					nailImbuementConfig.EnsurePersonalPool(base.gameObject);
				}
			}
		}
		EventRegister.GetRegisterGuaranteed(base.gameObject, "TOOL EQUIPS CHANGED").ReceivedEvent += delegate
		{
			if (ToolItemManager.ActiveState != ToolsActiveStates.Active)
			{
				SetElement(NailElements.None);
			}
		};
		EventRegister.GetRegisterGuaranteed(base.gameObject, "BENCHREST START").ReceivedEvent += delegate
		{
			SetElement(NailElements.None);
		};
	}

	private void Update()
	{
		if (imbuementTimeLeft > 0f)
		{
			imbuementTimeLeft -= Time.deltaTime;
			if (imbuementTimeLeft <= 0f)
			{
				SetElement(NailElements.None);
			}
		}
	}

	public void SetElement(NailElements element)
	{
		if (CurrentElement == NailElements.Fire && element != NailElements.Fire)
		{
			EventRegister.SendEvent(EventRegisterEvents.FlintSlateExpire);
		}
		else if (CurrentElement == NailElements.Poison && element != NailElements.Poison)
		{
			EventRegister.SendEvent(EventRegisterEvents.FlintSlateExpire);
		}
		CurrentElement = element;
		if ((bool)spawnedParticles)
		{
			spawnedParticles.StopParticleSystems();
			spawnedParticles = null;
		}
		if (element == NailElements.None)
		{
			currentImbuement = null;
			imbuementTimeLeft = 0f;
			spriteFlash.CancelRepeatingFlash(flashingHandle);
			if ((bool)imbuedHeroLightGroup)
			{
				imbuedHeroLightGroup.FadeTo(0f, imbuedHeroLightFadeOutDuration);
			}
			return;
		}
		NailImbuementConfig nailImbuementConfig = currentImbuement;
		currentImbuement = nailConfigs[(int)element];
		imbuementTimeLeft = currentImbuement.Duration;
		spriteFlash.flashFocusHeal();
		if (currentImbuement != nailImbuementConfig || !spriteFlash.IsFlashing(repeating: true, flashingHandle))
		{
			SpriteFlash.FlashConfig heroFlashing = currentImbuement.HeroFlashing;
			flashingHandle = spriteFlash.Flash(heroFlashing.Colour, heroFlashing.Amount, heroFlashing.TimeUp, heroFlashing.StayTime, heroFlashing.TimeDown, 0f, repeating: true, 0, 1);
			if ((bool)imbuedHeroLightRenderer)
			{
				imbuedHeroLightRenderer.color = currentImbuement.ExtraHeroLightColor;
			}
			if ((bool)imbuedHeroLightGroup)
			{
				imbuedHeroLightGroup.FadeTo(1f, imbuedHeroLightFadeInDuration);
			}
		}
		if ((bool)currentImbuement.HeroParticles)
		{
			spawnedParticles = currentImbuement.HeroParticles.Spawn(base.transform.position);
			spawnedParticles.PlayParticleSystems();
		}
	}
}
