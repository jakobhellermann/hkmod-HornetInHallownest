public static class EventRegisterEvents
{
	public static readonly int FsmCancel = EventRegister.GetEventHashCode("FSM CANCEL");

	public static readonly int HeroDeath = EventRegister.GetEventHashCode("HERO DEATH");

	public static readonly int MaggotCheck = EventRegister.GetEventHashCode("MAGGOT CHECK");

	public static readonly int HeroHealed = EventRegister.GetEventHashCode("HERO HEALED");

	public static readonly int HeroHealedToMax = EventRegister.GetEventHashCode("HERO HEALED TO MAX");

	public static readonly int HealthUpdate = EventRegister.GetEventHashCode("HEALTH UPDATE");

	public static readonly int CharmIndicatorCheck = EventRegister.GetEventHashCode("CHARM INDICATOR CHECK");

	public static readonly int HeroSurfaceEnter = EventRegister.GetEventHashCode("HERO SURFACE ENTER");

	public static readonly int HeroSurfaceExit = EventRegister.GetEventHashCode("HERO SURFACE EXIT");

	public static readonly int StartExtractM = EventRegister.GetEventHashCode("START EXTRACT M");

	public static readonly int StartExtractSwamp = EventRegister.GetEventHashCode("START EXTRACT SWAMP");

	public static readonly int StartExtractBlueblood = EventRegister.GetEventHashCode("START EXTRACT BLUEBLOOD");

	public static readonly int CinematicStart = EventRegister.GetEventHashCode("CINEMATIC START");

	public static readonly int CinematicEnd = EventRegister.GetEventHashCode("CINEMATIC END");

	public static readonly int CinematicSkipped = EventRegister.GetEventHashCode("CINEMATIC SKIPPED");

	public static readonly int FleaMusicStart = EventRegister.GetEventHashCode("FLEA MUSIC START");

	public static readonly int FleaMusicStop = EventRegister.GetEventHashCode("FLEA MUSIC STOP");

	public static readonly int BenchRegainControl = EventRegister.GetEventHashCode("BENCH REGAIN CONTROL");

	public static readonly int BenchRelinquishControl = EventRegister.GetEventHashCode("BENCH RELINQUISH CONTROL");

	public static readonly int LavaBellRecharging = EventRegister.GetEventHashCode("LAVA BELL RECHARGING");

	public static readonly int FrostUpdateHealth = EventRegister.GetEventHashCode("FROST UPDATE HEALTH");

	public static readonly int HeroDamagedExtra = EventRegister.GetEventHashCode("HERO DAMAGED EXTRA");

	public static readonly int LavaBellUsed = EventRegister.GetEventHashCode("LAVA BELL USED");

	public static readonly int ReminderBind = EventRegister.GetEventHashCode("REMINDER BIND");

	public static readonly int WarriorRageEnded = EventRegister.GetEventHashCode("WARRIOR RAGE ENDED");

	public static readonly int RegeneratedSilkChunk = EventRegister.GetEventHashCode("REGENERATED SILK CHUNK");

	public static readonly int SilkCursedUpdate = EventRegister.GetEventHashCode("SILK CURSED UPDATE");

	public static readonly int WarriorRageStarted = EventRegister.GetEventHashCode("WARRIOR RAGE STARTED");

	public static readonly int HazardRespawnReset = EventRegister.GetEventHashCode("HAZARD RESPAWN RESET");

	public static readonly int HeroHazardDeath = EventRegister.GetEventHashCode("HERO HAZARD DEATH");

	public static readonly int SpoolUnbroken = EventRegister.GetEventHashCode("SPOOL UNBROKEN");

	public static readonly int ClearEffects = EventRegister.GetEventHashCode("CLEAR EFFECTS");

	public static readonly int ClearEffectsInstant = EventRegister.GetEventHashCode("CLEAR EFFECTS INSTANT");

	public static readonly int LavaBellReset = EventRegister.GetEventHashCode("LAVA BELL RESET");

	public static readonly int ItemCollected = EventRegister.GetEventHashCode("ITEM COLLECTED");

	public static readonly int BreakHeroCorpse = EventRegister.GetEventHashCode("BREAK HERO CORPSE");

	public static readonly int DeliveryHudRefresh = EventRegister.GetEventHashCode("DELIVERY HUD REFRESH");

	public static readonly int DeliveryHudHit = EventRegister.GetEventHashCode("DELIVERY HUD HIT");

	public static readonly int DeliveryHudBreak = EventRegister.GetEventHashCode("DELIVERY HUD BREAK");

	public static readonly int InventoryCancel = EventRegister.GetEventHashCode("INVENTORY CANCEL");

	public static readonly int GgTransitionOutInstant = EventRegister.GetEventHashCode("GG TRANSITION OUT INSTANT");

	public static readonly int GgTransitionIn = EventRegister.GetEventHashCode("GG TRANSITION IN");

	public static readonly int GgTransitionFinal = EventRegister.GetEventHashCode("GG TRANSITION FINAL");

	public static readonly int GgTransitionOutStatue = EventRegister.GetEventHashCode("GG TRANSITION OUT STATUE");

	public static readonly int ShowBoundNail = EventRegister.GetEventHashCode("SHOW BOUND NAIL");

	public static readonly int ShowBoundCharms = EventRegister.GetEventHashCode("SHOW BOUND CHARMS");

	public static readonly int UpdateBlueHealth = EventRegister.GetEventHashCode("UPDATE BLUE HEALTH");

	public static readonly int BindVesselOrb = EventRegister.GetEventHashCode("BIND VESSEL ORB");

	public static readonly int HideBoundNail = EventRegister.GetEventHashCode("HIDE BOUND NAIL");

	public static readonly int HideBoundCharms = EventRegister.GetEventHashCode("HIDE BOUND CHARMS");

	public static readonly int UnbindVesselOrb = EventRegister.GetEventHashCode("UNBIND VESSEL ORB");

	public static readonly int ToolPinThunked = EventRegister.GetEventHashCode("TOOL PIN THUNKED");

	public static readonly int SceneTransitionBegan = EventRegister.GetEventHashCode("SCENE TRANSITION BEGAN");

	public static readonly int HazardFade = EventRegister.GetEventHashCode("HAZARD FADE");

	public static readonly int HazardReload = EventRegister.GetEventHashCode("HAZARD RELOAD");

	public static readonly int HeroEnteredScene = EventRegister.GetEventHashCode("HERO ENTERED SCENE");

	public static readonly int CustomCutsceneSkip = EventRegister.GetEventHashCode("CUSTOM CUTSCENE SKIP");

	public static readonly int DreamPlantHit = EventRegister.GetEventHashCode("DREAM PLANT HIT");

	public static readonly int DreamOrbCollect = EventRegister.GetEventHashCode("DREAM ORB COLLECT");

	public static readonly int EnviroUpdate = EventRegister.GetEventHashCode("ENVIRO UPDATE");

	public static readonly int AddBlueHealth = EventRegister.GetEventHashCode("ADD BLUE HEALTH");

	public static readonly int AddQueuedBlueHealth = EventRegister.GetEventHashCode("ADD QUEUED BLUE HEALTH");

	public static readonly int StartExtractB = EventRegister.GetEventHashCode("START EXTRACT B");

	public static readonly int ShowCustomToolReminder = EventRegister.GetEventHashCode("SHOW CUSTOM TOOL REMINDER");

	public static readonly int HideCustomToolReminder = EventRegister.GetEventHashCode("HIDE CUSTOM TOOL REMINDER");

	public static readonly int HudFrameChanged = EventRegister.GetEventHashCode("HUD FRAME CHANGED");

	public static readonly int DialogueBoxAppearing = EventRegister.GetEventHashCode("DIALOGUE BOX APPEARING");

	public static readonly int ResetShopWindow = EventRegister.GetEventHashCode("RESET SHOP WINDOW");

	public static readonly int GgTransitionOut = EventRegister.GetEventHashCode("GG TRANSITION OUT");

	public static readonly int HeroDamaged = EventRegister.GetEventHashCode("HERO DAMAGED");

	public static readonly int CogDamage = EventRegister.GetEventHashCode("COG DAMAGE");

	public static readonly int BindFailedNotEnough = EventRegister.GetEventHashCode("BIND FAILED NOT ENOUGH");

	public static readonly int ExtractCancel = EventRegister.GetEventHashCode("EXTRACT CANCEL");

	public static readonly int InventoryOpenComplete = EventRegister.GetEventHashCode("INVENTORY OPEN COMPLETE");

	public static readonly int FlintSlateExpire = EventRegister.GetEventHashCode("FLINT SLATE EXPIRE");

	public static readonly int EquipsChangedEvent = EventRegister.GetEventHashCode("TOOL EQUIPS CHANGED");

	public static readonly int EquipsChangedPostEvent = EventRegister.GetEventHashCode("POST TOOL EQUIPS CHANGED");
}
