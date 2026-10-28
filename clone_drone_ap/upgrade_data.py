"""Données des checks d'achat d'upgrades, des "Win Step" et des dépendances.

Module pur Python (aucun import Archipelago) : utilisé par le monde (locations, génération)
et par le client (conversion "BUY:Type:niveau" -> ID de location).

Les noms de types ("FireSword", "FlameBreath"...) sont les noms de l'enum UpgradeType du jeu,
tels qu'ils sont envoyés par le mod C#.
"""
from typing import Dict, List, Optional, Tuple

UPGRADE_LOCATION_BASE = 73000      # ID = base + step * 100 + index
WIN_STEP_INDEX = 90                # index réservé aux "Win Step" (les achats vont de 1 à 37)
MAX_STEPS = 5

ROMAN = {1: "I", 2: "II", 3: "III"}

# (nom du check, UpgradeType, nombre de niveaux). Un check par niveau, à chaque step.
# L'épée de base n'y figure pas : on ne peut pas l'acheter.
UPGRADE_CHECKS: List[Tuple[str, str, int]] = [
    ("Get Reflect Arrows", "BlockArrows", 3),
    ("Get Energy Capacity", "EnergyCapacity", 3),
    ("Get Energy Recharge", "EnergyRecharge", 2),
    ("Get Kick", "KickUnlock", 1),
    ("Get Kick Power", "KickPower", 1),
    ("Get Up", "GetUp", 1),
    ("Get Bow", "BowUnlock", 1),
    ("Get Arrow Size", "ArrowWidth", 2),
    ("Get Arrow Time", "AimTime", 2),
    ("Get Fire Arrow", "FireArrow", 2),
    ("Get Jetpack", "Jetpack", 2),
    ("Get Clone", "Clone", 1),
    ("Get Armor", "Armor", 1),
    ("Get Repair", "Repair", 1),
    ("Get Hammer", "Hammer", 3), 
    ("Get Fire Sword", "FireSword", 2),
    ("Get Fire Hammer", "FireHammer", 1),
    ("Get Fire Spear", "FireSpear", 1),
    ("Get Spear", "SpearUnlock", 1),
    ("Get Spear Shield", "ShieldSize", 1),
    ("Get Shield Bash", "ShieldBash", 1),
    ("Get Fire Breath", "FlameBreath", 1),
    ("Get Fire Resistance", "FireResistance", 1),
    ("Get Raptor Riding", "SummonFlameRaptor", 1),
    ("Get Unicycle Riding", "SummonSawbladeUnicycle", 1),
    # Ajouté EN DERNIER pour ne pas décaler les IDs existants. Généré seulement si l'épée n'est pas l'arme de départ du step.
    ("Get Sword", "SwordUnlock", 1),
]


def _expand() -> List[Tuple[int, str, str, int]]:
    """[(index, nom de base, UpgradeType, niveau)] : un par check d'un step."""
    out = []
    index = 0
    for name, upgrade, levels in UPGRADE_CHECKS:
        for level in range(1, levels + 1):
            index += 1
            out.append((index, name, upgrade, level))
    return out


_EXPANDED = _expand()
UPGRADE_CHECKS_PER_STEP = len(_EXPANDED)
assert UPGRADE_CHECKS_PER_STEP < WIN_STEP_INDEX


def upgrade_check_name(step: int, base: str, upgrade: str, level: int) -> str:
    levels = next(l for n, u, l in UPGRADE_CHECKS if u == upgrade)
    suffix = f" {ROMAN[level]}" if levels > 1 else ""
    return f"Step {step} - {base}{suffix}"


def upgrade_checks_for_step(step: int) -> List[Tuple[str, int, str]]:
    """[(nom de location, ID, UpgradeType)] pour un step."""
    return [
        (upgrade_check_name(step, base, upgrade, level),
         UPGRADE_LOCATION_BASE + step * 100 + index,
         upgrade)
        for index, base, upgrade, level in _EXPANDED
    ]


def win_step_name(step: int) -> str:
    return f"Win Step {step}"


def win_step_location_id(step: int) -> int:
    return UPGRADE_LOCATION_BASE + step * 100 + WIN_STEP_INDEX


