from dataclasses import dataclass
from Options import Choice, DeathLink, PerGameCommonOptions

class CloneDroneGoal(Choice):
    """
    Goal de la partie :
    - Get Titanium
    - Get Uranium
    - Get Insanium
    """
    display_name = "Goal"
    option_titanium = 1
    option_uranium = 2
    option_insanium = 3
    default = 2

@dataclass
class CloneDroneOptions(PerGameCommonOptions):
    goal: CloneDroneGoal
    death_link: DeathLink