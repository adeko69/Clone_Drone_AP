from typing import List, Dict, Any
from BaseClasses import Tutorial, ItemClassification
from worlds.AutoWorld import World
from .items import item_table, CloneDroneItem
from .locations import location_table
from .regions import create_regions
from .rules import set_rules
from .options import CloneDroneOptions
from worlds.LauncherComponents import Component, components, Type, launch_subprocess

def launch_client(*args):
    from .client import launch
    launch_subprocess(launch, name="CloneDroneClient", args=args)

components.append(Component(
    "Clone Drone Client",
    func=launch_client,
    component_type=Type.CLIENT,
    description="Archipelago Client for Clone Drone in the Danger Zone."
))

class CloneDroneWorld(World):
    game = "Clone Drone in the Danger Zone"
    topology_present = False

    options_dataclass = CloneDroneOptions
    options: CloneDroneOptions

    item_name_to_id = {name: data.code for name, data in item_table.items()}
    location_name_to_id = {name: data.code for name, data in location_table.items()}

    def create_item(self, name: str) -> CloneDroneItem:
        data = item_table[name]
        return CloneDroneItem(name, data.classification, data.code, self.player)

    def create_regions(self) -> None:
        create_regions(self)

    def set_rules(self) -> None:
        set_rules(self)

    def create_items(self) -> None:
        itempool: List[CloneDroneItem] = []
        for name in item_table.keys():
            itempool.append(self.create_item(name))

        filler_items = [name for name, data in item_table.items() if data.classification == ItemClassification.useful]
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))
        
        while len(itempool) < total_locations:
            chosen_filler = self.random.choice(filler_items)
            itempool.append(self.create_item(chosen_filler))

        self.multiworld.itempool.extend(itempool)

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": int(self.options.goal.value),
           "death_link": self.options.death_link.value
        }