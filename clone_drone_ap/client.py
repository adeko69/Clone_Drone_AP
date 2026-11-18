import asyncio
import random
import re
import Utils
from CommonClient import CommonContext, server_loop, gui_enabled, ClientCommandProcessor, logger, get_base_parser
from NetUtils import ClientStatus
from .upgrade_data import upgrade_location_id, win_step_location_id, upgrade_checks_for_step, upgrade_location_id_by_level_id
from .scenarios import (
    generate_scene,
    SCENARIO_NORMAL, SCENARIO_INFERNO, SCENARIO_SPRINTER,
    SCENARIO_RAPTOR, SCENARIO_RANDOM, SCENARIO_ARMOR,
    SCENARIO_LEVELUP, SCENARIO_EASYMODE, SCENARIO_UNDEAD,
    SCENARIO_DOGGOS, SCENARIO_SUMMONERS
)

# --- Constantes (doivent rester synchronisées avec locations.py) ---
BASE_LOCATION_ID = 72000
STRIDE = 40
GOAL_LEVELS = {1: 15, 2: 23, 3: 33, 4: 4}
GOAL_NAMES = {1: "titanium", 2: "uranium", 3: "insanium", 4: "scrap_collector"}   # 4 : relu par le C# (avant : "silver")
BASE_SCRAP_LOCATION_ID = 74000   # doit rester synchronisé avec locations.py
SCRAP_STEP = 1000                # ID du check de scrap = BASE_SCRAP_LOCATION_ID + seuil / SCRAP_STEP
# Système de scrap envoyé au jeu dans les DIZAINES du nombre après ":" du goal (le reste = keys_mode, 0..2) :
# 0 = inactif, 10 = 100k, 20 = 1000k, 30 = 10000k. Ex. "scrap_collector:10", "uranium:21".
SCRAP_TIER = {100_000: 1, 1_000_000: 2, 10_000_000: 3}
KEY_ITEM = "Progressive Key"   # doit rester synchronisé avec items.py et le C# (ap_grant_item)
COOP_STEP_PERCENT = 20          # option scrap_coop : 1 = 20 %, 2 = 40 % ... 5 = 100 %
ITEM_BATCH_SIZE = 100           # items envoyés au jeu par connexion TCP (beaucoup d'items avec beaucoup de checks)

# Les checks d'achat sont relatifs au step : une fois le step fini, ceux qui n'ont pas été faits ne peuvent plus l'être.
# True = finir un step valide aussi tous ses checks d'achat restants, pour que les items qu'ils contiennent
# ne restent pas bloqués jusqu'à la fin (les upgrades "(Step N)" peuvent se trouver dans les steps 1 à N).
RELEASE_STEP_CHECKS_ON_WIN = True

KEY_NEXTFREE = "NextUpgradeFreeUsed"
KEY_INFINITE_ARMOR = "InfiniteArmorUsed"
KEY_START_CLONE = "StartCloneUsed"
KEY_SCRAP = "ScrapEarned"         # scrap GAGNÉS en jeu (DataStorage) : survit au redémarrage du jeu ET du client
# Items "Scrap" : ils ne sont PAS dans ce total (le jeu les recompte à chaque session, car tous les items lui sont renvoyés).
# Valeurs dupliquées dans items.py et Class1.cs (ScrapItemValue / BigScrapItemValue).
SCRAP_ITEM_VALUES = {"Scrap": 500, "Big Scrap": 5000, "Small Scrap": 10}


def purchase_checks_of_step(step: int) -> list:
    if not RELEASE_STEP_CHECKS_ON_WIN:
        return []
    return [code for _name, code, _upgrade in upgrade_checks_for_step(step)]


