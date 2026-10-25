import asyncio
import random
import Utils
from CommonClient import CommonContext, server_loop, gui_enabled, ClientCommandProcessor, logger, get_base_parser
from NetUtils import ClientStatus
from .upgrade_data import upgrade_location_id, win_step_location_id
from .scenarios import (
    generate_scene,
    SCENARIO_NORMAL, SCENARIO_INFERNO, SCENARIO_SPRINTER,
    SCENARIO_RAPTOR, SCENARIO_RANDOM, SCENARIO_ARMOR,
)

# --- Constantes (doivent rester synchronisées avec locations.py) ---
BASE_LOCATION_ID = 72000
STRIDE = 40
GOAL_LEVELS = {1: 15, 2: 23, 3: 33, 4: 4}
GOAL_NAMES = {1: "titanium", 2: "uranium", 3: "insanium", 4: "silver"}


class Scene:
    """Une entrée par step : X = scénario (-1 = step terminé), Y = arme de départ (0 à 3).
    Format texte : "X:Y;X:Y;..." (l'ancien format "X;X;..." est accepté, Y vaut alors 0)."""

    def __init__(self, values, weapons=None):
        self.values = values
        if weapons is None or len(weapons) != len(values):
            weapons = [0] * len(values)
        self.weapons = weapons

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

    def __str__(self):
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


def resolve_check(scene: Scene, goal_n: int, level: int):
    """Retourne (liste d'IDs de locations, victoire, scene_modifiée).

    Finir un step qui n'est pas le dernier valide deux checks : le niveau final du step
    et "Win Step X". Finir le dernier step = victoire (pas de location)."""
    if scene is None or scene.complete or level < 1:
        return [], False, False
    if level > goal_n:                       # dépassement : on ne casse rien
        return [], False, False
    finished = scene.finished
    is_last_redo = finished == len(scene.values) - 1
    ids = [BASE_LOCATION_ID + finished * STRIDE + level]
    if level == goal_n:                      # fin du step
        scene.mark_done()
        if is_last_redo:
            return [], True, True            # check ultime = victoire
        ids.append(win_step_location_id(finished + 1))
    return ids, False, level == goal_n


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
        super().__init__(server_address, password)
        self.items_sent_to_game = 0
        self.scene = None
        self.session_info = None   # (slot, room, goal) une fois connecté à Archipelago
        self.goal_n = GOAL_LEVELS[2]

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

            # Active le DeathLink si coché dans le YAML
            if slot_data.get("death_link", False):
                asyncio.create_task(self.update_death_link(True))

            redo = slot_data.get("redo", 1)
            goal_value = slot_data.get("goal", 2)
            self.goal_n = GOAL_LEVELS.get(goal_value, 23)

            slot_name = self.auth or "UnknownSlot"
            room_name = getattr(self, "seed_name", None) or "DefaultRoom"

            scene_str = slot_data.get("scene")
            if scene_str:
                self.scene = Scene.parse(scene_str)          # source de vérité : la seed
            else:
                # Anciennes seeds sans "scene" : repli sur un tirage déterministe
                scenarios = []
                if slot_data.get("normal", 0):   scenarios.append(SCENARIO_NORMAL)
                if slot_data.get("inferno", 0):  scenarios.append(SCENARIO_INFERNO)
                if slot_data.get("sprinter", 0): scenarios.append(SCENARIO_SPRINTER)
                if slot_data.get("raptor", 0):   scenarios.append(SCENARIO_RAPTOR)
                if slot_data.get("random", 0):   scenarios.append(SCENARIO_RANDOM)
                if slot_data.get("armor", 0):    scenarios.append(SCENARIO_ARMOR)
                if not scenarios:
                    scenarios = [SCENARIO_NORMAL]
                rng = random.Random(f"{getattr(self, 'seed_name', '') or ''}:{self.slot}")
                self.scene = Scene(generate_scene(scenarios, redo, rng))

            # Progression restaurée depuis le serveur :
            # le check du niveau goal_n d'un redo = redo fini
            checked = set(args.get("checked_locations", []))
            for r in range(redo - 1):
                if BASE_LOCATION_ID + r * STRIDE + self.goal_n in checked:
                    self.scene.mark_done()

            goal_str = GOAL_NAMES.get(goal_value, "uranium")
            self.session_info = (slot_name, room_name, goal_str)
            asyncio.create_task(
                send_session_info_to_game(slot_name, room_name, goal_str, str(self.scene))
            )

            self.items_sent_to_game = 0

        elif cmd == "ReceivedItems":
            asyncio.create_task(send_pending_items_to_game(self))

    def make_gui(self):
        ui = super().make_gui()
        ui.base_title = "Clone Drone Archipelago Client"
        return ui


async def send_session_info_to_game(slot: str, room: str, goal: str, scene: str):
    payload = f"SESSION:{slot}|{room}|{goal}|{scene}\n"
    while True:
        try:
            reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
            writer.write(payload.encode('utf-8'))
            await writer.drain()
            writer.close()
            await writer.wait_closed()
            logger.info(f"Submitted data to Clone Drone Patch (Slot: {slot}, Room: {room}, Goal: {goal}, Scene: {scene})")
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


async def send_pending_items_to_game(ctx: CloneDroneContext):
    while ctx.items_sent_to_game < len(ctx.items_received):
        network_item = ctx.items_received[ctx.items_sent_to_game]
        item_name = ctx.item_names.lookup_in_game(network_item.item)
        try:
            reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
            writer.write(f"{item_name}\n".encode('utf-8'))
            await writer.drain()
            writer.close()
            await writer.wait_closed()
            ctx.items_sent_to_game += 1
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
            await send_session_info_to_game(slot, room, goal, str(ctx.scene))
        else:
            await send_raw_to_game("PONG\n")

    elif message.startswith("BUY:"):
        # Achat d'une upgrade dans la boutique : "BUY:<UpgradeType>:<niveau>". Le check est relatif au step en cours.
        parts = message.split(":")
        if len(parts) == 3 and parts[2].isdigit() and ctx.scene is not None and not ctx.scene.complete:
            step = ctx.scene.finished + 1
            loc_id = upgrade_location_id(step, parts[1], int(parts[2]))
            if loc_id and ctx.server and ctx.slot and loc_id in ctx.server_locations:
                await ctx.check_locations([loc_id])

    elif message.isdigit():
        # Le mod envoie juste le niveau gagné
        level = int(message)
        loc_ids, victory, changed = resolve_check(ctx.scene, ctx.goal_n, level)

        to_check = [i for i in loc_ids if i in ctx.server_locations]
        if to_check and ctx.server and ctx.slot:
            await ctx.check_locations(to_check)

        if victory and not ctx.finished_game:
            ctx.finished_game = True
            await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
            logger.info("Objectif atteint ! Victoire validée.")

        # Resync : le jeu doit repartir sur une nouvelle partie
        if ctx.scene is not None and (changed or level > ctx.goal_n):
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