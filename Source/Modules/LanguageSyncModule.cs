extern alias SilksongLoc;
using System;
using HornetInHallownest.Core;
using TeamCherry.Localization;
using HkLanguage = TeamCherry.Localization.Language;
using SLanguage = SilksongLoc::TeamCherry.Localization.Language;

namespace HornetInHallownest.Modules;

public sealed class LanguageSyncModule : ModuleBase {
    public override string Id => "language-sync";

    public override void Initialize() {
        Detour(typeof(HkLanguage), nameof(HkLanguage.SwitchLanguage), OnSwitchLanguage, typeof(LanguageCode));
        SyncLanguage();
    }

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
