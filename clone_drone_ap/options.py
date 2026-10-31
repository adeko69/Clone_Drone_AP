from dataclasses import dataclass
from Options import Choice, DeathLink, Toggle, PerGameCommonOptions, Range

class CloneDroneGoal(Choice):
    """
    Goal:
    - Get Titanium
    - Get Uranium
    - Get Insanium
    - Get Silver (debug, 4 levels)
    """
    display_name = "Goal"
    option_titanium = 1
    option_uranium = 2
    option_insanium = 3
    option_silver = 4
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
    Unless you start with the sword, the Sword becomes an item to find, and you start the step without it.
    The unlock of the weapon you start with is always given as a starting item
    (with a Global upgrade mode and Random By Step, only for the first step).
    """
    display_name = "Randomize Starting Weapon"
    option_none = 0
    option_same_per_step = 1
    option_random_by_step = 2
    default = 0

class StepIsStepLocked(Toggle):
    """
    Only used with the Per Step upgrade modes.
    Items of step N can only be found in the locations of step N (instead of steps 1 to N).
    """
    display_name = "Step Items are Step Locked"
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


@dataclass
class CloneDroneOptions(PerGameCommonOptions):
    goal: CloneDroneGoal
    death_link: DeathLink
    upgrade_mode: UpgradeMode
    randomize_starting_weapon: RandomizeStartingWeapon
    skillpoint_price: SkillpointPrice
    overpriced_shop: OverpricedShop
    steps: Steps
    step_is_step_locked: StepIsStepLocked
    multi_step_keys: MultiStepKeys
    normal: Normal
    inferno: Inferno
    jetpack: Sprinter
    raptor: Raptor
    random: TotallyRandom
    armor: Armor
    levelup: LevelUp
    easymode: EasyMode