def upgrade_location_id(step: int, upgrade: str, level: int) -> Optional[int]:
    """ID de la location "achat" (step, UpgradeType, niveau), ou None si ce niveau n'a pas de check."""
    if not 1 <= step <= MAX_STEPS:
        return None
    for index, _base, up, lvl in _EXPANDED:
        if up == upgrade and lvl == level:
            return UPGRADE_LOCATION_BASE + step * 100 + index
    return None


# ----------------------------------------------------------------------------
# Ce que débloque chaque item (doit suivre ApItemDatabase côté C#)
# ----------------------------------------------------------------------------
ITEM_GRANTS: Dict[str, Tuple[str, ...]] = {
    "BlockArrows": ("BlockArrows",),
    "EnergyCapacity": ("EnergyCapacity",),
    "EnergyRecharge": ("EnergyRecharge",),
    "KickUnlock": ("KickUnlock",),
    "KickPower+GetUp": ("KickPower", "GetUp"),
    "KickPower": ("KickPower",),
    "GetUp": ("GetUp",),
    "BowUnlock": ("BowUnlock",),
    "ArrowWidth": ("ArrowWidth",),
    "AimTime": ("AimTime",),
    "FireArrow": ("FireArrow",),
    "Jetpack": ("Jetpack",),
    "Clone": ("Clone",),
    "Clone (All Steps)": ("Clone",),
    "Armor+Repair": ("Armor", "Repair"),
    "Armor": ("Armor",),
    "Repair": ("Repair",),
    "Progressive Hammer": ("Hammer",),
    "FireSword": ("FireSword",),
    "FireHammer+FireSpear": ("FireHammer", "FireSpear"),
    "FireHammer": ("FireHammer",),
    "FireSpear": ("FireSpear",),
    "SpearUnlock": ("SpearUnlock",),
    "SpearShield+ShieldBash": ("ShieldSize", "ShieldBash"),
    "SpearShield": ("ShieldSize",),
    "ShieldBash": ("ShieldBash",),
    "FireBreath+FireResistance": ("FlameBreath", "FireResistance"),
    "FireBreath": ("FlameBreath",),
    "FireResistance": ("FireResistance",),
    "Riding": ("SummonFlameRaptor", "SummonSawbladeUnicycle"),
    "Raptor Riding": ("SummonFlameRaptor",),
    "Unicycle Riding": ("SummonSawbladeUnicycle",),
    "SwordUnlock": ("SwordUnlock",),
}

# ----------------------------------------------------------------------------
# Dépendances de l'arbre : enfant -> parent. À COMPLÉTER / VÉRIFIER avec le vrai arbre du jeu.
# Se tromper en ajoutant une dépendance ne fait que restreindre un peu le placement ;
# en oublier une peut créer un blocage (ex : l'item parent posé dans le check de son enfant).
# ----------------------------------------------------------------------------
PREREQ: Dict[str, str] = {
    "ArrowWidth": "BowUnlock",
    "AimTime": "BowUnlock",
    "FireArrow": "BowUnlock",
    "KickPower": "KickUnlock",
    "GetUp": "KickUnlock",
    "FireHammer": "Hammer",
    "FireSpear": "SpearUnlock",
    "ShieldSize": "SpearUnlock",
    "ShieldBash": "SpearUnlock",
    "Repair": "Armor",
    "FireSword": "SwordUnlock",   # sans effet si l'épée est l'arme de départ (aucun item SwordUnlock dans le pool)
}


def blockers(upgrade: str) -> set:
    """L'upgrade elle-même et tous ses ancêtres : les items qui les débloquent ne peuvent pas
    se trouver dans le check de cette upgrade (il faudrait déjà les avoir pour l'acheter)."""
    result = {upgrade}
    while upgrade in PREREQ:
        upgrade = PREREQ[upgrade]
        result.add(upgrade)
    return result


def item_blocked_at(item_base: str, upgrade: str) -> bool:
    """Vrai si l'item (nom sans suffixe de step) ne peut pas être posé dans le check de `upgrade`."""
    return any(g in blockers(upgrade) for g in ITEM_GRANTS.get(item_base, ()))


def split_step_suffix(item_name: str) -> Tuple[str, Optional[int]]:
    """"Armor (Step 2)" -> ("Armor", 2). Sans suffixe : (nom, None)."""
    if item_name.endswith(")") and " (Step " in item_name:
        base, _, rest = item_name.rpartition(" (Step ")
        digits = rest[:-1]
        if digits.isdigit():
            return base, int(digits)
    return item_name, None