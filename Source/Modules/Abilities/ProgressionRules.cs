using System.Linq;

namespace HornetInHallownest.Modules;

internal static class ProgressionRules {
    internal readonly record struct Flag(string Field, int Min = 0) {
        internal bool IsSet(PlayerData pd) => Min == 0 ? pd.GetBool(Field) : pd.GetInt(Field) >= Min;
    }

    internal sealed record Rule(string Reward, Flag[] Flags, bool AnyFlag = false) {
        internal bool IsEarned(PlayerData pd) {
            // Godseeker mode starts with the Knight's whole kit but none of the events that earn the rest.
            if (pd.bossRushMode) return true;
            return Flags.Length == 0 || (AnyFlag ? Flags.Any(f => f.IsSet(pd)) : Flags.All(f => f.IsSet(pd)));
        }
    }

    internal const string StartingCrest = "Hunter";

    internal static Flag Charm(int id) => new($"gotCharm_{id}");

    private static Rule Earned(string reward, params Flag[] flags) => new(reward, flags);

    internal static readonly Rule[] ToolRules = [
        Earned("Straight Pin", new Flag(nameof(PlayerData.falseKnightDefeated))),
        Earned("Tri Pin", Charm(25)), // Fragile Strength
        Earned("Sting Shard", new Flag(nameof(PlayerData.killedMimicSpider))), // Nosk
        Earned("Tack", new Flag(nameof(PlayerData.hasAcidArmour))), // Isma's Tear
        Earned("Harpoon", Charm(15)), // Heavy Blow
        Earned("Curve Claws", Charm(13)), // Mark of Pride
        Earned("Curve Claws Upgraded", new Flag(nameof(PlayerData.killedTraitorLord))),
        Earned("Shakra Ring", new Flag(nameof(PlayerData.killedMegaMossCharger))),
        Earned("Pimpilo", Charm(10)), // Defender's Crest
        Earned("Conch Drill", Charm(28)), // Shape of Unn
        Earned("WebShot Forge", new Flag(nameof(PlayerData.maskBrokenHegemol))),
        Earned("WebShot Architect", new Flag(nameof(PlayerData.maskBrokenMonomon))),
        Earned("WebShot Weaver", new Flag(nameof(PlayerData.maskBrokenLurien))),
        Earned("Silk Snare", Charm(22)), // Glowing Womb
        Earned("Screw Attack", new Flag(nameof(PlayerData.hasLantern))),
        Earned("Cogwork Saw", new Flag(nameof(PlayerData.royalCharmState), 3)), // Kingsoul
        Earned("Cogwork Flier", new Flag(nameof(PlayerData.grimmChildLevel), 3)), // Grimmchild's third form
        Earned("Rosary Cannon", new Flag(nameof(PlayerData.fragileGreed_unbreakable))),
        Earned("Lightning Rod", new Flag(nameof(PlayerData.killedMegaJellyfish))), // Uumuu
        Earned("Flintstone", new Flag(nameof(PlayerData.killedGrimm))), // Troupe Master Grimm
        Earned("Lifeblood Syringe", Charm(8)), // Lifeblood Heart
        Earned("Flea Brew", Charm(32)), // Quick Slash

        Earned("Silk Spear", new Flag(nameof(PlayerData.fireballLevel), 1)),
        Earned("Silk Charge", new Flag(nameof(PlayerData.fireballLevel), 2)), // Sharpdart
        Earned("Parry", new Flag(nameof(PlayerData.quakeLevel), 1)), // Cross Stitch
        Earned("Silk Bomb", new Flag(nameof(PlayerData.quakeLevel), 2)), // Rune Rage
        Earned("Thread Sphere", new Flag(nameof(PlayerData.screamLevel), 1)), // Thread Storm
        Earned("Silk Boss Needle", new Flag(nameof(PlayerData.screamLevel), 2)), // Pale Nails

        Earned("Bell Bind", Charm(5)), // Baldur Shell
        Earned("Quickbind", Charm(7)), // Quick Focus
        Earned("Multibind", Charm(34)), // Deep Focus
        Earned("Reserve Bind", Charm(27)), // Joni's Blessing
        Earned("White Ring", Charm(20)), // Soul Catcher
        Earned("Spool Extender", Charm(21)), // Soul Eater
        Earned("Mosscreep Tool 1", Charm(3)), // Grubsong
        Earned("Mosscreep Tool 2", Charm(35)), // Grubberfly's Elegy
        Earned("Revenge Crystal", Charm(12)), // Thorns of Agony
        Earned("Brolly Spike", Charm(16)), // Sharp Shadow
        Earned("Dazzle Bind", Charm(38)), // Dreamshield
        Earned("Dazzle Bind Upgraded", new Flag(nameof(PlayerData.dreamNailUpgraded))),
        Earned("Flea Charm", Charm(11)), // Flukenest
        Earned("Fractured Mask", Charm(23)), // Fragile Heart
        Earned("Longneedle", Charm(18)), // Longnail
        Earned("Pinstress Tool", Charm(26)), // Nailmaster's Glory
        Earned("Poison Pouch", Charm(17)), // Spore Shroom
        Earned("Quick Sling", Charm(19)), // Shaman Stone
        new("Wisp Lantern",
            [
                new Flag(nameof(PlayerData.killedGhostAladar)),
                new Flag(nameof(PlayerData.killedGhostXero)),
                new Flag(nameof(PlayerData.killedGhostHu)),
                new Flag(nameof(PlayerData.killedGhostMarmu)),
                new Flag(nameof(PlayerData.killedGhostNoEyes)),
                new Flag(nameof(PlayerData.killedGhostGalien)),
                new Flag(nameof(PlayerData.killedGhostMarkoth))
            ],
            AnyFlag: true),
        Earned("Zap Imbuement", Charm(33)), // Spell Twister
        Earned("Thief Claw", new Flag(nameof(PlayerData.collectorDefeated))),
        Earned(GrimmchildTool.ToolName, Charm(40)),

        Earned("Compass", Charm(2)), // Wayward Compass
        Earned("Rosary Magnet", Charm(1)), // Gathering Swarm
        Earned("Sprintmaster", Charm(37)),
        Earned("Scuttlebrace", Charm(31)), // Dashmaster
        Earned("Wallcling", new Flag(nameof(PlayerData.defeatedMegaBeamMiner))), // Crystal Guardian
        Earned("Weighted Anklet", Charm(14)), // Steady Body
        Earned("Barbed Wire", Charm(6)), // Fury of the Fallen
        Earned("Magnetite Dice", new Flag(nameof(PlayerData.grimmChildLevel), 5)), // Carefree Melody
        Earned("Thief Charm", Charm(24)), // Fragile Greed
        Earned("Dead Mans Purse", new Flag(nameof(PlayerData.jijiShadeOffered))),
        Earned("Musician Charm", Charm(30)), // Dream Wielder
        Earned("Shell Satchel", Charm(39)) // Weaversong
    ];