class Scene:
    """Une entrée par step : X = scénario (-1 = step terminé), Y = arme de départ (0 à 3).
    Format texte : "X:Y;X:Y;..." (l'ancien format "X;X;..." est accepté, Y vaut alors 0).

    Mode Scrap Collector (pool != None) : pas de step. Le texte est juste la liste des scénarios activés
    ("0:1:2:3") ; le jeu en tire un à chaque niveau. L'objet garde un seul step qui ne se termine jamais."""

    def __init__(self, values, weapons=None, pool=None):
        self.values = values
        if weapons is None or len(weapons) != len(values):
            weapons = [0] * len(values)
        self.weapons = weapons
        self.pool = pool

    @classmethod
    def parse(cls, s: str):
        values, weapons = [], []
        for part in s.split(";"):
            part = part.strip()
            if not part:
                continue
            x, _, y = part.partition(":")
            values.append(int(x))
            weapons.append(int(y) if y.strip() else 0)
        return cls(values, weapons)

    @classmethod
    def parse_pool(cls, s: str):
        """Scrap Collector : "0:1:2:3" (ou "0;1;2;3") -> liste des scénarios, sans doublon."""
        pool = []
        for part in s.replace(";", ":").split(":"):
            if part.strip().lstrip("-").isdigit() and int(part) not in pool:
                pool.append(int(part))
        if not pool:
            pool = [0]
        return cls([pool[0]], None, pool)

    def __str__(self):
        if self.pool is not None:
            return ":".join(str(p) for p in self.pool)
        return ";".join(f"{x}:{y}" for x, y in zip(self.values, self.weapons))

    @property
    def finished(self) -> int:
        return sum(1 for v in self.values if v == -1)

    @property
    def complete(self) -> bool:
        return self.finished >= len(self.values)

    def mark_done(self):
        for i, v in enumerate(self.values):
            if v != -1:
                self.values[i] = -1
                return


def resolve_check(scene: Scene, goal_n: int, level: int, keys_mode: int = 0, keys: int = 0):
    """Retourne (liste d'IDs de locations, victoire, scene_modifiée).

    Finir un step qui n'est pas le dernier valide le niveau final du step, "Win Step X" et (si activé)
    tous les checks d'achat restants du step. Finir le dernier step = victoire.

    keys_mode : 0 = pas de clé, 1 = clés sauf pour le dernier step, 2 = clés pour tous les steps.
    keys      : nombre de "Progressive Key" reçues. Le step N exige `keys >= N` quand une clé est requise.
    Sans la clé, le check du niveau final part quand même, mais le step n'est pas validé : le jeu continue."""
    if scene is None or scene.complete or level < 1:
        return [], False, False
    finished = scene.finished
    is_last_step = finished == len(scene.values) - 1
    if level > goal_n:
        if keys_mode == 0:                   # dépassement : on ne casse rien
            return [], False, False
        level = goal_n                       # niveau final franchi sans clé, clé reçue depuis : on valide maintenant
    ids = [BASE_LOCATION_ID + finished * STRIDE + level]
    if level != goal_n:
        return ids, False, False
    required = keys_mode == 2 or (keys_mode == 1 and not is_last_step)
    if required and keys < finished + 1:
        return ids, False, False             # pas de clé : le niveau compte, le step continue
    scene.mark_done()
    if is_last_step:
        return purchase_checks_of_step(finished + 1), True, True   # check ultime = victoire
    ids.append(win_step_location_id(finished + 1))
    ids.extend(purchase_checks_of_step(finished + 1))
    return ids, False, True


class CloneDroneCommandProcessor(ClientCommandProcessor):
    def _cmd_clonedrone(self):
        """TEST, IGNORE. Won't work anyway"""
        self.output("Client Clone Drone opérationnel.")
        if self.ctx.scene is not None:
            self.output(f"Scene: {self.ctx.scene}")


