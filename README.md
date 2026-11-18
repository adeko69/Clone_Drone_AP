# Clone_Drone_AP
A Clone Drone in The Danger Zone Repository for Archipelago.

## Required Software

- v0.1.X: Any Clone Drone in the Danger Zone version released after 2023
- v0.2.X and later: Any Clone Drone in the Danger Zone version released after 2023 (just ensure have RIP Riders in your game)
- v1.3 and later: Any Clone Drone in the Danger Zone version released after 10th year anniversary (just ensure having latest version)
- [BepInEx x64](https://github.com/bepinex/bepinex/releases). The game being x64, x86 won't work.
- The `.apworld` and the `.dll` in the releases

## Installation and Patch Procedures
### Windows

1. Locate `Clone Drone in the Danger Zone.exe` and `Clone Drone in the Danger Zone_Data/`, they are in the same file. Starting now, this location will be the "root" (also called `./` when moving in files). You can locate it inside Steam <img width="424" height="322" alt="image" src="https://github.com/user-attachments/assets/1703c0d5-f979-4e24-93dd-273d96c1ebfa" />

2. Close Steam entirely (DO NOT FORGET IN TASKBAR HIDDEN APPS, click "Leave Steam" or equivalent in your language, here French)<img width="469" height="861" alt="image" src="https://github.com/user-attachments/assets/84025ffe-4eb3-45ed-bde1-b158c6a84586" />

3. Drop all `BepInEx` (extracted) file in the root. This is the patcher, but you still need the patch.<img width="396" height="474" alt="image" src="https://github.com/user-attachments/assets/f1c14d11-59a0-4a00-b0d7-3b8d86649618" />

4. Create a new file `steam_appid.txt` in the root containing only "597170" (put Clone Drone in offline mode, so steam wont fuck everything up), in image: Japanese; check the number of character, it should says 6 characters (6文字 in Japanese) <img width="537" height="1087" alt="image" src="https://github.com/user-attachments/assets/37e4d936-7796-49f3-a765-e5cbc106a80d" />

5. Open `./BepInEx/` it should only contains "core".<img width="451" height="424" alt="image" src="https://github.com/user-attachments/assets/4747711a-143a-426b-81ea-04a2ba987876" />

6. Go back, open `Clone Drone in the Danger Zone.exe` and use menu "quit" button<img width="358" height="102" alt="image" src="https://github.com/user-attachments/assets/113c0004-2d86-4641-83a0-aa7559b0eb89" />

7. Open `./BepInEx/` it should now contains 6 elements instead of 1<img width="417" height="369" alt="image" src="https://github.com/user-attachments/assets/95620d78-75db-4b5f-ab6a-9734061ecc4e" />

8. Go in `./BepInEx/plugins/`, it should be empty. Put `CloneDroneAP.dll` in here<img width="435" height="229" alt="image" src="https://github.com/user-attachments/assets/1e9b5c51-09a7-46da-9385-a478be787d2a" />

9. Launch the game from `Clone Drone in the Danger Zone.exe` while Steam is closed.

## Check if patch is on

10. Launch `Clone Drone in the Danger Zone.exe`, if Steam appears it will unpatch, so patch wont work. Go back to step 2 and 4 and try again
11. Look at "Play Multiplayer" button. If it is greyed out but without a "Can't connect to online services", it means the patch is on. Click on Browse Workshop, it shouldnt work. Well down, your game is patched.

## Joining a world

Install the apworld in release by double clicking it
Launch Archipelago Launcher and find Clone Drone Client.

> [!IMPORTANT]
> Connect to the world, then enter your game slot, AND ONLY THEN launch `Clone Drone in the Danger Zone.exe`. Otherwise data wont be send. If you leave the game, your current progress in game (current endless level) is saved. BUT you'll need to disconnect to the world (using the disconnect button) and redo the last part to connect back

## Remove patch
To restore the normal game, just rename "./BepInEx" as "./.BepInEx/" (just put a point behind). Patcher wont work and game will be restored without the patch being active

## Edit of the Patch

I allow player to edit, fork and modify this patch. I'm a sole java dev doing it as hobby and this project is in C# and python so there is maybe major issues lol. Try contacting me @adeko69 on discord to push update and review things

## Unmaintenance of the code

Starting v2.0.1, I'll no longer push update except major game update. The reason is that I wanted an AP of this game to play with my friend, and now that everything I wanted to add is there, I don't need other features for now. If I have more things to had I may do it, but I'm already on my limits on some things (ex. add Emperor in the endless mode is more likely impossible except for the devs themself)
