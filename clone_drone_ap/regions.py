from BaseClasses import Region
from .locations import location_table, CloneDroneLocation

def create_regions(world):
    menu_region = Region("Menu", world.player, world.multiworld)
    arena_region = Region("Arena", world.player, world.multiworld)

    for location_name, location_data in location_table.items():
        location = CloneDroneLocation(
            world.player, 
            location_name, 
            location_data.code, 
            arena_region
        )
        arena_region.locations.append(location)

    menu_region.connect(arena_region)
    world.multiworld.regions.append(menu_region)
    world.multiworld.regions.append(arena_region)