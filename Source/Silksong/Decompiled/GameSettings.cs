using System;
using GlobalEnums;
using InControl;
using UnityEngine;

namespace Silksong;

[Serializable]
public class GameSettings
{
	private static readonly bool _verboseMode;

	[Header("Game Settings")]
	public SupportedLanguages gameLanguage;

	public int backerCredits;

	public int showNativeAchievementPopups;

	public int vibrationSetting;

	public int cameraShakeSetting;

	public int hudScaleSetting;

	[Header("Audio Settings")]
	public float masterVolume;

	public float musicVolume;

	public float soundVolume;

	public bool playerVoiceEnabled;

	[Header("Video Settings")]
	public int fullScreen;

	public int vSync;

	public int useDisplay;

	public float overScanAdjustment;

	public float brightnessAdjustment;

	public int overscanAdjusted;

	public int brightnessAdjusted;

	public int targetFrameRate;

	public int particleEffectsLevel;

	public ShaderQualities shaderQuality;

	public int dithering;

	public bool cameraNoise;

	[Header("Controller Settings")]
	public ControllerMapping controllerMapping;

	public bool nativeInput;

	public bool xInput;

	public bool appleMFi;

	[Header("Keyboard Settings")]
	public string jumpKey;

	public string attackKey;

	public string dashKey;

	public string castKey;

	public string superDashKey;

	public string dreamNailKey;

	public string tauntKey;

	public string quickMapKey;

	public string quickCastKey;

	public string inventoryKey;

	public string inventoryMapKey;

	public string inventoryJournalKey;

	public string inventoryToolsKey;

	public string inventoryQuestsKey;

	public string upKey;

	public string downKey;

	public string leftKey;

	public string rightKey;

	public GameSettings()
	{
		ResetGameOptionsSettings();
		ResetControllerSettings();
		ResetAudioSettings();
		ResetVideoSettings();
		ResetAdvancedVideoSettings();
	}

	public void LoadGameOptionsSettings()
	{
		LoadEnum("GameLang", ref gameLanguage, SupportedLanguages.EN);
		LoadInt("GameBackers", ref backerCredits, 0);
		LoadInt("GameNativePopups", ref showNativeAchievementPopups, 0);
		LoadInt("RumbleSetting", ref vibrationSetting, 0);
		LoadInt("CameraShakeSetting", ref cameraShakeSetting, 0);
		LoadInt("HudScaleSetting", ref hudScaleSetting, Platform.Current.DefaultHudSetting);
	}

