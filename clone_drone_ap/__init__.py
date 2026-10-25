from typing import Dict, Any
from BaseClasses import ItemClassification
from worlds.AutoWorld import World, WebWorld
from Options import OptionGroup
from .items import (
    item_table, CloneDroneItem, SPLIT_PARENTS, SPLIT_PARTS, UPGRADE_ITEMS, ITEM_COPIES, step_item_name,
    UPGRADE_GLOBAL_UNGROUPED, UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED,
)
from .locations import (
    location_table, GOAL_LEVELS, get_active_location_names, LOCATION_STEP, LOCATION_UPGRADE,
)
from .upgrade_data import split_step_suffix, item_blocked_at, win_step_name
from .regions import create_regions
from .rules import set_rules
from .options import CloneDroneOptions, Normal, Inferno, Sprinter, Raptor, TotallyRandom, Armor
from .steps import plan_step_items, GLOBAL_PRIORITY
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


class CloneDroneWeb(WebWorld):
    # Chaque groupe devient un onglet (Options Creator du lanceur, page d'options du site).
    # Les options hors groupe restent dans "Game Options". Le YAML ne change pas.
    option_groups = [
        OptionGroup("Scenario", [Normal, Inferno, Sprinter, Raptor, TotallyRandom, Armor]),
    ]


