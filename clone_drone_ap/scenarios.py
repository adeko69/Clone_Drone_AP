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

# Check d'achat (UpgradeType, niveau) déjà possédé quand on démarre un step avec cette arme : il ne doit pas exister.
# Marteau : le départ donne le niveau 1 (un exemplaire de "Progressive Hammer"), les niveaux 2 et 3 restent à acheter.
START_WEAPON_CHECK = {
    WEAPON_SWORD: ("SwordUnlock", 1),
    WEAPON_BOW: ("BowUnlock", 1),
    WEAPON_HAMMER: ("Hammer", 1),
    WEAPON_SPEAR: ("SpearUnlock", 1),
}

# Réduction de la probabilité de tirer l'Arc comme arme de départ
BOW_REDUCTION_SAME = 0.25     # Same Per Step (une arme pour tous les steps) : -25 %
BOW_REDUCTION_RANDOM = 0.75   # Random By Step : -75 %

# Valeurs de l'option "randomize_starting_weapon" (doivent suivre options.py)
WEAPON_MODE_NONE, WEAPON_MODE_SAME, WEAPON_MODE_RANDOM = 0, 1, 2


def _draw_bag(pool: list, count: int, rng, chances: dict = None) -> list:
    """Tirage 'sac' : pioche sans remise, le sac est rempli à nouveau quand il est vide.
    chances : {valeur: probabilité d'entrer dans le sac à chaque remplissage} (défaut 1).
    Sans `chances`, aucun nombre aléatoire supplémentaire n'est consommé (seeds existantes inchangées)."""
    pool = list(dict.fromkeys(pool))
    chances = chances or {}
    values, bag = [], []
    while len(values) < count:
        if not bag:
            bag = [p for p in pool if p not in chances or rng.random() < chances[p]] or list(pool)
        values.append(bag.pop(rng.randrange(len(bag))))
    return values


def generate_scene(scenarios: list, step_count: int, rng) -> list:
    """Retourne la liste des X (un scénario par step)."""
    return _draw_bag(scenarios, step_count, rng)


def generate_weapons(mode: int, step_count: int, rng) -> list:
    """Retourne la liste des Y (une arme de départ par step).

    - NONE   : toujours l'épée (0), rien à randomiser
    - SAME   : une arme tirée une seule fois, identique à chaque step
    - RANDOM : tirage 'sac' sur les 4 armes, donc jamais le même chiffre à chaque step
    """
    if mode == WEAPON_MODE_SAME:
        # poids de l'arc w tel que w / (3 + w) = (1/4) * (1 - réduction)
        p = (1 / WEAPON_COUNT) * (1 - BOW_REDUCTION_SAME)
        w = (WEAPON_COUNT - 1) * p / (1 - p)
        weights = [w if x == WEAPON_BOW else 1.0 for x in range(WEAPON_COUNT)]
        return [rng.choices(range(WEAPON_COUNT), weights=weights)[0]] * step_count
    if mode == WEAPON_MODE_RANDOM:
        return _draw_bag(list(range(WEAPON_COUNT)), step_count, rng,
                         {WEAPON_BOW: 1 - BOW_REDUCTION_RANDOM})
    return [WEAPON_SWORD] * step_count


def format_scene(scenarios: list, weapons: list) -> str:
    """Construit la chaîne "X:Y;X:Y;..." envoyée dans le slot_data."""
    return ";".join(f"{x}:{y}" for x, y in zip(scenarios, weapons))