	public void SaveGameOptionsSettings()
	{
		Platform.Current.LocalSharedData.SetInt("GameLang", (int)gameLanguage);
		Platform.Current.LocalSharedData.SetInt("GameBackers", backerCredits);
		Platform.Current.LocalSharedData.SetInt("GameNativePopups", showNativeAchievementPopups);
		Platform.Current.LocalSharedData.SetInt("RumbleSetting", vibrationSetting);
		Platform.Current.LocalSharedData.SetInt("CameraShakeSetting", cameraShakeSetting);
		int val = hudScaleSetting;
		if (val == 2)
		{
			LoadInt("HudScaleSetting", ref val, 0);
		}
		Platform.Current.LocalSharedData.SetInt("HudScaleSetting", val);
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetGameOptionsSettings()
	{
		gameLanguage = SupportedLanguages.EN;
		backerCredits = 0;
		showNativeAchievementPopups = 0;
		vibrationSetting = 0;
		cameraShakeSetting = 0;
		if ((bool)Platform.Current)
		{
			hudScaleSetting = Platform.Current.DefaultHudSetting;
		}
	}

	public void LoadControllerSettings()
	{
		LoadInt("RumbleSetting", ref vibrationSetting, 0);
		LoadBool("NativeInput", ref nativeInput, def: false);
		LoadBool("XInput", ref xInput, def: false);
		LoadBool("AppleMFi", ref appleMFi, def: false);
	}

	public void SaveGameControllerSettings()
	{
		Platform.Current.LocalSharedData.SetInt("RumbleSetting", vibrationSetting);
		Platform.Current.LocalSharedData.SetBool("NativeInput", nativeInput);
		Platform.Current.LocalSharedData.SetBool("XInput", xInput);
		Platform.Current.LocalSharedData.SetBool("AppleMFi", appleMFi);
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetControllerSettings()
	{
		vibrationSetting = 0;
		nativeInput = false;
		xInput = false;
		appleMFi = false;
	}

	public void LoadVideoSettings()
	{
		if (CommandArgumentUsed("-resetres"))
		{
			Screen.SetResolution(1920, 1080, fullscreen: true);
		}
		LoadInt("VidFullscreen", ref fullScreen, 2);
		LoadInt("VidVSync", ref vSync, 1);
		LoadInt("VidDisplay", ref useDisplay, 0);
		LoadInt("VidTFR", ref targetFrameRate, 60);
		LoadInt("VidParticles", ref particleEffectsLevel, 1);
		LoadEnum("ShaderQuality", ref shaderQuality, ShaderQualities.High);
		LoadInt("VidDithering", ref dithering, 1);
		LoadBool("VidNoise", ref cameraNoise, def: false);
	}

	public void SaveVideoSettings()
	{
		Platform.Current.LocalSharedData.SetInt("VidFullscreen", fullScreen);
		Platform.Current.LocalSharedData.SetInt("VidVSync", vSync);
		Platform.Current.LocalSharedData.SetInt("VidDisplay", useDisplay);
		Platform.Current.LocalSharedData.SetInt("VidTFR", targetFrameRate);
		Platform.Current.LocalSharedData.SetInt("VidParticles", particleEffectsLevel);
		Platform.Current.LocalSharedData.SetInt("ShaderQuality", (int)shaderQuality);
		Platform.Current.LocalSharedData.SetInt("VidDithering", dithering);
		Platform.Current.LocalSharedData.SetBool("VidNoise", cameraNoise);
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetVideoSettings()
	{
		fullScreen = 2;
		vSync = 1;
		useDisplay = 0;
		targetFrameRate = 60;
		overscanAdjusted = 0;
		overScanAdjustment = 0f;
		brightnessAdjusted = 0;
		brightnessAdjustment = 20f;
		ResetAdvancedVideoSettings();
	}

	public void ResetAdvancedVideoSettings()
	{
		particleEffectsLevel = 1;
		shaderQuality = ShaderQualities.High;
		dithering = 1;
		cameraNoise = false;
	}

	public void LoadOverscanSettings()
	{
		LoadFloat("VidOSValue", ref overScanAdjustment, 0f);
	}

	public void SaveOverscanSettings()
	{
		Platform.Current.LocalSharedData.SetFloat("VidOSValue", overScanAdjustment);
		overscanAdjusted = 1;
		Platform.Current.LocalSharedData.SetInt("VidOSSet", overscanAdjusted);
		if (_verboseMode)
		{
			LogSavedKey("VidOSValue", overScanAdjustment);
		}
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetOverscanSettings()
	{
		overScanAdjustment = 0f;
	}

	public void LoadOverscanConfigured()
	{
		LoadInt("VidOSSet", ref overscanAdjusted, 0);
	}

	public void LoadBrightnessSettings()
	{
		LoadFloat("VidBrightValue", ref brightnessAdjustment, 20f);
	}

	public void SaveBrightnessSettings()
	{
		Platform.Current.LocalSharedData.SetFloat("VidBrightValue", brightnessAdjustment);
		brightnessAdjusted = 1;
		Platform.Current.LocalSharedData.SetInt("VidBrightSet", brightnessAdjusted);
		if (_verboseMode)
		{
			LogSavedKey("VidBrightValue", brightnessAdjustment);
		}
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetBrightnessSettings()
	{
		brightnessAdjustment = 20f;
	}

	public void LoadBrightnessConfigured()
	{
		brightnessAdjusted = Platform.Current.LocalSharedData.GetInt("VidBrightSet", 0);
	}

	public void LoadAudioSettings()
	{
		LoadFloat("MasterVolume", ref masterVolume, 10f);
		LoadFloat("MusicVolume", ref musicVolume, 10f);
		LoadFloat("SoundVolume", ref soundVolume, 10f);
		LoadBool("PlayerVoiceEnabled", ref playerVoiceEnabled, def: true);
	}

	public void SaveAudioSettings()
	{
		Platform.Current.LocalSharedData.SetFloat("MasterVolume", masterVolume);
		Platform.Current.LocalSharedData.SetFloat("MusicVolume", musicVolume);
		Platform.Current.LocalSharedData.SetFloat("SoundVolume", soundVolume);
		Platform.Current.LocalSharedData.SetBool("PlayerVoiceEnabled", playerVoiceEnabled);
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetAudioSettings()
	{
		masterVolume = 10f;
		musicVolume = 10f;
		soundVolume = 10f;
		playerVoiceEnabled = true;
	}

	public void LoadKeyboardSettings()
	{
		LoadAndUpgradeKeyboardKey("KeyJump", ref jumpKey, Key.Z);
		LoadAndUpgradeKeyboardKey("KeyAttack", ref attackKey, Key.X);
		LoadAndUpgradeKeyboardKey("KeyDash", ref dashKey, Key.C);
		LoadAndUpgradeKeyboardKey("KeyCast", ref castKey, Key.A);
		LoadAndUpgradeKeyboardKey("KeySupDash", ref superDashKey, Key.S);
		LoadAndUpgradeKeyboardKey("KeyDreamnail", ref dreamNailKey, Key.D);
		LoadAndUpgradeKeyboardKey("KeyQuickMap", ref quickMapKey, Key.Tab);
		LoadAndUpgradeKeyboardKey("KeyQuickCast", ref quickCastKey, Key.F);
		LoadAndUpgradeKeyboardKey("KeyTaunt", ref tauntKey, Key.V);
		LoadAndUpgradeKeyboardKey("KeyInventory", ref inventoryKey, Key.I);
		LoadAndUpgradeKeyboardKey("KeyInventoryMap", ref inventoryMapKey, Key.M);
		LoadAndUpgradeKeyboardKey("KeyInventoryJournal", ref inventoryJournalKey, Key.J);
		LoadAndUpgradeKeyboardKey("KeyInventoryTools", ref inventoryToolsKey, Key.Q);
		LoadAndUpgradeKeyboardKey("KeyInventoryQuests", ref inventoryQuestsKey, Key.T);
		LoadAndUpgradeKeyboardKey("KeyUp", ref upKey, Key.UpArrow);
		LoadAndUpgradeKeyboardKey("KeyDown", ref downKey, Key.DownArrow);
		LoadAndUpgradeKeyboardKey("KeyLeft", ref leftKey, Key.LeftArrow);
		LoadAndUpgradeKeyboardKey("KeyRight", ref rightKey, Key.RightArrow);
	}

	private void LoadAndUpgradeKeyboardKey(string prefsKey, ref string setString, Key defaultKey)
	{
		string text = defaultKey.ToString();
		if (!LoadString(prefsKey, ref setString, text))
		{
			setString = text;
		}
	}

	public void SaveKeyboardSettings()
	{
		Platform.Current.LocalSharedData.SetString("KeyJump", jumpKey);
		Platform.Current.LocalSharedData.SetString("KeyAttack", attackKey);
		Platform.Current.LocalSharedData.SetString("KeyDash", dashKey);
		Platform.Current.LocalSharedData.SetString("KeyCast", castKey);
		Platform.Current.LocalSharedData.SetString("KeySupDash", superDashKey);
		Platform.Current.LocalSharedData.SetString("KeyDreamnail", dreamNailKey);
		Platform.Current.LocalSharedData.SetString("KeyQuickMap", quickMapKey);
		Platform.Current.LocalSharedData.SetString("KeyQuickCast", quickCastKey);
		Platform.Current.LocalSharedData.SetString("KeyTaunt", tauntKey);
		Platform.Current.LocalSharedData.SetString("KeyInventory", inventoryKey);
		Platform.Current.LocalSharedData.SetString("KeyInventoryMap", inventoryMapKey);
		Platform.Current.LocalSharedData.SetString("KeyInventoryTools", inventoryToolsKey);
		Platform.Current.LocalSharedData.SetString("KeyInventoryJournal", inventoryJournalKey);
		Platform.Current.LocalSharedData.SetString("KeyInventoryQuests", inventoryQuestsKey);
		Platform.Current.LocalSharedData.SetString("KeyUp", upKey);
		Platform.Current.LocalSharedData.SetString("KeyDown", downKey);
		Platform.Current.LocalSharedData.SetString("KeyLeft", leftKey);
		Platform.Current.LocalSharedData.SetString("KeyRight", rightKey);
	}

	public bool LoadGamepadSettings(GamepadType gamepadType)
	{
		gamepadType = RemapGamepadTypeForSettings(gamepadType);
		if (gamepadType != GamepadType.NONE)
		{
			string key = "Controller" + gamepadType;
			string val = "";
			if (LoadString(key, ref val, ""))
			{
				controllerMapping = JsonUtility.FromJson<ControllerMapping>(val);
				return true;
			}
		}
		return false;
	}

	public void SaveGamepadSettings(GamepadType gamepadType)
	{
		gamepadType = RemapGamepadTypeForSettings(gamepadType);
		string key = "Controller" + gamepadType;
		string text = JsonUtility.ToJson(controllerMapping);
		Platform.Current.LocalSharedData.SetString(key, text);
		LogSavedKey(key, text);
		Platform.Current.LocalSharedData.Save();
	}

	public void ResetGamepadSettings(GamepadType gamepadType)
	{
		gamepadType = RemapGamepadTypeForSettings(gamepadType);
		controllerMapping = new ControllerMapping();
		controllerMapping.gamepadType = gamepadType;
		if (_verboseMode)
		{
			Debug.LogFormat("ResetSettings - {0}", gamepadType);
		}
	}

	private GamepadType RemapGamepadTypeForSettings(GamepadType sourceType)
	{
		GamepadType gamepadType = ((sourceType != GamepadType.SWITCH_PRO_CONTROLLER && (uint)(sourceType - 14) > 1u) ? sourceType : GamepadType.SWITCH_JOYCON_DUAL);
		if (gamepadType != sourceType)
		{
			Debug.LogFormat("Remapped GamepadType from {0} to {1}", sourceType.ToString(), gamepadType.ToString());
		}
		return gamepadType;
	}

	public static bool LoadInt(string key, ref int val, int def)
	{
		if (Platform.Current.LocalSharedData.HasKey(key))
		{
			val = Platform.Current.LocalSharedData.GetInt(key, def);
			if (_verboseMode)
			{
				LogLoadedKey(key, val);
			}
			return true;
		}
		val = def;
		if (_verboseMode)
		{
			LogMissingKey(key);
		}
		return false;
	}

	private static bool HasSetting(string key)
	{
		return Platform.Current.LocalSharedData.HasKey(key);
	}

	private static bool LoadEnum<EnumTy>(string key, ref EnumTy val, EnumTy def)
	{
		int val2 = (int)(object)val;
		bool result = LoadInt(key, ref val2, (int)(object)def);
		val = (EnumTy)(object)val2;
		return result;
	}

	private static bool LoadBool(string key, ref bool val, bool def)
	{
		int val2 = (val ? 1 : 0);
		bool result = LoadInt(key, ref val2, def ? 1 : 0);
		val = val2 > 0;
		return result;
	}

	private static bool LoadFloat(string key, ref float val, float def)
	{
		if (Platform.Current.LocalSharedData.HasKey(key))
		{
			val = Platform.Current.LocalSharedData.GetFloat(key, def);
			if (_verboseMode)
			{
				LogLoadedKey(key, val);
			}
			return true;
		}
		val = def;
		if (_verboseMode)
		{
			LogMissingKey(key);
		}
		return false;
	}

	private static bool LoadString(string key, ref string val, string def)
	{
		if (Platform.Current.LocalSharedData.HasKey(key))
		{
			val = Platform.Current.LocalSharedData.GetString(key, def);
			if (_verboseMode)
			{
				LogLoadedKey(key, val);
			}
			return true;
		}
		val = def;
		if (_verboseMode)
		{
			LogMissingKey(key);
		}
		return false;
	}

	private static void LogMissingKey(string key)
	{
		Debug.LogFormat("LoadSettings - {0} setting not found. Loading defaults.", key);
	}

	private static void LogLoadedKey(string key, int value)
	{
		Debug.LogFormat("LoadSettings - {0} Loaded ({1})", key, value);
	}

	private static void LogLoadedKey(string key, float value)
	{
		Debug.LogFormat("LoadSettings - {0} Loaded ({1})", key, value);
	}

	private static void LogLoadedKey(string key, string value)
	{
		Debug.LogFormat("LoadSettings - {0} Loaded ({1})", key, value);
	}

	private static void LogSavedKey(string key, int value)
	{
		Debug.LogFormat("SaveSettings - {0} Saved ({1})", key, value);
	}

	private static void LogSavedKey(string key, float value)
	{
		Debug.LogFormat("SaveSettings - {0} Saved ({1})", key, value);
	}

	private static void LogSavedKey(string key, string value)
	{
		Debug.LogFormat("SaveSettings - {0} Saved ({1})", key, value);
	}

	public bool CommandArgumentUsed(string arg)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length; i++)
		{
			if (commandLineArgs[i].Equals(arg))
			{
				return true;
			}
		}
		return false;
	}
}
