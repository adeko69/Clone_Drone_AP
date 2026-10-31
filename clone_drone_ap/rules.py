from worlds.generic.Rules import add_rule
from .items import KEY_ITEM
from .upgrade_data import win_step_name
from .locations import LOCATION_UPGRADE
from .logic import purchase_requirements, item_universe


def set_rules(world):
    player = world.player
    multiworld = world.multiworld
    steps = int(world.options.steps.value)

    multiworld.completion_condition[player] = lambda state: state.has("Victory", player)

    # Acheter une upgrade exige l'item qui la débloque et ceux de ses ancêtres (logic.py).
    universe = item_universe(world)
    for location in multiworld.get_locations(player):
        if location.name not in LOCATION_UPGRADE:
            continue
        for names, need in purchase_requirements(location.name, universe):
            add_rule(location, lambda state, names=names, need=need:
                     sum(state.count(n, player) for n in names) >= need)

    if int(getattr(world, "keys_mode", 0)) == 0:
        return   # pas de clé : aucune règle, comme avant

    # La clé N est posée dans le step N : "Win Step N" est donc dans la même sphère que la clé N,
    # sans règle d'accès. C'est l'ENTRÉE du step N+1 (regions.py) qui exige la clé N.
    # Seule exception : une clé qui n'a pas pu être posée dans son step (retombée dans le pool, donc
    # n'importe où) garde la règle, pour que le fill ne suppose pas "Win Step N" atteignable trop tôt.
    for n in range(1, steps):
        if n in world.key_steps_placed:
            continue
        add_rule(multiworld.get_location(win_step_name(n), player),
                 lambda state, n=n: state.has(KEY_ITEM, player, n))

    # Mode 2 : le dernier step exige aussi sa clé pour la victoire (mode 1 : le dernier step est libre)
    if int(world.keys_mode) == 2:
        add_rule(multiworld.get_location("Goal Victory", player),
                 lambda state: state.has(KEY_ITEM, player, steps))