class CloneDroneContext(CommonContext):
    command_processor = CloneDroneCommandProcessor
    game = "Clone Drone in the Danger Zone"
    items_handling = 0b111  # Reçoit tous les items

    def __init__(self, server_address, password):
        self.randomize_voices = 0
        self.weapon_mode = 0       # option randomize_starting_weapon (3 = Every Death, tirage fait par le jeu)
        self.include_bow = 1       # option include_bow_in_weapon_randomization
        super().__init__(server_address, password)
        self.items_sent_to_game = 0
        self.items_lock = asyncio.Lock()   # un seul envoi d'items à la fois (sinon doublons)
        self.scene = None
        self.session_info = None   # (slot, room, goal) une fois connecté à Archipelago
        self.skillpoint_price = 3  # multiplicateur de prix de départ (option skillpoint_price)
        self.confusion = "0,0,0,0,0,0,0,0,0,0"
        self.overpriced_shop = 0   # nombre d'upgrades à +3 skillpoints, retirées au hasard à chaque mort (0 = désactivé)
        self.goal_n = GOAL_LEVELS[2]
        self.keys_mode = 0         # option multi_step_keys (0 = non, 1 = sans le dernier, 2 = avec le dernier)
        self.scrap_collector = False   # goal Scrap Collector : la victoire = atteindre required_scrap (pas de niveaux)
        self.scrap_enabled = False  # système de scrap actif (Scrap Collector, ou Scrap Anywhere)
        self.scrap_coop = 0         # co-op : 0 = off, 1..5 = 20..100 % des scrap gagnés DUPLIQUÉS vers les autres joueurs co-op
        self.coop_contribs = {}     # {slot (str): scrap partagés par ce joueur}, lu dans le DataStorage (commun à la room)
        self.required_scrap = 0
        self.scrap_thresholds = []   # seuils actifs de cette seed, un check "Scrap Check - <seuil>" chacun
        self.scrap_earned = 0       # plus haut total de scrap GAGNÉS reçu du jeu (ou lu dans le DataStorage), items Scrap exclus
        self.used = {KEY_NEXTFREE: 0, "StartWithArmorUsed": 0, KEY_INFINITE_ARMOR: 0, KEY_START_CLONE: 0}

        # Démarre le serveur TCP local immédiatement pour écouter le jeu, même hors connexion
        self.game_sync_task = asyncio.create_task(game_watcher(self), name="CloneDroneSync")

    async def server_auth(self, password_requested: bool = False):
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()

    def on_deathlink(self, data: dict):
        super().on_deathlink(data)
        asyncio.create_task(send_raw_to_game("DEATHLINK\n"))

    def on_package(self, cmd: str, args: dict):
        if cmd == "Connected":
            
            slot_data = args.get("slot_data", {})
            self.randomize_voices = int(slot_data.get("randomize_voices", 0))
            self.weapon_mode = int(slot_data.get("randomize_starting_weapon", 0))
            self.include_bow = int(slot_data.get("include_bow", 1))

            # Active le DeathLink si coché dans le YAML
            if slot_data.get("death_link", False):
                asyncio.create_task(self.update_death_link(True))

            step_count = slot_data.get("steps", 1)
            goal_value = slot_data.get("goal", 2)
            self.goal_n = GOAL_LEVELS.get(goal_value, 23)
            self.keys_mode = int(slot_data.get("multi_step_keys", 0))   # anciennes seeds : pas de clé
            self.scrap_collector = bool(slot_data.get("scrap_collector", 0))
            self.scrap_enabled = bool(slot_data.get("scrap_enabled", 0))
            self.scrap_coop = max(0, min(5, int(slot_data.get("scrap_coop", 0))))   # anciennes seeds : true = 1 (20 %)
            self.coop_contribs = {}
            self.scrap_earned = 0   # propre à CE slot : sinon, en changeant de slot dans le même client, le total de l'ancien slot est repris
            self.required_scrap = int(slot_data.get("required_scrap", 0))
            self.scrap_thresholds = [int(t) for t in slot_data.get("scrap_thresholds", [])]

            slot_name = self.auth or "UnknownSlot"
            room_name = getattr(self, "seed_name", None) or "DefaultRoom"

            scene_str = slot_data.get("scene")
            if scene_str:
                # source de vérité : la seed
                self.scene = Scene.parse_pool(scene_str) if self.scrap_collector else Scene.parse(scene_str)
            else:
                # Anciennes seeds sans "scene" : repli sur un tirage déterministe
                scenarios = []
                if slot_data.get("normal", 0):   scenarios.append(SCENARIO_NORMAL)
                if slot_data.get("inferno", 0):  scenarios.append(SCENARIO_INFERNO)
                if slot_data.get("sprinter", 0): scenarios.append(SCENARIO_SPRINTER)
                if slot_data.get("raptor", 0):   scenarios.append(SCENARIO_RAPTOR)
                if slot_data.get("random", 0):   scenarios.append(SCENARIO_RANDOM)
                if slot_data.get("armor", 0):    scenarios.append(SCENARIO_ARMOR)
                if slot_data.get("levelup", 0):  scenarios.append(SCENARIO_LEVELUP)
                if slot_data.get("easymode", 0): scenarios.append(SCENARIO_EASYMODE)
                if slot_data.get("undead", 0):   scenarios.append(SCENARIO_UNDEAD)
                if slot_data.get("doggos", 0): scenarios.append(SCENARIO_DOGGOS)
                if slot_data.get("summoners", 0):   scenarios.append(SCENARIO_SUMMONERS)

                if not scenarios:
                    scenarios = [SCENARIO_NORMAL]
                rng = random.Random(f"{getattr(self, 'seed_name', '') or ''}:{self.slot}")
                self.scene = Scene(generate_scene(scenarios, step_count, rng))

            # Progression restaurée depuis le serveur :
            # "Win Step r+1" coché = step r+1 fini (le check du niveau final ne suffit plus : avec les clés,
            # il part même quand le step n'est pas validé)
            checked = set(args.get("checked_locations", []))
            for r in range(step_count - 1):
                if win_step_location_id(r + 1) in checked:
                    self.scene.mark_done()

            tier = SCRAP_TIER.get(self.required_scrap, 0) if self.scrap_enabled else 0
            # ex. "uranium:1" (clés), "scrap_collector:10" (100k), "uranium:21" (1000k + clés) : le C# décode % 10 et / 10
            goal_str = f"{GOAL_NAMES.get(goal_value, 'uranium')}:{self.keys_mode + 10 * tier}"
            self.skillpoint_price = int(slot_data.get("skillpoint_price", 3))   # anciennes seeds : 3
            self.overpriced_shop = int(slot_data.get("overpriced_shop", 0))     # anciennes seeds : désactivé
            self.confusion = ",".join(str(int(slot_data.get(f"confusion_{n}", 0)))   # anciennes seeds : 0
                                      for n in ("inferno", "sprinter", "raptor", "random", "armor", "levelup", "easymode", "undead", "doggos", "summoners"))
            self.session_info = (slot_name, room_name, goal_str)
            asyncio.create_task(
                send_session_info_to_game(slot_name, room_name, goal_str, str(self.scene), self)
            )

            self.items_sent_to_game = 0

            self.used = {KEY_NEXTFREE: 0, "StartWithArmorUsed": 0, KEY_INFINITE_ARMOR: 0, KEY_START_CLONE: 0}
            keys = [self.storage_key(KEY_NEXTFREE), self.storage_key("StartWithArmorUsed"), self.storage_key(KEY_INFINITE_ARMOR), self.storage_key(KEY_START_CLONE), self.storage_key(KEY_SCRAP), self.coop_key()]
            asyncio.create_task(self.send_msgs([
                {"cmd": "SetNotify", "keys": keys},
                {"cmd": "Get", "keys": keys},
            ]))

        elif cmd == "Retrieved":
            changed = False
            for key, value in args.get("keys", {}).items():
                changed = self.apply_storage_value(key, value) or changed
            if changed:
                asyncio.create_task(send_used_to_game(self))
            asyncio.create_task(coop_sync(self))

        elif cmd == "SetReply":
            if self.apply_storage_value(args.get("key"), args.get("value")):
                asyncio.create_task(send_used_to_game(self))
                asyncio.create_task(coop_sync(self))

        elif cmd == "ReceivedItems":
            asyncio.create_task(send_pending_items_to_game(self))
            asyncio.create_task(check_scrap(self))   # un item Scrap / Big Scrap peut débloquer des paliers

    def item_name_for(self, item_id: int, slot: int) -> str:
        """Nom d'un item dans le jeu du slot receveur (les IDs dépendent du jeu du receveur)."""
        try:
            return self.item_names.lookup_in_slot(item_id, slot)
        except Exception:
            pass
        try:
            game = self.slot_info[slot].game
            return self.item_names.lookup_in_game(item_id, game)
        except Exception:
            return f"Item {item_id}"

    def is_stale_step_item(self, item_name: str) -> bool:
        """True pour un item "(Step N)" dont le step N est déjà fini : il ne servira plus à rien."""
        m = re.search(r" \(Step (\d+)\)$", item_name)
        return bool(m and self.scene is not None and int(m.group(1)) <= self.scene.finished)

    def on_print_json(self, args: dict):
        # ItemSend = event en direct (contrairement à ReceivedItems, qui rejoue tout l'historique à chaque connexion)
        if args.get("type") == "ItemSend" and self.slot is not None:
            try:
                item = args["item"]                 # NetworkItem : item.player = slot où le check a été trouvé
                receiver = int(args["receiving"])
                sender = int(item.player)
                me = int(self.slot)
                item_name = self.item_name_for(item.item, receiver)

                if receiver == me and self.is_stale_step_item(item_name):
                    super().on_print_json(args)   # reste visible dans la console, mais pas dans le HUD
                    return

                text = None
                if receiver == me and sender == me:
                    text = f"You got {item_name}"
                elif receiver == me:
                    text = f"{self.player_names[sender]} gave you {item_name}"
                elif sender == me:
                    text = f"{item_name} sent to {self.player_names[receiver]}"

                if text:
                    asyncio.create_task(send_notif_to_game(text))
            except Exception as e:
                logger.warning(f"[Notif] ItemSend ignoré : {e}")
        super().on_print_json(args)

    def scrap_bonus(self) -> int:
        """Scrap apportés par les items "Scrap" (500) et "Big Scrap" (5000) reçus."""
        return sum(SCRAP_ITEM_VALUES.get(self.item_names.lookup_in_game(n.item), 0) for n in self.items_received)

    def keys_received(self) -> int:
        """Nombre de "Progressive Key" reçues."""
        return sum(1 for n in self.items_received if self.item_names.lookup_in_game(n.item) == KEY_ITEM)

    def storage_key(self, name: str) -> str:
        return f"CloneDrone_{self.team}_{self.slot}_{name}"

    def coop_key(self) -> str:
        """Clé COMMUNE à toute la room (pas de slot) : {slot: contribution au pool}."""
        return f"CloneDrone_{self.team}_ScrapCoop"

    def scrap_coop_percent(self) -> int:
        """Pourcentage de MES scrap gagnés partagés avec les autres joueurs co-op (0 = co-op désactivé)."""
        return self.scrap_coop * COOP_STEP_PERCENT if self.scrap_enabled else 0

    def scrap_coop_shared(self) -> int:
        """Scrap que je partage : X % de mes scrap gagnés (arrondi à l'inférieur)."""
        return self.scrap_earned * self.scrap_coop_percent() // 100

    def scrap_coop_adjust(self) -> int:
        """Co-op : scrap reçus des AUTRES joueurs co-op (la somme de ce qu'ils partagent).
        Je garde 100 % de mes propres scrap : rien n'est retiré, les scrap partagés sont dupliqués."""
        if not self.scrap_coop_percent():
            return 0
        mine = str(self.slot)
        return sum(v for k, v in self.coop_contribs.items() if k != mine)

    def apply_storage_value(self, key, value) -> bool:
        if key == self.coop_key():
            self.coop_contribs = {str(k): int(v) for k, v in (value or {}).items()}
            return True
        if key == self.storage_key(KEY_SCRAP):
            self.scrap_earned = max(self.scrap_earned, int(value or 0))   # jamais à la baisse
            return True
        for name in (KEY_NEXTFREE, "StartWithArmorUsed", KEY_INFINITE_ARMOR, KEY_START_CLONE):
            if key == self.storage_key(name):
                self.used[name] = int(value or 0)
                return True
        return False

    def make_gui(self):
        ui = super().make_gui()
        ui.base_title = "Clone Drone Archipelago Client"
        return ui


