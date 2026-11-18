from dataclasses import dataclass
from Options import Choice, DeathLink, Toggle, PerGameCommonOptions, Range

class CloneDroneGoal(Choice):
    """
    Goal:
    - Reach Titanium (Step)
    - Reach Uranium (Step)
    - Reach Insanium (Step)
    - Get Scrap (Scrap Collector): reach the Required Scrap. Steps, Multi-Step Keys and level checks are disabled,
      and the Upgrade Mode is forced to Global Ungrouped.
      The options of the "Step (Reach Goal)" group are ignored.
    """
    display_name = "Goal"
    option_titanium = 1
    option_uranium = 2
    option_insanium = 3
    option_scrap_collector = 4
    default = 2

class Steps(Range):
    """
    Number of steps. A step is a full run to the goal: you have to win every step to complete the game.
    """
    display_name = "Number of Steps"
    range_start = 1
    range_end = 5
    default = 1

class UpgradeMode(Choice):
    """
    How upgrade items are handed out.
    - Global Grouped: every upgrade is one item, usable in every step (classic behaviour).
    - Global Ungrouped: same, but every grouped item (the ones with a "+", and Riding) is split in two.
      Example: "Armor+Repair" becomes "Armor" and "Repair".
    - Per Step Grouped: every step has its own copy of each upgrade, usable only during that step.
      Upgrades of step N can only be found in the locations of steps 1 to N.
    - Per Step Ungrouped: same as Per Step Grouped, with the grouped items split in two.
    """
    display_name = "Upgrade Mode"
    option_global_grouped = 0
    option_global_ungrouped = 1
    option_per_step_grouped = 2
    option_per_step_ungrouped = 3
    default = 0

class SkillpointPrice(Range):
    """
    Starting skillpoint price multiplier.
    Every upgrade costs (base price x current multiplier).
    Each Shop Discount item found lowers the multiplier by 1 (minimum x1).
    The number of Shop Discount items in the pool equals this value.
    """
    display_name = "Skillpoint Price"
    range_start = 1
    range_end = 5
    default = 3

# --- Fillers : un poids de 0 à 10 par filler (0 = jamais généré, 1 = poids minimal, 10 = poids maximal) ---
# Les places vides du pool sont remplies par un tirage pondéré entre les fillers.

class FillerFreeClone(Range):
    """
    Weight of the "Free Clone" filler (a free clone at the start of a level).
    Always generated: from 1 (minimal weight) to 10 (maximal weight). 0 does not exist,
    so there is always at least one filler to fill the empty locations.
    At most 100 are generated; further draws become a "Small Scrap" (nothing, or +10 scrap with the scrap system).
    """
    display_name = "Free Clone"
    range_start = 1
    range_end = 10
    default = 5

class FillerNextUpgradeFree(Range):
    """Weight of the "Next Upgrade Free" filler. 0 = never generated, 1 = minimal weight, 10 = maximal weight."""
    display_name = "Next Upgrade Free"
    range_start = 0
    range_end = 10
    default = 5

class FillerStartWithArmor(Range):
    """
    Weight of the "3 Start With Armor" filler (33% become the "6 Start With Armor" variant).
    0 = never generated, 1 = minimal weight, 10 = maximal weight.
    """
    display_name = "3 Start With Armor"
    range_start = 0
    range_end = 10
    default = 5

class FillerInfiniteArmor(Range):
    """
    Weight of the "60s of Infinite Armor" filler (33% become the "120s of Infinite Armor" variant).
    0 = never generated, 1 = minimal weight, 10 = maximal weight.
    """
    display_name = "60s of Infinite Armor"
    range_start = 0
    range_end = 10
    default = 5

class FillerEnergyBoost(Range):
    """Weight of the "Progressive Energy Boost" filler. 0 = never generated, 1 = minimal weight, 10 = maximal weight.
    At most 100 are generated; further draws become a "Small Scrap"."""
    display_name = "Progressive Energy Boost"
    range_start = 0
    range_end = 10
    default = 5

class FillerWeaponBoost(Range):
    """Weight of the "Progressive Weapon Boost" filler. 0 = never generated, 1 = minimal weight, 10 = maximal weight.
    At most 100 are generated; further draws become a "Small Scrap". The sword size is capped at x3."""
    display_name = "Progressive Weapon Boost"
    range_start = 0
    range_end = 10
    default = 5

class FillerScrap(Range):
    """
    Weight of the "Scrap" filler: 500 scrap (20% become "Big Scrap": 5000 scrap at once).
    Only generated with the Scrap Collector goal or Scrap Anywhere. In a Reach goal without scrap it is useless,
    so it is never generated, whatever the weight.
    0 = never generated, 1 = minimal weight, 10 = maximal weight.
    """
    display_name = "Scrap"
    range_start = 0
    range_end = 10
    default = 5

