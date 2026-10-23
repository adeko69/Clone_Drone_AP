import asyncio
import socket
import Utils
from CommonClient import CommonContext, server_loop, gui_enabled, ClientCommandProcessor, logger, get_base_parser
from NetUtils import ClientStatus

class CloneDroneCommandProcessor(ClientCommandProcessor):
    def _cmd_clonedrone(self):
        """Vérifie l'état de la connexion avec le mod Clone Drone."""
        self.output("Client Clone Drone opérationnel.")

class CloneDroneContext(CommonContext):
    command_processor = CloneDroneCommandProcessor
    game = "Clone Drone in the Danger Zone"
    items_handling = 0b111  # Reçoit tous les items

    def __init__(self, server_address, password):
        super().__init__(server_address, password)
        self.items_sent_to_game = 0
        
        # Démarre le serveur TCP local immédiatement pour écouter le jeu, même hors connexion
        self.game_sync_task = asyncio.create_task(game_watcher(self), name="CloneDroneSync")

    async def server_auth(self, password_requested: bool = False):
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()

    def on_package(self, cmd: str, args: dict):
        if cmd == "Connected":
            logger.info("Connecté au serveur Archipelago ! Récupération des infos...")
            
            # Nom du slot (ex: adekoDrone)
            slot_name = self.auth or "UnknownSlot"
            
            # Nom de la room / seed de la room (ex: WXYZ12345 ou le nom de la room)
            # On utilise server_seed_name s'il existe, sinon on prend une version nettoyée de l'adresse
            room_name = getattr(self, 'server_seed_name', None) or "DefaultRoom"
            
            # Récupère le goal du slot_data
            slot_data = args.get("slot_data", {})
            goal_value = slot_data.get("goal", 2)
            goal_mapping = {1: "titanium", 2: "uranium", 3: "insanium", 4: "bronze"}
            goal_str = goal_mapping.get(goal_value, "uranium")
            
            # Envoie tout au mod C# via le port TCP 38282
            asyncio.create_task(send_session_info_to_game(slot_name, room_name, goal_str))
            
            self.items_sent_to_game = 0
            
        elif cmd == "ReceivedItems":
            asyncio.create_task(send_pending_items_to_game(self))

async def send_session_info_to_game(slot: str, room: str, goal: str):
    """Transmet le slot, la room et le goal au mod C#."""
    payload = f"SESSION:{slot}|{room}|{goal}\n"
    while True:
        try:
            reader, writer = await asyncio.open_connection('127.0.0.1', 38282)
            writer.write(payload.encode('utf-8'))
            await writer.drain()
            writer.close()
            await writer.wait_closed()
            logger.info(f"Submitted data to Clone Drone Patch (Slot: {slot}, Room: {room}, Goal: {goal})")
            break
        except Exception:
            await asyncio.sleep(3.0)

    def make_gui(self):
        ui = super().make_gui()
        ui.base_title = "Clone Drone Archipelago Client"
        return ui

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

    if message.isdigit():
        check_offset = int(message)
        location_id = 72000 + check_offset
        logger.info(f"Check reçu du jeu : ID {location_id}")
        
        if ctx.server and ctx.slot:
            await ctx.check_locations([location_id])

        if check_offset == 106 and not ctx.finished_game:
            ctx.finished_game = True
            if ctx.server and ctx.slot:
                await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
                logger.info("Victory")

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