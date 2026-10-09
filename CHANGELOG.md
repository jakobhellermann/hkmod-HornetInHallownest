# Changelog

## _Unreleased_

- Fix a new game showing tools and items Hornet had in the game played before it
- Fix a down attack carried through a room transition staying armed and pogoing off the first thing Hornet touches
- Fix Hornet's masks never appearing in a new game
- Make Hornet's Compass show her on the map and her Magnetite Brooch pull in geo, in place of the Knight's Wayward
  Compass and Gathering Swarm
- Show Hornet's own head for the map compass while playing as her, and mark her cocoon's room on the map
- Fix the quick map not opening after Hornet's death until she sat on a bench or left the room
- Fix Hornet's cocoon spawning inside a wall when she dies right next to it
- Make Needolin enter a dream only once it has spent its first silk, like charging the Dream Nail, instead of right away
- Make Sylphsong restore Hornet's silk while she rests at a bench, at the pace of Silksong's benches
- Give Hornet plain Needolin from the start; the Dream Nail unlocks Elegy of the Deep (Needolin holding down), which
  acts as the Dream Nail, while any song still drains a dreamer from inside its dream
- Add Beastling Call (Needolin holding up), unlocked with the Dream Gate: it takes Hornet to the stag station the Stag
  waits at, wherever HK would let her set a Dream Gate
- Show Hornet's Silksong UI in the language picked in HK instead of always in English
- Fix Hornet's sprint bonking off the City of Tears lifts, and her stopping at other walls only the Knight could pass
- Fix soul totems filling Hornet's whole silk spool: soul from totems, Grubsong and Dream Nail hits turns into silk at
  the rate of a nail hit
- Fix bounce shrooms launching Hornet lower than the Knight when she pogos off them, short of ledges she needs
- Fix Hornet slipping out of bounds by vaulting or clambering at a wall whose top meets a low ceiling
- Give Hornet the Crystal Heart: hold up + Clawline on the ground or a wall to charge, release to fly

## v0.4.0

- Fix camera sticking to Hornet, jerking on ledges and not looking up/down
- Fix masks and soul vessels picked up as Hornet not counting (missing silk spools)
- Fix softlock when picking up the Crystal Heart as Hornet
- Fix softlock in the City of Tears fountain cutscene with Hornet
- Hide Hornet's HUD during cutscenes and dialogue, like the Knight's
- Play Hornet's own wake-up animation when arriving in a dream, or kneel and stand up for Hall of Gods fights, instead of
  her walking around invisible
- Fix Hornet's silk spool showing empty or frozen chunks after loading a save or switching heroes
- Fix bounce shrooms not bouncing Hornet when she lands on them
- Fix Hornet's side and up slashes passing through bounce shrooms instead of knocking her back
- Fix Gruz Mother never waking up and her fight not starting when hit by Hornet
- Fix the City of Tears toll bench room staying dark when Hornet enters it from the left
- Fix the dark room light, Gathering Swarm and the map compass staying where the Knight was instead of following Hornet
- Fix Hornet clinging to and climbing walls the Knight can't, such as dreamer and Godhome blockers
- Fix Knight effects frozen mid-animation, such as Monarch Wings, staying visible while playing as Hornet
- Fix Plasmium Phial and lifeblood cocoons not granting Hornet blue health after a death or hero switch
- Fix invisible contact damage from boss detection zones, such as Paintmaster Sheo's and Failed Champion's
- Fix Hornet's HUD staying hidden after a Hall of Gods fight, and let her lie down and get up at the statue like the Knight
- Fix the dreamer prompt asking Hornet to Focus instead of playing Needolin
- Fix Hornet moving freely while a dreamer is absorbed and during the mask-break cutscene, and let her lie down and get
  up afterwards like the Knight
- Hornet now dies with her death animation, leaves her geo in a cocoon where she fell and breaks her silk spool until it's
  recovered, except in dreams, the White Palace and Godhome
- Fix Hornet's silk spool showing an empty first cell after quitting to the menu and loading again
- Fix Hornet moving freely during the Abyss Shriek cutscene, and the cutscene leaving her in the Knight's appearance
- Fix the Shaman Crest bind halo staying on Hornet after the bind is interrupted by damage
- Fix nail hits on enemies not spawning the Reaper Crest's silk orbs, healing Hornet in the Beast Crest's rage or
  filling the evolved Hunter Crest's combo
- Fix Wanderer Crest critical hits on enemies dealing normal damage without their effect and sound
- Fix the frame around Hornet's silk spool not changing with her crest and not showing the evolved Hunter Crest's combo
  after her HUD was hidden
- Fix the Beast Crest's rage not tinting the screen edges red
- Fix the camera never shaking from Hornet's binds, attacks and other Silksong effects
- Fix Hornet's tool icons piling up in one slot after switching to a crest with more tools equipped
- Make the Architect Crest's quick craft (up + bind) refill Hornet's tools for free, like the bench, since Hallownest has
  no shell shards to pay with
- Fix Hornet falling through the floor when leaving a dream (dream bosses, Lurien)
- Fix repeated white flashes when holding Needolin while it can't be played
- Fix Magnetite Dice block sound ignoring the volume settings
- Fix Hornet's remaining sounds (e.g. Reaper crest mode) ignoring the volume settings
- Fix black screen softlock when entering a room after a collision-ignoring object was unloaded early
- Fix Trial of the Fool respawn platform not appearing after a spike hit as Hornet
- Allow opening the quick map as Hornet outside of benches
- Allow opening the inventory map as Hornet by double-tapping quick map, like the Knight
- Allow reaching the map from Hornet's inventory by paging past the last pane, and paging back from it
- Show the map pane icon in Hornet's inventory once the Knight has a map
- Fix the controller's X/Y menu buttons (e.g. changing crest) doing nothing in Hornet's inventory
- Fix button prompts in Hornet's UI showing "None" or nothing instead of the bound key/button
- Fix the button that closes the pause menu also triggering Hornet's bind

Huge shoutout to @The1fEst who contributed these changes.

## v0.3.0

- Prevent HK iframes eating damage dealt in quick succession
- Fix bind/taunt being broken after dialogue
- Fix not being able to open inventory on controller
- Fix not being able to press R3 for taunt on controller
- Inherit HK controller bindings
- Update inherited bindings when changing them
- Fix soul master activating both phase 1 and 2 in godhome
- Fix mod not loading on upgrade or silksong path change on windows

## v0.2.0

- Fix clawline not bouncing off of spikes
- Fix non-hunters crest not being able to pogo
- Fix unbalanced loud audio for certain sound effects
- Fix having to re-equip tools after restart
- Grant tool pouches and crafting kits for nail upgrades
- Respect Ascended & Radiant difficulties in godhome
- Fix silk regeneration not working after re-entering a save
- Clear active tools on new savefile creation
- Refresh silkspool HUD after gaining an upgrade

## v0.1.0

- Initial release 🎉