BASE_SCRAP_LOW_LOCATION_ID = 84000   # doit rester synchronisé avec locations.py


def scrap_location_id(t: int) -> int:
    """ID du check d'un seuil : multiples de 1000 = palier normal, multiples de 10 = check "bas" (Scrap Collector)."""
    if t % SCRAP_STEP:
        return BASE_SCRAP_LOW_LOCATION_ID + t // 10
    return BASE_SCRAP_LOCATION_ID + t // SCRAP_STEP


async def coop_sync(ctx):
    """Co-op : publie la part de mes scrap gagnés que je partage, prévient le jeu de ce que les autres m'ont donné, puis revérifie."""
    if ctx.scrap_coop_percent() and ctx.server and ctx.slot:
        contrib = ctx.scrap_coop_shared()
        if ctx.coop_contribs.get(str(ctx.slot), -1) < contrib:
            await ctx.send_msgs([{
                "cmd": "Set", "key": ctx.coop_key(), "default": {}, "want_reply": False,
                "operations": [{"operation": "update", "value": {str(ctx.slot): contrib}}],
            }])
        await send_raw_to_game(f"COOP:{ctx.scrap_coop_adjust()}\n")
    await check_scrap(ctx)


async def check_scrap(ctx):
    """Coche les paliers de scrap atteints et valide la victoire du Scrap Collector.
    Total = scrap gagnés (jeu) + scrap des items Scrap / Big Scrap reçus. check_locations est idempotent."""
    if not ctx.scrap_enabled:
        return
    total = ctx.scrap_earned + ctx.scrap_bonus() + ctx.scrap_coop_adjust()
    ids = [scrap_location_id(t) for t in ctx.scrap_thresholds if total >= t]
    to_check = [i for i in ids if i in ctx.server_locations]
    if to_check and ctx.server and ctx.slot:
        await ctx.check_locations(to_check)
    if ctx.scrap_collector and total >= ctx.required_scrap and not ctx.finished_game:
        ctx.finished_game = True
        await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
        logger.info("Objectif atteint ! Scrap requis atteints.")


