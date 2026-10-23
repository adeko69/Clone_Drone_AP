# Clone_Drone_AP
A Clone Drone in The Danger Zone Repository for Archipelago.

## Required Software

- A computer version of Clone Drone in the Danger Zone installed with/near Steam ID build `25044987` (Night Mode Update). apworld and patch v0.1.X should also works on The Enemy Zombie Adventure Update and later update, but some later feature won't work (ride or die upgrades). This version is potentially compatible with later version (purchasable from most online game stores)
- [BepInEx x64](https://github.com/bepinex/bepinex/releases). The game being x64, x86 won't work.
- The `.apworld` and the `.dll` in the releases

## Installation and Patch Procedures
### Windows

1. Locate `Clone Drone in the Danger Zone.exe` and `Clone Drone in the Danger Zone_Data/`, they are in the same file. Starting now, this location will be the "root" (also called `./` when moving in files). You can locate it inside Steam
2. Close Steam entirely
3. Drop all `BepInEx` (extracted) file in the root. This is the patcher, but you still need the patch.
4. Create a new file `steam_appid.txt` in the root containing only "597170" (put Clone Drone in offline mode, so steam wont fuck everything up)
5. Open `./BepInEx/` it should only contains "core". 
6. Go back, open `Clone Drone in the Danger Zone.exe` and use menu "quit" button
7. Open `./BepInEx/` it should now contains 6 elements instead of 1
8. Go in `./BepInEx/plugins/`, it should be empty. Put `CloneDroneAP.dll` in here
9. Launch the game from `Clone Drone in the Danger Zone.exe` while Steam is closed.

## Check if patch is on

10. Launch `Clone Drone in the Danger Zone.exe`, if Steam appears it will unpatch, so patch wont work. Go back to step 2 and 4 and try again
11. If "Steam Connect Failed" appear, look at "Play Multiplayer" button. If it is greyed out but without a "Can't connect to online services", it means the patch is on. Click on Browse Workshop, it shouldnt work. Well down, your game is patched.

## Joining a world

Install the apworld in release by double clicking it
Launch Archipelago Launcher and find Clone Drone Client.

> [!IMPORTANT]
> Connect to the world, then enter your game slot, AND ONLY THEN launch `Clone Drone in the Danger Zone.exe`. Otherwise data wont be send. If you leave the game, your current progress in game (current endless level) is saved. BUT you'll need to disconnect to the world (using the disconnect button) and redo the last part to connect back

## Remove patch
To restore the normal game, just rename "./BepInEx" as "./.BepInEx/" (just put a point behind). Patcher wont work and game will be restored without the patch being active
## Edit of the Patch

I allow player to edit, fork and modify this patch. I'm a sole java dev doing it as hobby and this project is in C# and python so there is maybe major issues lol. Try contacting me @adeko69 on discord to push update and review things
