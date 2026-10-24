SCENARIO_NORMAL, SCENARIO_INFERNO, SCENARIO_SPRINTER = 0, 1, 2
SCENARIO_RAPTOR, SCENARIO_RANDOM, SCENARIO_ARMOR = 3, 4, 5

def generate_scene(scenarios: list, redo: int, rng) -> list:
    """Tirage 'sac' : pioche sans remise, le sac est rempli à nouveau quand il est vide."""
    pool = list(dict.fromkeys(scenarios))
    values, bag = [], []
    while len(values) < redo:
        if not bag:
            bag = list(pool)
        values.append(bag.pop(rng.randrange(len(bag))))
    return values