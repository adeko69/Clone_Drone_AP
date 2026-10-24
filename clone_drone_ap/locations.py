from typing import Dict, List, NamedTuple
from BaseClasses import Location

class CloneDroneLocation(Location):
    game: str = "Clone Drone in the Danger Zone"

class LocationData(NamedTuple):
    code: int

BASE_LOCATION_ID = 72000
STRIDE = 40                      # > 33, constant, ne dépend pas du goal
MAX_REDO = 5
GOAL_LEVELS = {1: 15, 2: 23, 3: 33, 4: 4}   # titanium, uranium, insanium

def loc_name(redo: int, lvl: int) -> str:
    return f"Step {redo} - Level {lvl}"

def loc_id(finished: int, lvl: int) -> int:
    return BASE_LOCATION_ID + finished * STRIDE + lvl

location_table: Dict[str, LocationData] = {
    loc_name(r, l): LocationData(loc_id(r - 1, l))
    for r in range(1, MAX_REDO + 1) for l in range(1, 34)
}

def get_active_location_names(goal_value: int, redo_value: int) -> List[str]:
    n = GOAL_LEVELS[goal_value]
    names = [loc_name(r, l) for r in range(1, redo_value + 1) for l in range(1, n + 1)]
    return names[:-1]