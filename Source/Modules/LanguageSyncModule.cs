extern alias SilksongLoc;
using System;
using HornetInHallownest.Core;
using TeamCherry.Localization;
using HkLanguage = TeamCherry.Localization.Language;
using SLanguage = SilksongLoc::TeamCherry.Localization.Language;

namespace HornetInHallownest.Modules;

public sealed class LanguageSyncModule : ModuleBase {
    // Silksong text on a sheet named with this prefix reads HK's sheet named by the rest, e.g. "HK UI" reads HK's "UI".
    internal const string HkSheetPrefix = "HK ";

    public override string Id => "language-sync";

    public override void Initialize() {
        Detour(typeof(HkLanguage), nameof(HkLanguage.SwitchLanguage), OnSwitchLanguage, typeof(LanguageCode));
        Detour(typeof(SLanguage), nameof(SLanguage.Has), OnLanguageHas, typeof(string), typeof(string));
        Detour(typeof(SLanguage), nameof(SLanguage.Get), OnLanguageGet, typeof(string), typeof(string));
        SyncLanguage();
    }

    private static bool OnLanguageHas(Func<string, string?, bool> orig, string key, string? sheetTitle) =>
        IsHkSheet(sheetTitle)
            ? HkLanguage.Has(key, sheetTitle!.Substring(HkSheetPrefix.Length))
            : orig(key, sheetTitle);

    private static string OnLanguageGet(Func<string, string?, string> orig, string key, string? sheetTitle) =>
        IsHkSheet(sheetTitle)
            ? HkLanguage.Get(key, sheetTitle!.Substring(HkSheetPrefix.Length))
            : orig(key, sheetTitle);

    private static bool IsHkSheet(string? sheetTitle) =>
        sheetTitle != null && sheetTitle.StartsWith(HkSheetPrefix, StringComparison.Ordinal);

    private static bool OnSwitchLanguage(Func<LanguageCode, bool> orig, LanguageCode code) {
        var switched = orig(code);
        SyncLanguage();
        return switched;
    }

    // Both games share the language codes.
    private static void SyncLanguage() {
        var code = HkLanguage.CurrentLanguage().ToString();
        if (SLanguage.CurrentLanguage().ToString() == code || Array.IndexOf(SLanguage.GetLanguages(), code) < 0) return;
        // Silksong's sheets load through Resources, which HK's own Languages/* shadow outside this context.
        using (SilksongContext.Enter()) {
            SLanguage.SwitchLanguage(code);
        }
    }
}
