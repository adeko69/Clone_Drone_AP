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

class Redo(Range):
    """
    REDO
    """
    display_name = "Number of Redo"
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

class RandomizeStartingWeapon(Choice):
    """
    Randomize the weapon you start each step with (Sword, Bow, Hammer or Spear).
    - None: always start with the sword.
    - Same Per Step: one weapon is drawn once and used for every step.
      It is given as a starting item, and removed from the item pool.
    - Random By Step: every step gets its own weapon (the weapons vary from step to step).
    Unless you start with the sword, the Sword becomes an item to find.
    """
    display_name = "Randomize Starting Weapon"
    option_none = 0
    option_same_per_step = 1
    option_random_by_step = 2
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
    """Every enemy spawns with their personal Armor Bot"""
    display_name = "Armor Bot Invasion"
    default = 0


@dataclass
class CloneDroneOptions(PerGameCommonOptions):
    goal: CloneDroneGoal
    death_link: DeathLink
    redo: Redo
    upgrade_mode: UpgradeMode
    randomize_starting_weapon: RandomizeStartingWeapon
    normal: Normal
    inferno: Inferno
    jetpack: Sprinter
    raptor: Raptor
    random: TotallyRandom
    armor: Armor