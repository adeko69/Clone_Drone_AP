from typing import Dict, List, NamedTuple, Tuple
from BaseClasses import Item, ItemClassification

class CloneDroneItem(Item):
    game: str = "Clone Drone in the Danger Zone"

class ItemData(NamedTuple):
    code: int
    classification: ItemClassification

BASE_ITEM_ID = 71000

# Tous les items qui débloquent une upgrade sont "progression" : les règles d'accès des achats (logic.py) les exigent,
# et Archipelago ne compte que les items progression dans ses règles (state.count / state.has).
item_table: Dict[str, ItemData] = {
    "BlockArrows": ItemData(BASE_ITEM_ID + 1, ItemClassification.progression),
    "EnergyCapacity": ItemData(BASE_ITEM_ID + 2, ItemClassification.progression),
    "EnergyRecharge": ItemData(BASE_ITEM_ID + 3, ItemClassification.progression),
    "KickUnlock": ItemData(BASE_ITEM_ID + 4, ItemClassification.progression),
    "KickPower+GetUp": ItemData(BASE_ITEM_ID + 5, ItemClassification.progression),
    "BowUnlock": ItemData(BASE_ITEM_ID + 6, ItemClassification.progression),
    "ArrowWidth": ItemData(BASE_ITEM_ID + 7, ItemClassification.progression),
    "AimTime": ItemData(BASE_ITEM_ID + 8, ItemClassification.progression),
    "FireArrow": ItemData(BASE_ITEM_ID + 9, ItemClassification.progression),
    "Jetpack": ItemData(BASE_ITEM_ID + 10, ItemClassification.progression),
    "Clone": ItemData(BASE_ITEM_ID + 11, ItemClassification.progression),
    "Armor+Repair": ItemData(BASE_ITEM_ID + 12, ItemClassification.progression),
    "Progressive Hammer": ItemData(BASE_ITEM_ID + 13, ItemClassification.progression), 
    "FireSword": ItemData(BASE_ITEM_ID + 14, ItemClassification.progression),
    "FireHammer+FireSpear": ItemData(BASE_ITEM_ID + 15, ItemClassification.progression),
    "SpearUnlock": ItemData(BASE_ITEM_ID + 16, ItemClassification.progression),
    "SpearShield+ShieldBash": ItemData(BASE_ITEM_ID + 17, ItemClassification.progression),
    "FireBreath+FireResistance": ItemData(BASE_ITEM_ID + 18, ItemClassification.progression),
    "Next Upgrade Free": ItemData(BASE_ITEM_ID + 19, ItemClassification.filler),
    "Riding": ItemData(BASE_ITEM_ID + 20, ItemClassification.progression),
}

MAX_SKILLPOINT_PRICE = 5

for i in range(1, MAX_SKILLPOINT_PRICE + 1):
    item_table[f"ShopDiscount_{i}"] = ItemData(BASE_ITEM_ID + 20 + i, ItemClassification.progression)

item_table["SwordUnlock"] = ItemData(BASE_ITEM_ID + 63, ItemClassification.progression)

# --- Items groupés -> items séparés (option "ungroup_upgrade") ---
# parent : [(nom de l'item séparé, offset de l'ID)]
# Les noms séparés doivent correspondre EXACTEMENT aux clés de ApItemDatabase côté C#.
SPLIT_ITEMS: Dict[str, List[Tuple[str, int]]] = {
    "KickPower+GetUp":           [("KickPower", 51), ("GetUp", 52)],
    "Armor+Repair":              [("Armor", 53), ("Repair", 54)],
    "FireHammer+FireSpear":      [("FireHammer", 55), ("FireSpear", 56)],
    "SpearShield+ShieldBash":    [("SpearShield", 57), ("ShieldBash", 58)],
    "FireBreath+FireResistance": [("FireBreath", 59), ("FireResistance", 60)],
    "Riding":                    [("Raptor Riding", 61), ("Unicycle Riding", 62)],
}

# Les items séparés héritent de la classification de leur parent
for _parent, _parts in SPLIT_ITEMS.items():
    for _name, _offset in _parts:
        item_table[_name] = ItemData(BASE_ITEM_ID + _offset, item_table[_parent].classification)

