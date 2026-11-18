from typing import Dict, List, NamedTuple
from BaseClasses import Location
from .upgrade_data import upgrade_checks_for_step, win_step_name, win_step_location_id

class CloneDroneLocation(Location):
    game: str = "Clone Drone in the Danger Zone"

class LocationData(NamedTuple):
    code: int

BASE_LOCATION_ID = 72000
STRIDE = 40                      # > 33, constant, ne dépend pas du goal
MAX_STEPS = 5
GOAL_LEVELS = {1: 15, 2: 23, 3: 33, 4: 4}   # titanium, uranium, insanium, silver
GOAL_SCRAP_COLLECTOR = 4                       # = CloneDroneGoal.option_scrap_collector (pas de niveaux : le but = des scrap)

# --- Checks de scrap (mode Scrap Collector, ou "Scrap Anywhere") ---
# ID = 74000 + seuil / 1000  ->  74001..83999. upgrade_data.py occupe 73100..73590 : pas de collision.
BASE_SCRAP_LOCATION_ID = 74000
SCRAP_STEP = 1000
# Scrap Collector seulement : "Early Checks" (100 à 9990, multiples de 10, hors multiples de 1000 déjà couverts)
BASE_SCRAP_LOW_LOCATION_ID = 84000   # ID = 84000 + seuil / 10  ->  84010..84999
LOW_SCRAP_MIN, LOW_SCRAP_MAX = 100, 9990
REQUIRED_SCRAP = {0: 100_000, 1: 1_000_000, 2: 10_000_000}   # HowMuchScrap
# Avec 10000k, au-delà de 50 % chaque point de pourcentage ne rajoute qu'UN check : 100 % = 5000 + 50 = 5050 checks (au lieu de 9999)
HIGH_TIER_REQUIRED = 10_000_000
HIGH_TIER_HALF_PERCENT = 50

def loc_name(step: int, lvl: int) -> str:
    return f"Step {step} - Level {lvl}"

def loc_id(finished: int, lvl: int) -> int:
    return BASE_LOCATION_ID + finished * STRIDE + lvl

# Nom de location -> step auquel elle appartient (tous types confondus)
LOCATION_STEP: Dict[str, int] = {}
# Nom de location d'achat -> UpgradeType acheté
LOCATION_UPGRADE: Dict[str, str] = {}

location_table: Dict[str, LocationData] = {}

# 1. Niveaux de chaque step
for _r in range(1, MAX_STEPS + 1):
    for _l in range(1, 34):
        _name = loc_name(_r, _l)
        location_table[_name] = LocationData(loc_id(_r - 1, _l))
        LOCATION_STEP[_name] = _r

# 2. Achats d'upgrades (un check par niveau, à chaque step)
for _r in range(1, MAX_STEPS + 1):
    for _name, _code, _upgrade in upgrade_checks_for_step(_r):
        location_table[_name] = LocationData(_code)
        LOCATION_STEP[_name] = _r
        LOCATION_UPGRADE[_name] = _upgrade

# 2b. Checks de scrap : UNE location par seuil possible (1k, 2k, ... required max - 1k), nommée d'après son seuil.
# (location_name_to_id est statique : le nom ne peut pas dépendre de la seed, donc on crée tous les seuils possibles
# et seuls ceux tirés par la seed sont actifs.)
MAX_SCRAP_THRESHOLD = max(REQUIRED_SCRAP.values()) - SCRAP_STEP   # 9 999 000


def scrap_loc_name(threshold: int) -> str:
    return f"Scrap Check - {threshold:,}"          # "Scrap Check - 107,000"


def scrap_location_id(threshold: int) -> int:
    if threshold % SCRAP_STEP:   # seuil "bas" (multiple de 10)
        return BASE_SCRAP_LOW_LOCATION_ID + threshold // 10
    return BASE_SCRAP_LOCATION_ID + threshold // SCRAP_STEP


def scrap_names(thresholds) -> List[str]:
    return [scrap_loc_name(t) for t in thresholds]


for _t in range(SCRAP_STEP, MAX_SCRAP_THRESHOLD + 1, SCRAP_STEP):
    _name = scrap_loc_name(_t)
    location_table[_name] = LocationData(scrap_location_id(_t))
    LOCATION_STEP[_name] = 1   # région d'entrée : accessibles dès le début

