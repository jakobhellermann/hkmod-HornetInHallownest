# Second try
- blue hearts
- big door oob

# Bugs

## Cosmetic
- dreamer:
    - needolin on a dreamer fires instantly (the dream-nail hitbox is armed the moment the tune starts) — should have a wind-up instead of triggering immediately
    - freeing prompt shows "HOLD A to Focus" (HK's Focus binding) — for Hornet it should read the needolin key ("HOLD D")
    - after a dream-scene exit Hornet's animator stays on the airborne/warp-in clip until any action (dash/attack) — HK's "Dream Return" get-up (StartAnimationControl) doesn't run for her
    - can't silkspear
    - dreamer walls are climbable
- audio volume not tied to settings
- camera positioning
- death animation
- fury area glow doesnt stop
- hornet bench location
- VS/dive animation shows knight always
- mask shard pickup animation
- hollow knight statue cutscene
- kings brand cutscene

## Integration
- XX hardsaves
- XX acid swimming should float
- XX game finish animation not shown
- dream-scene input softlock: dream sequences (enter cutscene / dreamer confrontation / textbox) call HeroController.IgnoreInput on Hornet (via global "Hero") and never restore AcceptInput → acceptingInput stuck false, can't move (controlReqlinquished stays false, so StuckControlNet doesn't catch it). Happens on dream entry and after textbox interaction. Fix options: extend StuckControlNet to also restore stuck acceptingInput, or trace each dream sequence's IgnoreInput/AcceptInput for Hornet
- OOB on scene transitions
- elevator/lift transitions: Knight strands at shaft top, Hornet falls to bottom, camera-lock stuck up top (dark)
- spring water
- swimming
- ability sync
- hornet falling after level exit
- dash into elevator: bump
- sprint-attack (dash-stab) misses HK trigger-breakables (e.g. Watcher Knights chandelier rope, Ruins2_03)
- lantern immer aktiv
- XX double check descending dark, cdash pickup
- weaver entry

# Balancing
- soul totem <-> silk

## Nive to have
- hud sync? geo on hornet
- dirtmouth slowdown
- crouch mode for knight height:
    - thorns, crossroads shard, greenpath entry, fungal cornifer, godpixel peaks, crusher grub, frogs, catacombs, geo egg drop, mato down, lots in deepnest

## Intermittent / needs debug
- cannot bench sometimes

## Double check
- scream control dung defender - fixed?
- acid before ismas
