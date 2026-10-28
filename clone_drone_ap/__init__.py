from collections import Counter
from typing import Dict, Any
from BaseClasses import ItemClassification
from worlds.AutoWorld import World, WebWorld
from Options import OptionGroup
from .items import (
    item_table, CloneDroneItem, SPLIT_PARENTS, SPLIT_PARTS, UPGRADE_ITEMS, ITEM_COPIES, step_item_name,
    UPGRADE_GLOBAL_UNGROUPED, UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED,
    shop_discount_step_name,
)
from .locations import (
    location_table, GOAL_LEVELS, get_active_location_names, get_level_names_by_step, LOCATION_STEP, LOCATION_UPGRADE,
)
from .upgrade_data import split_step_suffix, item_blocked_at, win_step_name, upgrade_location_id
from .regions import create_regions
from .rules import set_rules
from .options import CloneDroneOptions, Normal, Inferno, Sprinter, Raptor, TotallyRandom, Armor
from .steps import plan_step_items, GLOBAL_PRIORITY
from worlds.LauncherComponents import Component, components, Type, launch_subprocess
from .scenarios import *


# Part des items "(Step N)" qui peuvent atterrir dans le monde des autres joueurs (le reste est verrouillé ici).
# Seulement en multiworld : en solo il n'y a pas d'autre monde, tout doit rester dans les locations du step.
INTERWORLD_RATIO = 0.70