SPLIT_PARENTS = set(SPLIT_ITEMS)
SPLIT_PARTS = {name for parts in SPLIT_ITEMS.values() for name, _ in parts}

# --- Upgrades globales ou par step (option "upgrade_mode") ---
UPGRADE_GLOBAL_GROUPED, UPGRADE_GLOBAL_UNGROUPED = 0, 1
UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED = 2, 3

MAX_STEPS = 5   # doit rester égal à MAX_STEPS dans locations.py

# Tous les items qui débloquent une upgrade côté jeu (tout sauf l'économie : ShopDiscount / Next Upgrade Free)
UPGRADE_ITEMS = [n for n in item_table
                 if not n.startswith("ShopDiscount") and not n.startswith("Next Upgrade Free")]


# Nombre d'exemplaires dans le pool (par copie de step en mode Per Step). Défaut : 1.
# Progressive Hammer : le 1er exemplaire permet d'acheter le Hammer (niveau 1), le 2e le niveau 2, le 3e le niveau 3.
ITEM_COPIES = {"Progressive Hammer": 3}


def step_item_name(base: str, step: int) -> str:
    """"Armor+Repair" + 2 -> "Armor+Repair (Step 2)". Le C# relit ce suffixe exact."""
    return f"{base} (Step {step})"


# Une copie de chaque upgrade par step. ID = BASE_ITEM_ID + 100 * step + offset de l'item d'origine
# (offset < 100, donc aucun conflit avec les IDs globaux ni entre steps)
for _step in range(1, MAX_STEPS + 1):
    for _base in UPGRADE_ITEMS:
        _data = item_table[_base]
        item_table[step_item_name(_base, _step)] = ItemData(
            BASE_ITEM_ID + 100 * _step + (_data.code - BASE_ITEM_ID),
            _data.classification,
        )



item_table["3 Start With Armor"] = ItemData(BASE_ITEM_ID + 64, ItemClassification.filler)
item_table["60s of Infinite Armor"] = ItemData(BASE_ITEM_ID + 67, ItemClassification.filler)
item_table["Clone (All Steps)"] = ItemData(BASE_ITEM_ID + 65, ItemClassification.progression)

# --- Fillers "améliorés" (variantes x2) ---
# Remplacent le filler de base avec une probabilité de FILLER_UPGRADE_CHANCE (33 %).
item_table["6 Start With Armor"] = ItemData(BASE_ITEM_ID + 69, ItemClassification.filler)
item_table["120s of Infinite Armor"] = ItemData(BASE_ITEM_ID + 70, ItemClassification.filler)

item_table["Free Clone"] = ItemData(BASE_ITEM_ID + 71, ItemClassification.filler) 
item_table["Progressive Energy Boost"] = ItemData(BASE_ITEM_ID + 72, ItemClassification.filler)
item_table["Progressive Weapon Boost"] = ItemData(BASE_ITEM_ID + 73, ItemClassification.filler)

# Scrap : filler du mode Scrap Collector / Scrap Anywhere (inutile dans un goal Reach sans scrap).
# "Scrap" = 500 scrap, "Big Scrap" = 5000 scrap (20 % des "Scrap" deviennent "Big Scrap").
# Les valeurs sont dupliquées dans client.py (SCRAP_ITEM_VALUES) et Class1.cs (ScrapItemValue / BigScrapItemValue).
item_table["Scrap"] = ItemData(BASE_ITEM_ID + 74, ItemClassification.filler)
item_table["Big Scrap"] = ItemData(BASE_ITEM_ID + 75, ItemClassification.filler)

# Mode scrap (Scrap Collector ou Scrap Anywhere) : 2 items de progression et 2 fillers.
# Combo Strength / More Scrap : plusieurs exemplaires selon le scrap requis (voir scrap_progression_copies).
# Combo Strength : +0.1x de multiplicateur de combo par kill. More Scrap : +1x sur tous les kills.
# Combo Time : +0.5 s par combo (kill) et +1 s sur les 10 s de reprise, par exemplaire. Plain Scrap Gain : +1 scrap par kill, avant les multiplicateurs.
item_table["Combo Strength"] = ItemData(BASE_ITEM_ID + 76, ItemClassification.progression)
item_table["More Scrap"] = ItemData(BASE_ITEM_ID + 77, ItemClassification.progression)
item_table["Combo Time"] = ItemData(BASE_ITEM_ID + 78, ItemClassification.filler)
item_table["Plain Scrap Gain"] = ItemData(BASE_ITEM_ID + 79, ItemClassification.filler)