async def bump_storage(ctx, name: str):
    """+1 sur un compteur "utilisés" du DataStorage (atomique côté serveur). La réponse (SetReply) renvoie la valeur au jeu."""
    await ctx.send_msgs([{
        "cmd": "Set",
        "key": ctx.storage_key(name),
        "default": 0,
        "want_reply": True,
        "operations": [{"operation": "add", "value": 1}],
    }])


async def send_used_to_game(ctx):
    await send_raw_to_game(
        f"USED:NextFree:{ctx.used[KEY_NEXTFREE]}\n"
        f"USED:StartArmor:{ctx.used['StartWithArmorUsed']}\n"
        f"USED:60sInfiniteArmor:{ctx.used[KEY_INFINITE_ARMOR]}\n"
        f"USED:StartClone:{ctx.used[KEY_START_CLONE]}\n"
        f"USED:Scrap:{ctx.scrap_earned}\n"   # le jeu repart de 0 à chaque démarrage : on lui rend ses scrap gagnés
        + (f"COOP:{ctx.scrap_coop_adjust()}\n" if ctx.scrap_coop else "")
    )


async def send_session_info_to_game(slot: str, room: str, goal: str, scene: str, ctx=None):
    price = getattr(ctx, "skillpoint_price", 3) if ctx is not None else 3
    overpriced = getattr(ctx, "overpriced_shop", 0) if ctx is not None else 0
    confusion = getattr(ctx, "confusion", "0,0,0,0,0,0,0,0,0,0") if ctx is not None else "0,0,0,0,0,0,0,0,0,0"
    voices = getattr(ctx, "randomize_voices", 0) if ctx is not None else 0
    weapon_mode = getattr(ctx, "weapon_mode", 0) if ctx is not None else 0
    include_bow = getattr(ctx, "include_bow", 1) if ctx is not None else 1
    payload = f"SESSION:{slot}|{room}|{goal}|{scene}|{price}|{overpriced}|{confusion}|{voices}|{weapon_mode}|{include_bow}\n"
    while True:
        try:
            reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
            writer.write(payload.encode('utf-8'))
            await writer.drain()
            writer.close()
            await writer.wait_closed()
            logger.info(f"Submitted data to Clone Drone Patch (Slot: {slot}, Room: {room}, Goal: {goal}, Scene: {scene})")
            if ctx is not None:
                await send_used_to_game(ctx)   # le jeu remet ses compteurs à 0 sur SESSION : on lui renvoie les vrais
            break
        except Exception:
            await asyncio.sleep(3.0)