class CloneDroneWorld(World):
    game = "Clone Drone in the Danger Zone"
    web = CloneDroneWeb()
    topology_present = False

    options_dataclass = CloneDroneOptions
    options: CloneDroneOptions

    item_name_to_id = {name: data.code for name, data in item_table.items()}
    location_name_to_id = {name: data.code for name, data in location_table.items()}

    def generate_early(self) -> None:
        self.prefill = []   # [(location, item)] posés dans pre_fill

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

        scenario_values = generate_scene(scenarios, o.redo.value, self.random)
        self.weapons = generate_weapons(int(o.randomize_starting_weapon.value), o.redo.value, self.random)
        self.scene = format_scene(scenario_values, self.weapons)   # "X:Y;X:Y;..."

    def create_item(self, name: str) -> CloneDroneItem:
        data = item_table[name]
        return CloneDroneItem(name, data.classification, data.code, self.player)

    def create_regions(self) -> None:
        create_regions(self)

    # ------------------------------------------------------------------
    # Interdits de placement : un item ne peut pas se trouver dans le check de sa propre upgrade,
    # ni d'une upgrade qui en dépend (il faudrait déjà l'avoir pour l'acheter).
    # Une copie "(Step N)" n'est concernée que dans les checks du step N.
    # ------------------------------------------------------------------
    @staticmethod
    def _blocked(item_name: str, location_step: int, upgrade: str) -> bool:
        base, item_step = split_step_suffix(item_name)
        if item_step is not None and item_step != location_step:
            return False
        return item_blocked_at(base, upgrade)

    def _forbidden(self, item_name: str, location_name: str) -> bool:
        upgrade = LOCATION_UPGRADE.get(location_name)
        if upgrade is None:
            return False
        return self._blocked(item_name, LOCATION_STEP[location_name], upgrade)

    def set_rules(self) -> None:
        set_rules(self)

        player = self.player
        for location in self.multiworld.get_locations(player):
            upgrade = LOCATION_UPGRADE.get(location.name)
            if upgrade is None:
                continue

            step = LOCATION_STEP[location.name]
            previous = location.item_rule

            def rule(item, previous=previous, step=step, upgrade=upgrade):
                if not previous(item):
                    return False
                if item.player != player:
                    return True
                return not self._blocked(item.name, step, upgrade)

            location.item_rule = rule

    def create_items(self) -> None:
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))
        mode = int(self.options.upgrade_mode.value)
        ungroup = mode in (UPGRADE_GLOBAL_UNGROUPED, UPGRADE_PER_STEP_UNGROUPED)
        per_step = mode in (UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED)
        weapon_mode = int(self.options.randomize_starting_weapon.value)
        redo = int(self.options.redo.value)

        def start_item(step: int) -> str:
            base = WEAPON_ITEMS[self.weapons[step - 1]]
            return step_item_name(base, step) if per_step else base

        # Arme connue dès le début (none / same_per_step) : son unlock est un item initial (sphère 0).
        # Arme tirée à chaque step : seule celle du step 1 est connue ; celle du step suivant
        # est donnée par le check "Win Step" du step en cours (le joueur ne l'apprend qu'à ce moment-là).
        precollected = []
        win_locks = []
        if weapon_mode == WEAPON_MODE_SAME:
            precollected = [start_item(k) for k in (range(1, redo + 1) if per_step else [1])]
        elif weapon_mode == WEAPON_MODE_RANDOM:
            precollected = [start_item(1)]
            win_locks = [(win_step_name(k), start_item(k + 1)) for k in range(1, redo)]

        for name in precollected:
            self.multiworld.push_precollected(self.create_item(name))

        self.prefill = list(win_locks)
        consumed = precollected + [item for _location, item in win_locks]   # un exemplaire retiré du pool pour chacun

        if per_step:
            self._create_per_step_items(total_locations, ungroup, consumed, {loc for loc, _ in win_locks})
        else:
            self._create_global_items(total_locations, ungroup, consumed)

    # ------------------------------------------------------------------
    # Mode Global : une copie de chaque upgrade, valable à tous les steps
    # ------------------------------------------------------------------
    def _create_global_items(self, total_locations: int, ungroup: bool, consumed: list) -> None:
        weapon_mode = int(self.options.randomize_starting_weapon.value)
        excluded = set(SPLIT_PARENTS if ungroup else SPLIT_PARTS)

        # L'épée devient un item à trouver si on ne démarre pas (toujours) avec elle
        need_sword = (weapon_mode == WEAPON_MODE_RANDOM
                      or (weapon_mode == WEAPON_MODE_SAME and self.weapons[0] != WEAPON_SWORD))
        if not need_sword:
            excluded.add("SwordUnlock")

        # Les copies par step n'existent qu'en mode Per Step
        counts = {
            n: ITEM_COPIES.get(n, 1)
            for n in item_table
            if n not in excluded and split_step_suffix(n)[1] is None
        }
        for name in consumed:
            if counts.get(name, 0) > 0:
                counts[name] -= 1

        def expand(classification):
            names = []
            for n, c in counts.items():
                if item_table[n].classification == classification:
                    names.extend([n] * c)
            return names

        progression = expand(ItemClassification.progression)
        useful = expand(ItemClassification.useful)
        filler = expand(ItemClassification.filler)

        # Pas de logique d'accès : si trop peu de locations, on tronque au hasard au lieu de planter
        self.random.shuffle(progression)
        self.random.shuffle(useful)

        remaining = total_locations - len(self.prefill)
        names = (progression + useful + filler)[:remaining]
        while len(names) < remaining:
            names.append(self.random.choice(useful))

        self.multiworld.itempool.extend(self.create_item(n) for n in names)

    # ------------------------------------------------------------------
    # Mode Per Step : une copie de chaque upgrade par step.
    # L'upgrade "(Step N)" est utilisable pendant le step N seulement, et ne peut se trouver
    # que dans les locations des steps 1 à N. On la pose nous-mêmes en pre_fill.
    # ------------------------------------------------------------------
    def _create_per_step_items(self, total_locations: int, ungroup: bool, consumed: list, locked: set) -> None:
        goal = int(self.options.goal.value)
        redo = int(self.options.redo.value)
        weapon_mode = int(self.options.randomize_starting_weapon.value)

        # Locations libres de chaque step (les "Win Step" verrouillés sont déjà pris)
        step_locs = {k: [] for k in range(1, redo + 1)}
        for name in get_active_location_names(goal, redo):
            if name not in locked:
                step_locs[LOCATION_STEP[name]].append(name)

        # Upgrades disponibles (l'épée est gérée à part, selon l'arme de départ de chaque step)
        excluded = set(SPLIT_PARENTS if ungroup else SPLIT_PARTS)
        excluded.add("SwordUnlock")
        base_names = []
        for n in UPGRADE_ITEMS:
            if n not in excluded:
                base_names.extend([n] * ITEM_COPIES.get(n, 1))

        candidates = {}
        for k in range(1, redo + 1):
            names = list(base_names)
            if weapon_mode != WEAPON_MODE_NONE and self.weapons[k - 1] != WEAPON_SWORD:
                names.append("SwordUnlock")   # item à trouver pour les steps qui démarrent sans l'épée
            candidates[k] = [step_item_name(n, k) for n in names]

        for name in consumed:                 # items initiaux / "Win Step" : un exemplaire retiré
            _base, step = split_step_suffix(name)
            if step in candidates and name in candidates[step]:
                candidates[step].remove(name)

        # Beaucoup de locations (achats d'upgrades) : on complète chaque step avec des doublons d'upgrades utiles
        for k, names in candidates.items():
            pad_pool = [n for n in names if item_table[n].classification == ItemClassification.useful] or names
            while len(names) < len(step_locs[k]) and pad_pool:
                names.append(self.random.choice(pad_pool))

        global_items = list(GLOBAL_PRIORITY) + [f"NextUpgradeFree_{i}" for i in range(1, 14)]
        total_free = sum(len(v) for v in step_locs.values())
        reserve = min(len(global_items), total_free // 5)

        self.prefill += plan_step_items(candidates, step_locs, self.random, forbidden=self._forbidden, reserve=reserve)

        # Le reste du pool : items globaux (ShopDiscount, NextUpgradeFree...)
        remaining = total_locations - len(self.prefill)
        names = global_items[:remaining]
        while len(names) < remaining:
            names.append("NextUpgradeFree")

        self.multiworld.itempool.extend(self.create_item(n) for n in names)

    def pre_fill(self) -> None:
        for location_name, item_name in self.prefill:
            location = self.multiworld.get_location(location_name, self.player)
            location.place_locked_item(self.create_item(item_name))

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": int(self.options.goal.value),
            "death_link": self.options.death_link.value,
            "scene": self.scene,
            "redo": int(self.options.redo.value),
            "upgrade_mode": int(self.options.upgrade_mode.value),
            "randomize_starting_weapon": int(self.options.randomize_starting_weapon.value),
            "normal": int(self.options.normal.value),
            "inferno": int(self.options.inferno.value),
            "sprinter": int(self.options.jetpack.value),
            "raptor": int(self.options.raptor.value),
            "random": int(self.options.random.value),
            "armor": int(self.options.armor.value),
        }