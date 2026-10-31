"""Logique d'accès des achats d'upgrades (pure : aucune dépendance à Archipelago hormis les tables du monde).

Acheter l'upgrade U au step N exige l'item qui débloque U ET ceux de tous ses ancêtres (PREREQ),
sous leur forme utilisable au step N : item global, copie "(Step N)", forme groupée ou séparée.
Cette logique sert à deux endroits :
  - rules.py : règles d'accès Archipelago (le fill ne pose jamais un item là où il faudrait déjà l'avoir) ;
  - __init__.py : vérification des placements pré-remplis par steps.py, qui ne passent pas par le fill.
"""
from collections import Counter
from functools import lru_cache
from typing import Dict, List, Tuple

from .items import item_table, step_item_name, KEY_ITEM
from .locations import LOCATION_STEP, LOCATION_UPGRADE
from .upgrade_data import ITEM_GRANTS, ROMAN, blockers

_ROMAN_TO_INT = {v: k for k, v in ROMAN.items()}


def purchase_level(location_name: str) -> int:
    """"Step 2 - Get Hammer III" -> 3 ; sans chiffre romain -> 1."""
    return _ROMAN_TO_INT.get(location_name.rsplit(" ", 1)[-1], 1)


@lru_cache(maxsize=None)
def grant_names(grant: str, step: int) -> Tuple[str, ...]:
    """Noms d'items (globaux ou "(Step N)") qui débloquent `grant` au step `step`."""
    names = []
    for item, grants in ITEM_GRANTS.items():
        if grant in grants:
            names.append(item)
            names.append(step_item_name(item, step))
    return tuple(n for n in names if n in item_table)


def purchase_requirements(location_name: str, universe: Counter) -> List[Tuple[Tuple[str, ...], int]]:
    """[(noms d'items possibles, nombre requis)] pour acheter ce check.

    `universe` : exemplaires qui existent réellement dans la seed. Un groupe sans aucun exemplaire
    (ex. l'épée quand on commence avec elle) est ignoré : on ne peut pas l'exiger."""
    upgrade = LOCATION_UPGRADE[location_name]
    step = LOCATION_STEP[location_name]
    out = []
    for ancestor in sorted(blockers(upgrade)):
        names = tuple(n for n in grant_names(ancestor, step) if universe[n] > 0)
        if not names:
            continue
        need = purchase_level(location_name) if ancestor == upgrade == "Hammer" else 1   # Progressive Hammer : niveau N = N exemplaires
        requirement = (names, min(need, sum(universe[n] for n in names)))
        if requirement not in out:   # un item groupé débloque parfois plusieurs ancêtres : une seule exigence suffit
            out.append(requirement)
    return out


def item_universe(world) -> Counter:
    """Items réellement présents pour ce joueur : pool + items de départ + pré-remplissage."""
    universe = Counter(i.name for i in world.multiworld.itempool if i.player == world.player)
    universe.update(i.name for i in world.multiworld.precollected_items[world.player])
    universe.update(item for _loc, item in world.prefill)
    return universe


def stuck_prefills(world, prefill: List[Tuple[str, str]], free: Counter, universe: Counter) -> List[Tuple[str, str]]:
    """Placements (location, item) du pré-remplissage qui ne pourraient jamais être atteints.

    `free` : items qui ne sont PAS pré-remplis (le fill les posera en respectant les règles, on les
    suppose donc obtenables). On relâche les placements un à un, tant que leur location est accessible."""
    have = Counter(free)
    keyed = int(world.keys_mode) != 0

    def reachable(loc: str) -> bool:
        step = LOCATION_STEP.get(loc, 1)
        if keyed and step > 1 and have[KEY_ITEM] < step - 1:        # entrée du step (regions.py)
            return False
        if loc in LOCATION_UPGRADE:
            return all(sum(have[n] for n in names) >= need
                       for names, need in purchase_requirements(loc, universe))
        if keyed and loc in world.win_step_locations and step not in world.key_steps_placed:
            return have[KEY_ITEM] >= step                              # règle de rules.py
        return True

    pending = list(prefill)
    progress = True
    while progress and pending:
        progress = False
        remaining = []
        for loc, item in pending:
            if reachable(loc):
                have[item] += 1
                progress = True
            else:
                remaining.append((loc, item))
        pending = remaining
    return pending