# "Small Scrap" : filler quasi inutile. Scrap actif : +10 scrap (valeur dupliquée dans client.py et Class1.cs). Scrap inactif : rien.
# Il remplace les Free Clone / Progressive Weapon Boost / Progressive Energy Boost au-delà de FILLER_CAPS, et entre dans
# la liste des fillers (poids SMALL_SCRAP_WEIGHT) dès que le joueur a plus de SMALL_SCRAP_LOCATION_THRESHOLD locations.
SMALL_SCRAP = "Small Scrap"
SMALL_SCRAP_VALUE = 10
SMALL_SCRAP_LOCATION_THRESHOLD = 1075
SMALL_SCRAP_WEIGHT = 5
item_table[SMALL_SCRAP] = ItemData(BASE_ITEM_ID + 80, ItemClassification.filler)

# Nombre maximum d'exemplaires générés (pour chacun) ; les tirages suivants deviennent des "Small Scrap".
FILLER_CAPS = {"Free Clone": 100, "Progressive Weapon Boost": 100, "Progressive Energy Boost": 100}

SCRAP_PROGRESSION_ITEMS = ("Combo Strength", "More Scrap")
SCRAP_ONLY_FILLERS = ("Scrap", "Combo Time", "Plain Scrap Gain")   # inutiles sans système de scrap

# Exemplaires de chaque item de SCRAP_PROGRESSION_ITEMS selon le scrap requis (HowMuchScrap) : 100k -> 1, 1000k -> 3, 10000k -> 6
SCRAP_PROGRESSION_COPIES = {100_000: 1, 1_000_000: 3, 10_000_000: 6}


def scrap_progression_copies(required_scrap: int) -> int:
    """Nombre d'exemplaires de "Combo Strength" et de "More Scrap" pour ce scrap requis (chacun)."""
    return SCRAP_PROGRESSION_COPIES.get(required_scrap, 1)

FILLER_UPGRADE_CHANCE = 0.33
SCRAP_UPGRADE_CHANCE = 0.20
FILLER_UPGRADE_CHANCES: Dict[str, float] = {"Scrap": SCRAP_UPGRADE_CHANCE}   # défaut : FILLER_UPGRADE_CHANCE
FILLER_UPGRADES: Dict[str, str] = {
    "60s of Infinite Armor": "120s of Infinite Armor",
    "3 Start With Armor":    "6 Start With Armor",
    "Scrap":                 "Big Scrap",
}


def roll_filler_name(base_name: str, rng) -> str:
    """Retourne la variante du filler (x2 : 33 % ; Scrap -> Big Scrap : 20 %), sinon le filler de base.
    `rng` = world.random (ou tout objet random.Random)."""
    upgraded = FILLER_UPGRADES.get(base_name)
    if upgraded is not None and rng.random() < FILLER_UPGRADE_CHANCES.get(base_name, FILLER_UPGRADE_CHANCE):
        return upgraded
    return base_name


def shop_discount_step_name(step: int) -> str:
    return f"Shop Discount (Step {step})"


for _step in range(1, MAX_STEPS + 1):
    item_table[shop_discount_step_name(_step)] = ItemData(BASE_ITEM_ID + 100 * _step + 66, ItemClassification.progression)


# --- Multi-Step Keys ---
# Une seule clé progressive : la clé n est posée dans le step n (voir _plan_keys). Pas de copie par step.
# Le nom doit correspondre EXACTEMENT à celui testé côté client.py et Class1.cs (ap_grant_item).
KEY_ITEM = "Progressive Key"
item_table[KEY_ITEM] = ItemData(BASE_ITEM_ID + 68, ItemClassification.progression)


def key_count(steps: int, mode: int) -> int:
    """mode 0 = aucune clé, 1 = une par step sauf le dernier, 2 = une par step."""
    return {0: 0, 1: max(0, steps - 1), 2: steps}[mode]