async def send_raw_to_game(message: str):
    try:
        reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
        writer.write(message.encode('utf-8'))
        await writer.drain()
        writer.close()
        await writer.wait_closed()
    except Exception as e:
        logger.error(f"[TCP Client] Can't send raw: {e}")


async def send_notif_to_game(text: str):
    """Message affiché dans le HUD. Silencieux si le jeu n'est pas lancé."""
    text = " ".join(text.split())   # une seule ligne : le protocole est découpé par \n
    try:
        reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
        writer.write(f"NOTIF:{text}\n".encode('utf-8'))
        await writer.drain()
        writer.close()
        await writer.wait_closed()
    except Exception:
        pass


async def send_pending_items_to_game(ctx: CloneDroneContext):
    # Par paquets : avec des milliers de checks il y a des milliers d'items, une connexion par item serait très lente.
    # Le verrou évite que deux tâches (ReceivedItems + game_watcher) envoient le même paquet deux fois.
    async with ctx.items_lock:
        while ctx.items_sent_to_game < len(ctx.items_received):
            start = ctx.items_sent_to_game
            batch = ctx.items_received[start:start + ITEM_BATCH_SIZE]
            names = [ctx.item_names.lookup_in_game(n.item) for n in batch]
            try:
                reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
                writer.write(("\n".join(names) + "\n").encode('utf-8'))
                await writer.drain()
                writer.close()
                await writer.wait_closed()
                ctx.items_sent_to_game = start + len(batch)
            except Exception:
                break