class FillerComboTime(Range):
    """
    Weight of the "Combo Time" filler: +0.5s on every combo (kill) and +1s on the 10 seconds after a new game, per copy.
    Only generated if the scrap system is active (Scrap Collector or Scrap Anywhere).
    0 = never generated, 1 = minimal weight, 10 = maximal weight.
    """
    display_name = "Combo Time"
    range_start = 0
    range_end = 10
    default = 5

class FillerPlainScrapGain(Range):
    """
    Weight of the "Plain Scrap Gain" filler: +1 scrap per kill (before the multipliers), per copy.
    Only generated if the scrap system is active (Scrap Collector or Scrap Anywhere).
    0 = never generated, 1 = minimal weight, 10 = maximal weight.
    """
    display_name = "Plain Scrap Gain"
    range_start = 0
    range_end = 10
    default = 5

class ScrapCoop(Range):
    """
    Co-op: share a part of the scrap you earn with the other co-op players of the room.
    You keep 100% of what you earn; every other co-op player ALSO receives a copy of the shared part
    (the scrap is duplicated, not taken from you). Works with the Scrap Collector goal and with Scrap Anywhere.
    - 0: off (you neither share nor receive).
    - 1: 20%   - 2: 40%   - 3: 60%   - 4: 80%   - 5: 100%
    Only players with a value above 0 take part. The percentage is the one of the player who earns the scrap.
    Scrap coming from "Scrap" / "Big Scrap" items is never shared.
    """
    display_name = "Scrap Coop"
    range_start = 0
    range_end = 5
    default = 0

class IncludeBowInWeaponRandomization(Toggle):
    """
    Only used with "Randomize Starting Weapon" (Same Per Step / Random By Step / Every Death).
    On: the Bow can be drawn as a starting weapon.
    Off: the Bow is never drawn (0% chance).
    """
    display_name = "Include Bow in Weapon Randomization"
    default = 1

class AdditionalShopDiscount(Toggle):
    """
    On: the pool holds as many Shop Discount items as the Skillpoint Price (default behaviour).
    Off: only (Skillpoint Price - 1) Shop Discount items are generated,
    so the multiplier can never go down to x1 through discounts alone.
    """
    display_name = "Additional Shop Discount"
    default = 1

class ShopDiscountGeneration(Choice):
    """
    Where the Shop Discount items are placed.
    - Proportional: spread evenly over the whole run (25 %, 50 %, 75 % with 3 discounts).
    - Random: placed on any random location of the game (levels and upgrade purchases alike).
    """
    display_name = "Shop Discount Chance Generation"
    option_proportional = 0
    option_random = 1
    default = 0

class StepShopDiscountPerStep(Choice):
    """
    Only used with the Per Step upgrade modes (and more than one step).
    Controls in which steps a "Shop Discount (Step N)" item is generated.
    Let H = half of the Shop Discount items, rounded up (5 discounts -> 3, 3 discounts -> 2),
    and S = the step in which you find the H-th Shop Discount.
    - Never: no Step Shop Discount at all.
    - Before 50%: steps 1 to S-1 generate one (step S itself does not), then it stops.
    - After 50%: steps 1 to S generate one (step S included), then it stops.
    - Always: every step (default behaviour).
    """
    display_name = "Step Shop Discount Per Step"
    option_never = 0
    option_before_50_percent = 1
    option_after_50_percent = 2
    option_always = 3
    default = 3


class OverpricedShop(Range):
    """
    Make X upgrades being +3 skillpoint. Randomized on every death.
    0 disable the feature.
    """
    display_name = "Overpriced Shop"
    range_start = 0
    range_end = 10
    default = 0

class RandomizeStartingWeapon(Choice):
    """
    Randomize the weapon you start each step with (Sword, Bow, Hammer or Spear).
    - None: always start with the sword.
    - Same Per Step: one weapon is drawn once and used for every step.
      It is given as a starting item, and removed from the item pool.
    - Random By Step: every step gets its own weapon (the weapons vary from step to step).
    - Every Death: a new weapon is drawn by the game at EVERY reset (each death and each new step).
      It is only a loan for that life: nothing is given as a starting item, the Sword becomes an item to find,
      and every weapon unlock purchase check exists (you can buy and switch weapons during the run).
    Unless you start with the sword, the Sword becomes an item to find, and you start the step without it.
    The unlock of the weapon you start with is always given as a starting item
    (with a Global upgrade mode and Random By Step, only for the first step).
    Not applicable to Every Death (no starting item, the weapon is only lent for the current life).
    """
    display_name = "Randomize Starting Weapon"
    option_none = 0
    option_same_per_step = 1
    option_random_by_step = 2
    option_every_death = 3
    default = 0

