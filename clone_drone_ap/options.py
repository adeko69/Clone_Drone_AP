from dataclasses import dataclass
from Options import Choice, DeathLink, Toggle, PerGameCommonOptions, Range

class CloneDroneGoal(Choice):
    """
    Goal:
    - Get Titanium
    - Get Uranium
    - Get Insanium
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
    normal: Normal
    inferno: Inferno
    jetpack: Sprinter
    raptor: Raptor
    random: TotallyRandom
    armor: Armor
    