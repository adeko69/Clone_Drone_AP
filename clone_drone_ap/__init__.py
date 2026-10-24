from typing import Dict, Any
from BaseClasses import ItemClassification
from worlds.AutoWorld import World
from .items import item_table, CloneDroneItem
from .locations import location_table
from .regions import create_regions
from .rules import set_rules
from .options import CloneDroneOptions
from worlds.LauncherComponents import Component, components, Type, launch_subprocess
from .scenarios import *


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

    def generate_early(self) -> None:
        o = self.options
        flags = (o.normal, o.inferno, o.jetpack, o.raptor, o.random, o.armor)
        if not any(f.value for f in flags):
            o.normal.value = 1

        scenarios = []
        if o.normal.value:   scenarios.append(SCENARIO_NORMAL)
        if o.inferno.value:  scenarios.append(SCENARIO_INFERNO)
        if o.jetpack.value:  scenarios.append(SCENARIO_SPRINTER)
        if o.raptor.value:   scenarios.append(SCENARIO_RAPTOR)
        if o.random.value:   scenarios.append(SCENARIO_RANDOM)
        if o.armor.value:    scenarios.append(SCENARIO_ARMOR)

        self.scene = generate_scene(scenarios, o.redo.value, self.random)

    def create_item(self, name: str) -> CloneDroneItem:
        data = item_table[name]
        return CloneDroneItem(name, data.classification, data.code, self.player)

    def create_regions(self) -> None:
        create_regions(self)

    def set_rules(self) -> None:
        set_rules(self)

    def create_items(self) -> None:
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))

        progression = [n for n, d in item_table.items() if d.classification == ItemClassification.progression]
        useful      = [n for n, d in item_table.items() if d.classification == ItemClassification.useful]
        filler      = [n for n, d in item_table.items() if d.classification == ItemClassification.filler]

        if len(progression) > total_locations:
            raise Exception("Pas assez de locations pour les items progression")

        self.random.shuffle(useful)
        names = (progression + useful + filler)[:total_locations]

        while len(names) < total_locations:
            names.append(self.random.choice(useful))

        self.multiworld.itempool.extend(self.create_item(n) for n in names)

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": int(self.options.goal.value),
            "death_link": self.options.death_link.value,
            "scene": ";".join(map(str, self.scene)),
            "redo": int(self.options.redo.value),
            "normal": int(self.options.normal.value),
            "inferno": int(self.options.inferno.value),
            "sprinter": int(self.options.jetpack.value),
            "raptor": int(self.options.raptor.value),
            "random": int(self.options.random.value),
            "armor": int(self.options.armor.value),
        }