class StepIsStepLocked(Toggle):
    """
    Only used with the Per Step upgrade modes.
    Items of step N can only be found in the locations of step N (instead of steps 1 to N).
    """
    display_name = "Step Items are Step Locked"
    default = 0

class RandomizeLines(Toggle):
    """
    If you want the Commentatron and other NPCs lines randomized.
    """
    display_name = "Randomize Voice Lines"
    default = 0

class Normal(Toggle):
    """
    Add Normal Case Scenario (No scenario). If there are no scenarios, then normal is turned on automatically
    """
    display_name = "Normal"
    default = 1

class Inferno(Toggle):
    """
    Set inferno mode: all foes spawns with fire weapon
    """
    display_name = "Inferno"
    default = 0

class Sprinter(Toggle):
    """Replace all foes with Jetpack boys (MK1, MK2, Bow)."""
    display_name = "Jetpack Everywhere"
    default = 0

class Raptor(Toggle):
    """Put every enemy on raptor"""
    display_name = "Riding Raptor"
    default = 0

class TotallyRandom(Toggle):
    """Every enemy are random"""
    display_name = "True Enemy Randomness"
    default = 0

class Armor(Toggle):
    """Donne de l'armure"""
    display_name = "Armored"
    default = 0


class LevelUp(Toggle):
    """Most of enemies becomes an harder version of themselves."""
    display_name = "Level Up"
    default = 0

class EasyMode(Toggle):
    """Most of enemies becomes an easier version of themselves"""
    display_name = "Easy Mode"
    default = 0


class Undead(Toggle):
    """Enemies come back as undead."""
    display_name = "Undead"
    default = 0

class Doggos(Toggle):
    """Transform enemy into spidertron, sawdog or fire raptor. Boss are included at 1% each."""
    display_name = "Doggos"
    default = 0

class Summoners(Toggle):
    """Only summoners. gl"""
    display_name = "Summoners"
    default = 0

class ConfusionInferno(Range):
    """Chance (in %, 0 = disabled) of mixing "Inferno" into other scenarios."""
    display_name = "Confusion Inferno"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionSprinter(Range):
    """Chance (in %, 0 = disabled) of mixing "Jetpack Everywhere" into other scenarios."""
    display_name = "Confusion Jetpack Everywhere"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionRaptor(Range):
    """Chance (in %, 0 = disabled) of mixing "Riding Raptor" into other scenarios."""
    display_name = "Confusion Riding Raptor"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionTotallyRandom(Range):
    """Chance (in %, 0 = disabled) of mixing "True Enemy Randomness" into other scenarios."""
    display_name = "Confusion True Enemy Randomness"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionArmor(Range):
    """Chance (in %, 0 = disabled) of mixing "Armored" into other scenarios."""
    display_name = "Confusion Armored"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionLevelUp(Range):
    """Chance (in %, 0 = disabled) of mixing "Level Up" into other scenarios."""
    display_name = "Confusion Level Up"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionEasyMode(Range):
    """Chance (in %, 0 = disabled) of mixing "Easy Mode" into other scenarios."""
    display_name = "Confusion Easy Mode"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionUndead(Range):
    """Chance (in %, 0 = disabled) of mixing "Undead" into other scenarios."""
    display_name = "Confusion Undead"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionDoggos(Range):
    """Chance (in %, 0 = disabled) of mixing "Doggos" into other scenarios."""
    display_name = "Confusion Doggos"
    range_start = 0
    range_end = 50
    default = 0

class ConfusionSummoners(Range):
    """Chance (in %, 0 = disabled) of mixing "Summoners" into other scenarios."""
    display_name = "Confusion Summoners"
    range_start = 0
    range_end = 50
    default = 0


class MultiStepKeys(Choice):
    """
    Keys required to validate "Win Step".
    - No: no key, a step is won as soon as its last level is cleared.
    - Yes without the last: one Progressive Key per step, except the last one (N steps -> N-1 keys).
    - Yes including the last: the Goal also needs a key (N steps -> N keys).
    Key N is always found inside step N. Without it, clearing the final level does nothing: the game goes on.
    """
    display_name = "Multi-Step Keys"
    option_no = 0
    option_yes_without_last = 1
    option_yes_including_last = 2
    default = 0