# Places laissées libres en mode Per Step (en plus des items globaux) pour que les fillers aient de la place
FILLER_RESERVE = 14


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
        self.step_locked = (
            bool(o.step_is_step_locked.value)
            and int(o.upgrade_mode.value) in (UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED)
        )
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

        scenario_values = generate_scene(scenarios, o.steps.value, self.random)
        self.weapons = generate_weapons(int(o.randomize_starting_weapon.value), o.steps.value, self.random)
        self.scene = format_scene(scenario_values, self.weapons)   # "X:Y;X:Y;..."
        # Pas de check d'achat pour l'arme de départ de chaque step (déjà possédée) : épée, arc, marteau niv. I, lance
        self.skipped_checks = {
            upgrade_location_id(k, *START_WEAPON_CHECK[self.weapons[k - 1]])
            for k in range(1, o.steps.value + 1)
        }

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
        if self.step_locked:
            _base, item_step = split_step_suffix(item_name)
            if item_step is not None and item_step != LOCATION_STEP[location_name]:
                return True
        upgrade = LOCATION_UPGRADE.get(location_name)
        if upgrade is None:
            return False
        return self._blocked(item_name, LOCATION_STEP[location_name], upgrade)

    def set_rules(self) -> None:
        set_rules(self)

        player = self.player
        for location in self.multiworld.get_locations(player):
            upgrade = LOCATION_UPGRADE.get(location.name)
            step = LOCATION_STEP.get(location.name)
            if step is None:
                continue
            previous = location.item_rule

            def rule(item, previous=previous, step=step, upgrade=upgrade):
                if not previous(item):
                    return False
                if item.player != player:
                    return True
                # Une copie "(Step N)" de ce monde ne peut pas être dans un step > N (elle serait inutilisable)
                _base, item_step = split_step_suffix(item.name)
                if item_step is not None and step > item_step:
                    return False
                return upgrade is None or not self._blocked(item.name, step, upgrade)

            location.item_rule = rule

    def create_items(self) -> None:
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))
        mode = int(self.options.upgrade_mode.value)
        ungroup = mode in (UPGRADE_GLOBAL_UNGROUPED, UPGRADE_PER_STEP_UNGROUPED)
        per_step = mode in (UPGRADE_PER_STEP_GROUPED, UPGRADE_PER_STEP_UNGROUPED)
        weapon_mode = int(self.options.randomize_starting_weapon.value)
        step_count = int(self.options.steps.value)

        def start_item(step: int) -> str:
            base = WEAPON_ITEMS[self.weapons[step - 1]]
            return step_item_name(base, step) if per_step else base

        # Arme connue dès le début (none / same_per_step) : son unlock est un item initial (sphère 0).
        # Arme tirée à chaque step : seule celle du step 1 est connue ; celle du step suivant
        # est donnée par le check "Win Step" du step en cours (le joueur ne l'apprend qu'à ce moment-là).
        precollected = []
        win_locks = []
        if weapon_mode == WEAPON_MODE_SAME:
            precollected = [start_item(k) for k in (range(1, step_count + 1) if per_step else [1])]
        elif weapon_mode == WEAPON_MODE_RANDOM:
            precollected = [start_item(1)]
            win_locks = [(win_step_name(k), start_item(k + 1)) for k in range(1, step_count)]

        for name in precollected:
            self.multiworld.push_precollected(self.create_item(name))

        shop_locks = self._plan_shop_discounts(per_step, step_count, {loc for loc, _ in win_locks})
        self.prefill = list(win_locks) + shop_locks
        consumed = precollected + [item for _location, item in win_locks] + [item for _location, item in shop_locks]   # un exemplaire retiré du pool pour chacun

        if per_step:
            self._create_per_step_items(total_locations, ungroup, consumed, {loc for loc, _ in self.prefill})
        else:
            self._create_global_items(total_locations, ungroup, consumed)


    FILLER_NAMES = ("NextUpgradeFree", "StartWithArmor", "60sInfiniteArmor")

    def _filler_names(self, count: int) -> list:
        count = max(0, count)
        weights = [self.random.random() for _ in self.FILLER_NAMES]
        return self.random.choices(self.FILLER_NAMES, weights=weights, k=count)

    def _create_global_items(self, total_locations: int, ungroup: bool, consumed: list) -> None:
        weapon_mode = int(self.options.randomize_starting_weapon.value)
        excluded = set(SPLIT_PARENTS if ungroup else SPLIT_PARTS)

        # L'épée devient un item à trouver si on ne démarre pas (toujours) avec elle
        need_sword = (weapon_mode == WEAPON_MODE_RANDOM
                      or (weapon_mode == WEAPON_MODE_SAME and self.weapons[0] != WEAPON_SWORD))
        if not need_sword:
            excluded.add("SwordUnlock")
        excluded.add("Clone (All Steps)") 

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

        # Pas de logique d'accès : si trop peu de locations, on tronque au hasard au lieu de planter
        self.random.shuffle(progression)
        self.random.shuffle(useful)

        remaining = total_locations - len(self.prefill)
        names = (progression + useful)[:remaining]
        names += self._filler_names(remaining - len(names))   # les places vides = fillers

        self.multiworld.itempool.extend(self.create_item(n) for n in names)


    def _create_per_step_items(self, total_locations: int, ungroup: bool, consumed: list, locked: set) -> None:
        goal = int(self.options.goal.value)
        step_count = int(self.options.steps.value)
        weapon_mode = int(self.options.randomize_starting_weapon.value)

        step_locs = {k: [] for k in range(1, step_count + 1)}
        for name in get_active_location_names(goal, step_count, self.skipped_checks):
            if name not in locked:
                step_locs[LOCATION_STEP[name]].append(name)

        excluded = set(SPLIT_PARENTS if ungroup else SPLIT_PARTS)
        excluded.add("SwordUnlock")
        base_names = []
        for n in UPGRADE_ITEMS:
            if n not in excluded:
                base_names.extend([n] * ITEM_COPIES.get(n, 1))

        candidates = {}
        for k in range(1, step_count + 1):
            names = list(base_names)
            if weapon_mode != WEAPON_MODE_NONE and self.weapons[k - 1] != WEAPON_SWORD:
                names.append("SwordUnlock")
            candidates[k] = [step_item_name(n, k) for n in names]

        for name in consumed:
            _base, step = split_step_suffix(name)
            if step in candidates and name in candidates[step]:
                candidates[step].remove(name)

        priority = [n for n in GLOBAL_PRIORITY if n.startswith("ShopDiscount")]
        if step_count > 1:
            priority.insert(0, "Clone (All Steps)")
        for name in consumed:
            if name in priority:
                priority.remove(name)
        total_free = sum(len(v) for v in step_locs.values())
        reserve = min(len(priority) + FILLER_RESERVE, total_free // 5)
        interworld = INTERWORLD_RATIO if self.multiworld.players > 1 else 0.0
        reserve = max(reserve, int(total_free * interworld))

        placed = plan_step_items(candidates, step_locs, self.random, forbidden=self._forbidden, reserve=reserve)
        self.prefill += placed

        leftovers = []
        if interworld > 0:
            every = [n for names in candidates.values() for n in names]
            leftovers = list((Counter(every) - Counter(item for _loc, item in placed)).elements())
            self.random.shuffle(leftovers)
            leftovers.sort(key=lambda n: item_table[n].classification != ItemClassification.progression)

        # Le reste du pool : items globaux (ShopDiscount, NextUpgradeFree...)
        remaining = total_locations - len(self.prefill)
        names = (priority + leftovers)[:remaining]
        names += self._filler_names(remaining - len(names))   # les places vides = fillers

        self.multiworld.itempool.extend(self.create_item(n) for n in names)

    # ------------------------------------------------------------------
    # ShopDiscount : répartis proportionnellement (25 %, 50 %, 75 % des niveaux), au lieu de tomber au hasard
    # (ce qui les entassait dans le dernier step). Les "Shop Discount (Step N)" sont posés de la même façon,
    # mais uniquement dans les niveaux du step N.
    # ------------------------------------------------------------------
    SHOP_FRACTIONS = (0.25, 0.50, 0.75)

    @classmethod
    def _pick_proportional(cls, names: list, taken: set, fractions=None) -> list:
        """Choisit une location libre au plus près de chaque fraction de `names`."""
        n = len(names)
        picks = []
        for fraction in (fractions or cls.SHOP_FRACTIONS):
            target = min(n - 1, max(0, round(n * fraction) - 1))
            for dist in range(n):
                found = next((names[i] for i in (target + dist, target - dist)
                              if 0 <= i < n and names[i] not in taken), None)
                if found:
                    taken.add(found)
                    picks.append(found)
                    break
        return picks

    def _plan_shop_discounts(self, per_step: bool, step_count: int, taken: set) -> list:
        by_step = get_level_names_by_step(int(self.options.goal.value), step_count)
        flat = [n for k in range(1, step_count + 1) for n in by_step[k]]   # progression globale de la run

        result = list(zip(self._pick_proportional(flat, taken), ["ShopDiscount_1", "ShopDiscount_2", "ShopDiscount_3"]))
        if per_step and step_count > 1:
            for k in range(1, step_count + 1):
                picks = self._pick_proportional(by_step[k], taken, (0.50,))   # un seul, au milieu du step
                result.extend((loc, shop_discount_step_name(k)) for loc in picks)
        return result

    def pre_fill(self) -> None:
        for location_name, item_name in self.prefill:
            location = self.multiworld.get_location(location_name, self.player)
            location.place_locked_item(self.create_item(item_name))

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": int(self.options.goal.value),
            "death_link": self.options.death_link.value,
            "scene": self.scene,
            "steps": int(self.options.steps.value),
            "upgrade_mode": int(self.options.upgrade_mode.value),
            "randomize_starting_weapon": int(self.options.randomize_starting_weapon.value),
            "normal": int(self.options.normal.value),
            "inferno": int(self.options.inferno.value),
            "sprinter": int(self.options.jetpack.value),
            "raptor": int(self.options.raptor.value),
            "random": int(self.options.random.value),
            "armor": int(self.options.armor.value),
        }