async def handle_game_check(reader, writer, ctx: CloneDroneContext):
    try:
        data = await reader.read(1024)
        message = data.decode('utf-8').strip()
    except Exception:
        message = ""
    finally:
        writer.close()
        try:
            await writer.wait_closed()
        except Exception:
            pass

    if message == "DEATH":
        if "DeathLink" in ctx.tags:
            await ctx.send_death(f"{ctx.auth} deathlink!")

    elif message in ("PING", "SYNC"):
        # Boutons du menu principal du jeu : "y a-t-il une session Archipelago ?"
        connected = bool(ctx.server and ctx.slot and ctx.scene is not None and ctx.session_info)
        if not connected:
            await send_raw_to_game("NOSESSION\n")
        elif message == "SYNC":
            # Le jeu n'a pas encore la session (démarré après le client) : on la renvoie avec les items
            slot, room, goal = ctx.session_info
            ctx.items_sent_to_game = 0
            await send_session_info_to_game(slot, room, goal, str(ctx.scene), ctx)
        else:
            await send_raw_to_game("PONG\n")

    elif message in ("NEXTFREE_USED", "STARTARMOR_USED", "INFINITEARMOR_USED", "STARTCLONE_USED"):
        if ctx.server and ctx.slot:
             key = {
                 "NEXTFREE_USED": KEY_NEXTFREE,
                 "STARTARMOR_USED": "StartWithArmorUsed",
                 "INFINITEARMOR_USED": KEY_INFINITE_ARMOR,
                 "STARTCLONE_USED": KEY_START_CLONE,
             }[message]
             await bump_storage(ctx, key)

    elif message.startswith("BUY:"):
        # Achat d'une upgrade dans la boutique : "BUY:<id>:<niveau>" où <id> = ID du niveau (UpgradesClass côté C#, UPGRADE_LEVEL_IDS).
        # Le niveau est alors redondant. Sans ID connu, le mod envoie "BUY:<UpgradeType>:<niveau>". Le check est relatif au step en cours.
        parts = message.split(":")
        if len(parts) == 3 and parts[2].isdigit() and ctx.scene is not None and not ctx.scene.complete:
            step = ctx.scene.finished + 1
            if parts[1].isdigit():
                loc_id = upgrade_location_id_by_level_id(step, int(parts[1]))
            else:
                loc_id = upgrade_location_id(step, parts[1], int(parts[2]))
            if loc_id and ctx.server and ctx.slot and loc_id in ctx.server_locations:
                await ctx.check_locations([loc_id])

    elif message.startswith("SCRAP:"):
        # Le jeu envoie ses scrap GAGNÉS ("SCRAP:<gagnés>"), sans les items Scrap : le total = gagnés + items (voir check_scrap).
        parts = message.split(":")
        if ctx.scrap_enabled and len(parts) == 2 and parts[1].isdigit():
            scrap = int(parts[1])
            if scrap > ctx.scrap_earned:
                ctx.scrap_earned = scrap
                if ctx.server and ctx.slot:
                    # "max" : le serveur garde le plus grand total (jamais à la baisse, même avec des messages dans le désordre)
                    await ctx.send_msgs([{
                        "cmd": "Set",
                        "key": ctx.storage_key(KEY_SCRAP),
                        "default": 0,
                        "want_reply": False,
                        "operations": [{"operation": "max", "value": scrap}],
                    }])
            await coop_sync(ctx)

    elif message.isdigit() and not ctx.scrap_collector:   # en Scrap Collector, il n'y a pas de niveaux : pas de victoire par niveau
        # Le mod envoie juste le niveau gagné
        level = int(message)
        loc_ids, victory, changed = resolve_check(ctx.scene, ctx.goal_n, level, ctx.keys_mode, ctx.keys_received())

        to_check = [i for i in loc_ids if i in ctx.server_locations]
        if to_check and ctx.server and ctx.slot:
            await ctx.check_locations(to_check)

        if victory and not ctx.finished_game:
            ctx.finished_game = True
            await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
            logger.info("Objectif atteint ! Victoire validée.")

        # Resync : le jeu doit repartir sur une nouvelle partie
        if ctx.scene is not None and (changed or (level > ctx.goal_n and ctx.keys_mode == 0)):
            await send_raw_to_game(f"SCENE:{ctx.scene}\n")


async def game_watcher(ctx: CloneDroneContext):
    try:
        server = await asyncio.start_server(
            lambda r, w: handle_game_check(r, w, ctx), '127.0.0.1', 38283
        )
        async with server:
            while not ctx.exit_event.is_set():
                if ctx.server and ctx.slot:
                    await send_pending_items_to_game(ctx)
                await asyncio.sleep(2.0)
    except Exception as e:
        logger.error(f"game_watcher TCP Error: {e}")


def launch(*args):
    async def main(args):
        parser = get_base_parser(description="Clone Drone Archipelago Client")
        parser.add_argument('--name', default=None, help="Slot Name to connect as.")
        parser.add_argument("url", nargs="?", help="Archipelago connection url")
        parsed_args, unknown = parser.parse_known_args(args)

        ctx = CloneDroneContext(parsed_args.connect, parsed_args.password)
        ctx.auth = parsed_args.name
        ctx.server_task = asyncio.create_task(server_loop(ctx), name="server loop")

        if gui_enabled:
            ctx.run_gui()
        ctx.run_cli()

        await ctx.exit_event.wait()
        await ctx.shutdown()

    import colorama
    colorama.just_fix_windows_console()
    asyncio.run(main(args))
    colorama.deinit()