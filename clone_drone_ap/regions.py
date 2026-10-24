from BaseClasses import Region, Item, ItemClassification
from .locations import location_table, CloneDroneLocation, get_active_location_names
from .items import item_table, CloneDroneItem

def create_regions(world):
    menu_region = Region("Menu", world.player, world.multiworld)
    arena_region = Region("Arena", world.player, world.multiworld)

    goal_val = world.options.goal.value
    redo_val = world.options.redo.value

    # Locations dynamiques selon le goal et le redo
    for location_name in get_active_location_names(goal_val, redo_val):
        location = CloneDroneLocation(
            world.player,
            location_name,
            location_table[location_name].code,
            arena_region
        )
        arena_region.locations.append(location)

    # Event de victoire
    victory = CloneDroneLocation(world.player, "Goal Victory", None, arena_region)
    victory.place_locked_item(
        Item("Victory", ItemClassification.progression, None, world.player)
    )
    arena_region.locations.append(victory)

    menu_region.connect(arena_region)
    world.multiworld.regions.append(menu_region)
    world.multiworld.regions.append(arena_region)