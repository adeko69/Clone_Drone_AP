from typing import Dict, NamedTuple
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
    "HammerUnlock": ItemData(BASE_ITEM_ID + 13, ItemClassification.progression),
    "FireSword": ItemData(BASE_ITEM_ID + 14, ItemClassification.useful),
    "FireHammer+FireSpear": ItemData(BASE_ITEM_ID + 15, ItemClassification.useful),
    "SpearUnlock": ItemData(BASE_ITEM_ID + 16, ItemClassification.progression),
    "SpearShield+ShieldBash": ItemData(BASE_ITEM_ID + 17, ItemClassification.useful),
    "FireBreath+FireResistance": ItemData(BASE_ITEM_ID + 18, ItemClassification.useful),
    "NextUpgradeFree": ItemData(BASE_ITEM_ID + 19, ItemClassification.useful),
    "Riding": ItemData(BASE_ITEM_ID + 20, ItemClassification.useful),
}

for i in range(1, 4):
    item_table[f"ShopDiscount_{i}"] = ItemData(BASE_ITEM_ID + 20 + i, ItemClassification.progression)

for i in range(1, 14):
    item_table[f"NextUpgradeFree_{i}"] = ItemData(BASE_ITEM_ID + 23 + i, ItemClassification.filler)