class StepKeyLocation(Choice):
    """
    Only used with Multi-Step Keys. Where the Progressive Keys can be generated.
    - Everywhere In Local: key N is placed in step N of your own world, on a level or an upgrade purchase (default).
    - Not In Level Checks In Local: key N is placed in step N of your own world, on upgrade purchases only.
      If step N has no free purchase left, the key falls back to the pool (never on a level check).
    - Not In Level Checks Anywhere: keys go to the pool: they can end up in other players' worlds,
      or on your upgrade purchases, but never on your level checks.
    - Absolutely Anywhere: keys go to the pool and can be anywhere, level checks included
      (never on a "Win Step", which would deadlock).
    Keys placed through the pool are positioned by the fill, which respects the access rules.
    """
    display_name = "Step Key Location"
    option_everywhere_in_local = 0
    option_not_in_level_checks_in_local = 1
    option_not_in_level_checks_anywhere = 2
    option_absolutely_anywhere = 3
    default = 0

class ScrapAnywhere(Toggle):
    """
    Only used with the Reach Titanium/Uranium/Insanium goals.
    On: the Scrap Collector scrap system is also active in Step mode: the Scrap Location checks exist
    and are obtained when the Required Scrap are passed (the goal itself stays the Step victory).
    The Scrap Collector goal always uses the scrap system.
    """
    display_name = "Scrap Anywhere"
    default = 0

class HowMuchScrap(Choice):
    """
    Scrap required to win the game with the Scrap Collector goal.
    Also the upper bound of the Scrap Location checks, if the scrap system is active.
    With 1000k, 3 copies of "Combo Strength" and of "More Scrap" are generated; with 10000k, 6 copies of each (1 with 100k).
    """
    display_name = "Required Scrap"
    option_100k = 0
    option_1000k = 1
    option_10000k = 2
    default = 0

class LocationPerScrapPercent(Range):
    """
    Percentage of the possible Scrap Location checks that are created (rounded up, minimum 1 check).
    The possible checks are one every 1k, up to the Required Scrap amount (the goal itself is not a check).
    Which multiples of 1k are kept is random, never twice the same.
    Only used if the scrap system is active (Scrap Collector goal, or Scrap Anywhere).
    """
    display_name = "Scrap Location Percent"
    range_start = 1
    range_end = 100
    default = 10


class ScrapEarlyChecks(Range):
    """
    Scrap Collector goal only: number of "Early Checks" (low scrap checks that the fill fills with progression items first).
    Each one sits at a random multiple of 10 scrap between 100 and 9990 included (never a multiple of 1000,
    those belong to the normal Scrap Location checks), never twice the same.
    """
    display_name = "Early Checks"
    range_start = 1
    range_end = 100
    default = 20

@dataclass
class CloneDroneOptions(PerGameCommonOptions):
    goal: CloneDroneGoal
    death_link: DeathLink
    upgrade_mode: UpgradeMode
    randomize_starting_weapon: RandomizeStartingWeapon
    include_bow_in_weapon_randomization: IncludeBowInWeaponRandomization
    skillpoint_price: SkillpointPrice
    additional_shop_discount: AdditionalShopDiscount
    shop_discount_generation: ShopDiscountGeneration
    step_shop_discount_per_step: StepShopDiscountPerStep
    overpriced_shop: OverpricedShop
    steps: Steps
    step_is_step_locked: StepIsStepLocked
    multi_step_keys: MultiStepKeys
    step_key_location: StepKeyLocation
    randomize_voices: RandomizeLines
    filler_free_clone: FillerFreeClone
    filler_next_upgrade_free: FillerNextUpgradeFree
    filler_start_with_armor: FillerStartWithArmor
    filler_infinite_armor: FillerInfiniteArmor
    filler_energy_boost: FillerEnergyBoost
    filler_weapon_boost: FillerWeaponBoost
    filler_scrap: FillerScrap
    filler_combo_time: FillerComboTime
    filler_plain_scrap_gain: FillerPlainScrapGain
    normal: Normal
    inferno: Inferno
    jetpack: Sprinter
    raptor: Raptor
    random: TotallyRandom
    armor: Armor
    levelup: LevelUp
    easymode: EasyMode
    undead: Undead
    doggos: Doggos
    summoners: Summoners
    confusion_inferno: ConfusionInferno
    confusion_jetpack: ConfusionSprinter
    confusion_raptor: ConfusionRaptor
    confusion_random: ConfusionTotallyRandom
    confusion_armor: ConfusionArmor
    confusion_levelup: ConfusionLevelUp
    confusion_easymode: ConfusionEasyMode
    confusion_undead: ConfusionUndead
    confusion_doggos: ConfusionDoggos
    confusion_summoners: ConfusionSummoners
    scrap_anywhere: ScrapAnywhere
    required_scrap: HowMuchScrap
    scrap_locations: LocationPerScrapPercent
    scrap_early_checks: ScrapEarlyChecks
    scrap_coop: ScrapCoop