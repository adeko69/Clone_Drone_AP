from BaseClasses import Region, Item, ItemClassification
from .items import KEY_ITEM
from .locations import location_table, CloneDroneLocation, get_active_location_names, LOCATION_STEP


def create_regions(world):
    player = world.player
    menu_region = Region("Menu", player, world.multiworld)

    goal_val = world.options.goal.value
    step_val = world.options.steps.value

    # Avec les Multi-Step Keys, chaque step est sa propre région : on n'entre dans le step N+1
    # qu'avec les N clés (= le step N est validable). Sans clé, on garde l'unique région "Arena" (ancien comportement).
    gated = int(getattr(world, "keys_mode", 0)) != 0
    if gated:
        regions = [Region(f"Step {k}", player, world.multiworld) for k in range(1, step_val + 1)]
    else:
        regions = [Region("Arena", player, world.multiworld)]

    # Locations dynamiques selon le goal et le nombre de steps
    for location_name in get_active_location_names(goal_val, step_val, getattr(world, "skipped_checks", ())):
        region = regions[LOCATION_STEP[location_name] - 1] if gated else regions[0]
        region.locations.append(
            CloneDroneLocation(player, location_name, location_table[location_name].code, region)
        )

    # Event de victoire : dans le dernier step
    last_region = regions[-1]
    victory = CloneDroneLocation(player, "Goal Victory", None, last_region)
    victory.place_locked_item(Item("Victory", ItemClassification.progression, None, player))
    last_region.locations.append(victory)

    menu_region.connect(regions[0])
    for k in range(1, len(regions)):
        regions[k - 1].connect(
            regions[k],
            # Entrer dans le step k+1 = avoir validé le step k = avoir k Progressive Keys (comme "Win Step k")
            rule=lambda state, n=k: state.has(KEY_ITEM, player, n),
        )

    world.multiworld.regions.append(menu_region)
    world.multiworld.regions.extend(regions)