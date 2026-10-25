from typing import Dict, List, NamedTuple
from BaseClasses import Location
from .upgrade_data import upgrade_checks_for_step, win_step_name, win_step_location_id

class CloneDroneLocation(Location):
    game: str = "Clone Drone in the Danger Zone"

class LocationData(NamedTuple):
    code: int

BASE_LOCATION_ID = 72000
STRIDE = 40                      # > 33, constant, ne dépend pas du goal
MAX_REDO = 5
GOAL_LEVELS = {1: 15, 2: 23, 3: 33, 4: 4}   # titanium, uranium, insanium, silver

def loc_name(redo: int, lvl: int) -> str:
    return f"Redo {redo} - Level {lvl}"

def loc_id(finished: int, lvl: int) -> int:
    return BASE_LOCATION_ID + finished * STRIDE + lvl

# Nom de location -> step auquel elle appartient (tous types confondus)
LOCATION_STEP: Dict[str, int] = {}
# Nom de location d'achat -> UpgradeType acheté
LOCATION_UPGRADE: Dict[str, str] = {}

location_table: Dict[str, LocationData] = {}

# 1. Niveaux de chaque step
for _r in range(1, MAX_REDO + 1):
    for _l in range(1, 34):
        _name = loc_name(_r, _l)
        location_table[_name] = LocationData(loc_id(_r - 1, _l))
        LOCATION_STEP[_name] = _r

# 2. Achats d'upgrades (un check par niveau, à chaque step)
for _r in range(1, MAX_REDO + 1):
    for _name, _code, _upgrade in upgrade_checks_for_step(_r):
        location_table[_name] = LocationData(_code)
        LOCATION_STEP[_name] = _r
        LOCATION_UPGRADE[_name] = _upgrade

# 3. "Win Step X" : tous les steps sauf le dernier (sa victoire est le Goal)
for _r in range(1, MAX_REDO):
    _name = win_step_name(_r)
    location_table[_name] = LocationData(win_step_location_id(_r))
    LOCATION_STEP[_name] = _r


def get_active_location_names(goal_value: int, redo_value: int) -> List[str]:
    n = GOAL_LEVELS[goal_value]

    # Niveaux : le dernier check = validation de la run (Goal Victory)
    names = [loc_name(r, l) for r in range(1, redo_value + 1) for l in range(1, n + 1)]
    names = names[:-1]

    # Achats d'upgrades de chaque step
    for r in range(1, redo_value + 1):
        names.extend(name for name, _code, _upgrade in upgrade_checks_for_step(r))

    # Win Step : pas pour le dernier step
    names.extend(win_step_name(r) for r in range(1, redo_value))
    return names