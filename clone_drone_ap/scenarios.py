"""Scénarios (X) et armes de départ (Y) d'une Scene "X:Y;X:Y;...".

Importé par __init__.py (génération) et par client.py (repli pour les anciennes seeds).
"""

# --- X : scénarios ---
SCENARIO_NORMAL, SCENARIO_INFERNO, SCENARIO_SPRINTER = 0, 1, 2
SCENARIO_RAPTOR, SCENARIO_RANDOM, SCENARIO_ARMOR = 3, 4, 5

# --- Y : armes de départ ---
WEAPON_SWORD, WEAPON_BOW, WEAPON_HAMMER, WEAPON_SPEAR = 0, 1, 2, 3
WEAPON_COUNT = 4
WEAPON_ITEMS = {
    WEAPON_SWORD: "SwordUnlock",
    WEAPON_BOW: "BowUnlock",
    WEAPON_HAMMER: "Progressive Hammer",
    WEAPON_SPEAR: "SpearUnlock",
}

# Valeurs de l'option "randomize_starting_weapon" (doivent suivre options.py)
WEAPON_MODE_NONE, WEAPON_MODE_SAME, WEAPON_MODE_RANDOM = 0, 1, 2


def _draw_bag(pool: list, count: int, rng) -> list:
    """Tirage 'sac' : pioche sans remise, le sac est rempli à nouveau quand il est vide."""
    pool = list(dict.fromkeys(pool))
    values, bag = [], []
    while len(values) < count:
        if not bag:
            bag = list(pool)
        values.append(bag.pop(rng.randrange(len(bag))))
    return values


def generate_scene(scenarios: list, redo: int, rng) -> list:
    """Retourne la liste des X (un scénario par step)."""
    return _draw_bag(scenarios, redo, rng)


def generate_weapons(mode: int, redo: int, rng) -> list:
    """Retourne la liste des Y (une arme de départ par step).

    - NONE   : toujours l'épée (0), rien à randomiser
    - SAME   : une arme tirée une seule fois, identique à chaque step
    - RANDOM : tirage 'sac' sur les 4 armes, donc jamais le même chiffre à chaque step
    """
    if mode == WEAPON_MODE_SAME:
        return [rng.randrange(WEAPON_COUNT)] * redo
    if mode == WEAPON_MODE_RANDOM:
        return _draw_bag(list(range(WEAPON_COUNT)), redo, rng)
    return [WEAPON_SWORD] * redo


def format_scene(scenarios: list, weapons: list) -> str:
    """Construit la chaîne "X:Y;X:Y;..." envoyée dans le slot_data."""
    return ";".join(f"{x}:{y}" for x, y in zip(scenarios, weapons))