# 3. "Win Step X" : tous les steps sauf le dernier (sa victoire est le Goal)
for _r in range(1, MAX_STEPS):
    _name = win_step_name(_r)
    location_table[_name] = LocationData(win_step_location_id(_r))
    LOCATION_STEP[_name] = _r


for _t in range(LOW_SCRAP_MIN, LOW_SCRAP_MAX + 1, 10):
    if _t % SCRAP_STEP:
        _name = scrap_loc_name(_t)
        location_table[_name] = LocationData(scrap_location_id(_t))
        LOCATION_STEP[_name] = 1


def low_scrap_thresholds(count: int, rng) -> List[int]:
    """`count` "Early Checks" au hasard : multiples de 10 entre 100 et 9990 inclus (110, 2050, 1400... jamais 1253),
    jamais un multiple de 1000 (ceux-là sont des checks normaux), sans doublon. Maximum : 981 (100 est le maximum de l'option)."""
    candidates = [t for t in range(LOW_SCRAP_MIN, LOW_SCRAP_MAX + 1, 10) if t % SCRAP_STEP]
    count = max(0, min(count, len(candidates)))
    return sorted(rng.sample(candidates, count))


def scrap_thresholds(required: int, percent: int, rng) -> List[int]:
    """Seuils (en scrap) des checks : `percent` % (arrondi au supérieur, minimum 1) des multiples de 1k possibles,
    tirés au hasard, sans doublon, entre 1k et `required` EXCLU (`required` est le but, pas un check).
    100 % = un check tous les 1k (99 pour 100k, 999 pour 1000k, 9999 pour 10000k). Triés par ordre croissant.
    `rng` = world.random (la seed fixe donc les seuils)."""
    slots = required // SCRAP_STEP - 1          # seuils possibles : 1k .. required-1k
    percent = max(0, min(100, percent))
    if slots <= 0 or percent <= 0:
        count = 0
    elif required >= HIGH_TIER_REQUIRED and percent > HIGH_TIER_HALF_PERCENT:
        half = -(-slots * HIGH_TIER_HALF_PERCENT // 100)           # 50 % arrondi au supérieur : 5000 pour 9999 seuils
        count = min(slots, half + (percent - HIGH_TIER_HALF_PERCENT))   # puis +1 check par point au-dessus de 50 %
    else:
        count = min(slots, max(1, -(-slots * percent // 100)))
    return [k * SCRAP_STEP for k in sorted(rng.sample(range(1, slots + 1), count))]


def get_level_names_by_step(goal_value: int, step_count: int) -> Dict[int, List[str]]:
    """{step: [noms des locations de niveau actives]} (le dernier niveau du dernier step = Goal, retiré)."""
    if goal_value == GOAL_SCRAP_COLLECTOR:
        return {r: [] for r in range(1, step_count + 1)}   # pas de niveaux (et surtout pas {} : KeyError)
    n = GOAL_LEVELS[goal_value]
    out = {r: [loc_name(r, l) for l in range(1, n + 1)] for r in range(1, step_count + 1)}
    out[step_count] = out[step_count][:-1]
    return out


def get_active_location_names(goal_value: int, step_count: int, skipped_ids=(), scrap_thresholds_=()) -> List[str]:
    """skipped_ids : IDs des checks d'achat à ne pas créer (l'arme de départ de chaque step est déjà possédée).
    scrap_thresholds_ : seuils des checks de scrap actifs (vide = système de scrap désactivé)."""
    scrap_collector = goal_value == GOAL_SCRAP_COLLECTOR

    # Niveaux : le dernier check = validation de la run (Goal Victory). Aucun en mode Scrap Collector.
    names = []
    if not scrap_collector:
        n = GOAL_LEVELS[goal_value]
        names = [loc_name(r, l) for r in range(1, step_count + 1) for l in range(1, n + 1)]
        names = names[:-1]

    # Achats d'upgrades de chaque step
    for r in range(1, step_count + 1):
        names.extend(name for name, _code, _upgrade in upgrade_checks_for_step(r)
                     if _code not in skipped_ids)

    # Win Step : pas pour le dernier step (ni en Scrap Collector : un seul step)
    if not scrap_collector:
        names.extend(win_step_name(r) for r in range(1, step_count))

    names.extend(scrap_names(scrap_thresholds_))
    return names