    internal static readonly Rule[] CrestRules = [
        Earned(StartingCrest),
        Earned("Hunter_v2", new Flag(nameof(PlayerData.journalEntriesCompleted), 60)), // entries encountered
        Earned("Hunter_v3", new Flag(nameof(PlayerData.journalNotesCompleted), 80)), // entries completed
        Earned("Spell",
            new Flag(nameof(PlayerData.fireballLevel), 2),
            new Flag(nameof(PlayerData.quakeLevel), 2),
            new Flag(nameof(PlayerData.screamLevel), 2)),
        Earned("Warrior", new Flag(nameof(PlayerData.maskBrokenHegemol))),
        Earned("Reaper", new Flag(nameof(PlayerData.dreamReward2))),
        Earned("Wanderer", new Flag(nameof(PlayerData.quirrelEpilogueCompleted))),
        Earned("Toolmaster", new Flag(nameof(PlayerData.nailSmithUpgrades), 4)),
        Earned("Witch", Charm(9)) // Lifeblood Core
    ];

    internal static readonly Rule SylphsongRule = Earned("Sylphsong", new Flag(nameof(PlayerData.salubraBlessing)));

    internal static readonly Rule[] LocketRules = [
        Earned("Grubfather 10", new Flag(nameof(PlayerData.grubsCollected), 10)),
        Earned("Grubfather 16", new Flag(nameof(PlayerData.grubsCollected), 16)),
        Earned("Grubfather 23", new Flag(nameof(PlayerData.grubsCollected), 23)),
        Earned("Grubfather 31", new Flag(nameof(PlayerData.grubsCollected), 31)),
        Earned("Grubfather 38", new Flag(nameof(PlayerData.grubsCollected), 38)),
        Earned("Wanderer's Journal", new Flag(nameof(PlayerData.foundTrinket1))),
        Earned("Hallownest Seal", new Flag(nameof(PlayerData.foundTrinket2))),
        Earned("King's Idol", new Flag(nameof(PlayerData.foundTrinket3))),
        Earned("Arcane Egg", new Flag(nameof(PlayerData.foundTrinket4))),
        Earned("Seer 100", new Flag(nameof(PlayerData.dreamReward1))),
        Earned("Seer 300", new Flag(nameof(PlayerData.dreamReward3))),
        Earned("Seer 500", new Flag(nameof(PlayerData.dreamReward4)))
    ];
}
