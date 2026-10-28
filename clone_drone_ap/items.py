from typing import Dict, List, NamedTuple, Tuple
from BaseClasses import Item, ItemClassification

class CloneDroneItem(Item):
    game: str = "Clone Drone in the Danger Zone"

class ItemData(NamedTuple):
    code: int
    classification: ItemClassification

BASE_ITEM_ID = 71000

item_table: Dict[str, ItemData] = {
    "BlockArrows": ItemData(BASE_ITEM_ID + 1, ItemClassification.useful),
    "EnergyCapacity": ItemData(BASE_ITEM_ID + 2, ItemClassification.progression),
    "EnergyRecharge": ItemData(BASE_ITEM_ID + 3, ItemClassification.useful),
    "KickUnlock": ItemData(BASE_ITEM_ID + 4, ItemClassification.progression),
    "KickPower+GetUp": ItemData(BASE_ITEM_ID + 5, ItemClassification.progression),
    "BowUnlock": ItemData(BASE_ITEM_ID + 6, ItemClassification.progression),
    "ArrowWidth": ItemData(BASE_ITEM_ID + 7, ItemClassification.useful),
    "AimTime": ItemData(BASE_ITEM_ID + 8, ItemClassification.useful),
    "FireArrow": ItemData(BASE_ITEM_ID + 9, ItemClassification.useful),
    "Jetpack": ItemData(BASE_ITEM_ID + 10, ItemClassification.progression),
    "Clone": ItemData(BASE_ITEM_ID + 11, ItemClassification.progression),
    "Armor+Repair": ItemData(BASE_ITEM_ID + 12, ItemClassification.useful),
    "Progressive Hammer": ItemData(BASE_ITEM_ID + 13, ItemClassification.progression), 
    "FireSword": ItemData(BASE_ITEM_ID + 14, ItemClassification.useful),
    "FireHammer+FireSpear": ItemData(BASE_ITEM_ID + 15, ItemClassification.useful),
    "SpearUnlock": ItemData(BASE_ITEM_ID + 16, ItemClassification.progression),
    "SpearShield+ShieldBash": ItemData(BASE_ITEM_ID + 17, ItemClassification.useful),
    "FireBreath+FireResistance": ItemData(BASE_ITEM_ID + 18, ItemClassification.useful),
    "NextUpgradeFree": ItemData(BASE_ITEM_ID + 19, ItemClassification.filler),
    "Riding": ItemData(BASE_ITEM_ID + 20, ItemClassification.useful),
}

for i in range(1, 4):
    item_table[f"ShopDiscount_{i}"] = ItemData(BASE_ITEM_ID + 20 + i, ItemClassification.progression)

# Épée : n'entre dans le pool que si elle n'est pas l'arme de départ (voir create_items dans __init__.py)
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

# Tous les items qui débloquent une upgrade côté jeu (tout sauf l'économie : ShopDiscount / NextUpgradeFree)
UPGRADE_ITEMS = [n for n in item_table
                 if not n.startswith("ShopDiscount") and not n.startswith("NextUpgradeFree")]


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



item_table["StartWithArmor"] = ItemData(BASE_ITEM_ID + 64, ItemClassification.filler)
item_table["60sInfiniteArmor"] = ItemData(BASE_ITEM_ID + 67, ItemClassification.filler)
item_table["Clone (All Steps)"] = ItemData(BASE_ITEM_ID + 65, ItemClassification.progression)


def shop_discount_step_name(step: int) -> str:
    return f"Shop Discount (Step {step})"


for _step in range(1, MAX_STEPS + 1):
    item_table[shop_discount_step_name(_step)] = ItemData(BASE_ITEM_ID + 100 * _step + 66, ItemClassification.progression)