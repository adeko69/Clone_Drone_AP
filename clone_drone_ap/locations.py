from typing import Dict, NamedTuple
from BaseClasses import Location

class CloneDroneLocation(Location):
    game: str = "Clone Drone in the Danger Zone"

class LocationData(NamedTuple):
    code: int

BASE_LOCATION_ID = 72000
location_table: Dict[str, LocationData] = {}

for lvl in range(1,33):
    location_table[f"Level {lvl}"] = LocationData(BASE_LOCATION_ID + lvl)

for chapter in range(1, 4):
    location_table[f"Free check every 10 levels: {chapter}0"] = LocationData(BASE_LOCATION_ID + 100 + chapter)

location_table["Goal Victory"] = LocationData(BASE_LOCATION_ID + 106)