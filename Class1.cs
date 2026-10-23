using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace CloneDroneAP
{
    [BepInPlugin("com.tonnom.clonedrone.ap", "Clone Drone Archipelago", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static bool IsReceivingFromArchipelago = false;
        public static int TotalFreeUpgradesReceived = 0;
        public static int NextUpgradesFree = 0;
        public static int ShopPriceReduction = 0;

        public static Dictionary<int, bool> LocalChecks = new Dictionary<int, bool>();
        public static List<UpgradeType> UnlockedUpgrades = new List<UpgradeType>();

        public static ConcurrentQueue<string> PendingItems = new ConcurrentQueue<string>();
        private static TcpListener tcpServer;
        private static Thread listenerThread;
        public static string CurrentGoal = "uranium";
        private static Text hudText = null;
        public static string CurrentSlotName = "DefaultSlot";
        public static string CurrentRoomName = "DefaultRoom";

        public static bool IsDyingFromDeathLink = false;

        public static void SendDeathToPython()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes("DEATH");
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch { }
            });
        }

        public static void KillPlayerFromDeathLink()
        {
            if (Singleton<CharacterTracker>.Instance == null || Singleton<GameFlowManager>.Instance == null) return;

            // Sécurité : on ne tue pas le joueur s'il est dans le menu principal
            if (Singleton<GameFlowManager>.Instance.IsOnTitleScreen()) return;

            FirstPersonMover player = Singleton<CharacterTracker>.Instance.GetPlayerRobot();

            // On vérifie qu'il est en vie et qu'il n'est pas en train de subir une animation d'amélioration à l'autel
            if (player != null && player.IsAlive())
            {
                Log.LogInfo("[DEATHLINK] Un allié est mort ! Destruction du robot...");
                IsDyingFromDeathLink = true;
                try
                {
                    // 1. Passe le booléen interne _isAlive (hérité de Character) à false
                    AccessTools.Field(typeof(Character), "_isAlive")?.SetValue(player, false);

                    // 2. Appelle la méthode protégée onDeath(Character killer, DamageSourceType damageSourceType, int attackID)
                    var onDeathMethod = AccessTools.Method(typeof(FirstPersonMover), "onDeath");
                    if (onDeathMethod != null)
                    {
                        onDeathMethod.Invoke(player, new object[] { null, DamageSourceType.None, -1 });
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[DEATHLINK] Erreur lors de la destruction du joueur : {ex.Message}");
                }
                finally
                {
                    IsDyingFromDeathLink = false;
                }
            }
        }

        public class ApItemDefinition
        {
            public List<UpgradeType> UpgradesToUnlock = new List<UpgradeType>();
        }

        public static Dictionary<string, ApItemDefinition> ApItemDatabase = new Dictionary<string, ApItemDefinition>()
        {
            { "BlockArrows", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.BlockArrows } } },
            { "EnergyCapacity", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.EnergyCapacity } } },
            { "EnergyRecharge", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.EnergyRecharge } } },
            { "KickUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.KickUnlock } } },
            { "KickPower+GetUp", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.KickPower, UpgradeType.GetUp } } },
            { "BowUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.BowUnlock } } },
            { "ArrowWidth", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.ArrowWidth } } },
            { "AimTime", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.AimTime } } },
            { "FireArrow", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireArrow } } },
            { "Jetpack", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Jetpack } } },
            { "Clone", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Clone } } },
            { "Armor+Repair", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Armor, UpgradeType.Repair } } },
            { "Hammer", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Hammer } } },
            { "FireSword", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireSword } } },
            { "FireHammer", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireHammer } } },
            { "SpearUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SpearUnlock } } },
            { "FireSpear+ShieldBash", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireSpear, UpgradeType.ShieldBash, UpgradeType.ShieldSize, UpgradeType.EMPSpear, UpgradeType.UnbalanceOpponentSpear } } },
            { "FireBreath+FireResistance", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FlameBreath, UpgradeType.FireResistance } } }
        };

        private void Awake()
        {
            Log = Logger;
            try
            {
                Harmony harmony = new Harmony("com.tonnom.clonedrone.ap");
                harmony.PatchAll();

                UnlockedUpgrades.Add(UpgradeType.BlockSwords);

                for (int i = 1; i <= 55; i++)
                {
                    LocalChecks[i] = false;
                }

                StartTcpServer();
                StartCoroutine(HudUpdateRoutine());
            }
            catch (System.Exception ex)
            {
                Log.LogError("Error in Awake : " + ex.ToString());
            }
        }

        private void StartTcpServer()
        {
            try
            {
                tcpServer = new TcpListener(IPAddress.Loopback, 38282);
                tcpServer.Start();
                listenerThread = new Thread(ListenForPythonItems) { IsBackground = true };
                listenerThread.Start();
            }
            catch (System.Exception ex)
            {
                Log.LogError("[AP TCP] Failed to start TCP server : " + ex.Message);
            }
        }

        private void ListenForPythonItems()
        {
            while (true)
            {
                try
                {
                    using (TcpClient client = tcpServer.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] buffer = new byte[1024];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                        if (!string.IsNullOrEmpty(message))
                        {
                            string[] items = message.Split('\n');
                            foreach (string item in items)
                            {
                                string cleanItem = item.Trim();
                                if (!string.IsNullOrEmpty(cleanItem))
                                {
                                    PendingItems.Enqueue(cleanItem);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        public static void RecalculateFreeUpgrades()
        {
            int usedCount = getNextFreeUsed();
            NextUpgradesFree = Mathf.Max(0, TotalFreeUpgradesReceived - usedCount);
            UpdateHudDisplay();
        }

        private void Update()
        {
            while (PendingItems.TryDequeue(out string message))
            {
                if (message == "DEATHLINK")
                {
                    KillPlayerFromDeathLink();
                }
                if (message.StartsWith("SESSION:"))
                {
                    string data = message.Substring(8);
                    string[] parts = data.Split('|');
                    if (parts.Length >= 3)
                    {
                        CurrentSlotName = parts[0].Trim();
                        CurrentRoomName = parts[1].Trim();
                        CurrentGoal = parts[2].Trim().ToLower();

                        Log.LogInfo($"[AP SESSION] Slot : {CurrentSlotName} | Room : {CurrentRoomName} | Goal : {CurrentGoal.ToUpper()}");
                        RecalculateFreeUpgrades();
                    }
                }
                else
                {
                    ap_grant_item(message);
                }
            }
        }

        private IEnumerator HudUpdateRoutine()
        {
            while (true)
            {
                UpdateHudDisplay();
                yield return new WaitForSeconds(2f);
            }
        }

        public static void UpdateHudDisplay()
        {
            if (NextUpgradesFree <= 0)
            {
                if (hudText != null)
                {
                    Destroy(hudText.gameObject);
                    hudText = null;
                }
                return;
            }

            if (hudText == null)
            {
                Canvas canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null)
                {
                    GameObject textObj = new GameObject("AP_HudText");
                    textObj.transform.SetParent(canvas.transform, false);

                    hudText = textObj.AddComponent<Text>();
                    hudText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    hudText.fontSize = 24;
                    hudText.color = Color.yellow;

                    RectTransform rect = hudText.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(1, 1);
                    rect.anchorMax = new Vector2(1, 1);
                    rect.pivot = new Vector2(1, 1);
                    rect.anchoredPosition = new Vector2(-20, -20);
                    rect.sizeDelta = new Vector2(250, 50);
                }
            }

            if (hudText != null)
            {
                hudText.text = $"Free Upgrade : {NextUpgradesFree}";
            }
        }

        public static void SendCheckToPython(int checkOffset)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(checkOffset.ToString());
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch
                {
                }
            });
        }

        public static string GetSaveFilePath()
        {
            string saveDir = Path.Combine(Paths.ConfigPath, "CloneDroneAP", "save");
            Directory.CreateDirectory(saveDir);

            string safeSlot = string.Join("_", CurrentSlotName.Split(Path.GetInvalidFileNameChars()));
            string safeRoom = string.Join("_", CurrentRoomName.Split(Path.GetInvalidFileNameChars()));

            return Path.Combine(saveDir, $"{safeSlot}_{safeRoom}_used.txt");
        }

        public static int getNextFreeUsed()
        {
            try
            {
                string path = GetSaveFilePath();
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path).Trim();
                    if (int.TryParse(content, out int value))
                    {
                        return value;
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.LogError($"[SAVE] Can't read: {e.Message}");
            }
            return 0;
        }

        public static void SetUsedNextFreeCount(int count)
        {
            try
            {
                string path = GetSaveFilePath();
                File.WriteAllText(path, count.ToString());
            }
            catch (System.Exception e)
            {
                Log.LogError($"[SAVE] Can't write: {e.Message}");
            }
        }

        public static void ap_grant_item(string itemName)
        {
            Log.LogInfo($"[ARCHIPELAGO] Got : {itemName}");

            if (itemName.StartsWith("ShopDiscount"))
            {
                ShopPriceReduction++;
                if (UpgradeManager.Instance != null)
                {
                    UpgradeManager.Instance.RefreshUpgradeStationsVisibility();
                }
                return;
            }

            if (itemName.StartsWith("NextUpgradeFree") || itemName.Contains("FreeUpgrade"))
            {
                TotalFreeUpgradesReceived++;
                RecalculateFreeUpgrades();
                if (UpgradeManager.Instance != null)
                {
                    UpgradeManager.Instance.RefreshUpgradeStationsVisibility();
                }
                return;
            }

            if (ApItemDatabase.ContainsKey(itemName))
            {
                ApItemDefinition itemDef = ApItemDatabase[itemName];

                foreach (UpgradeType type in itemDef.UpgradesToUnlock)
                {
                    if (!UnlockedUpgrades.Contains(type))
                    {
                        UnlockedUpgrades.Add(type);
                    }
                }

                if (UpgradeManager.Instance != null)
                {
                    UpgradeManager.Instance.RefreshUpgradeStationsVisibility();
                }
            }
            else
            {
                Log.LogWarning($"[ARCHIPELAGO] Unknown Item : {itemName}");
            }
        }

        public static void GiveItemToPlayer(UpgradeType type, int level)
        {
            if (UpgradeManager.Instance == null || GameDataManager.Instance == null) return;
            UpgradeDescription desc = UpgradeManager.Instance.GetUpgrade(type, level);
            if (desc == null) return;

            if (!desc.IsRepeatable)
            {
                GameDataManager.Instance.SetUpgradeLevel(desc.UpgradeType, desc.Level);
            }
            else
            {
                int playerUpgradeLevel = UpgradeManager.Instance.GetPlayerUpgradeLevel(desc.UpgradeType);
                GameDataManager.Instance.SetUpgradeLevel(desc.UpgradeType, playerUpgradeLevel + 1);
            }

            if (Singleton<GlobalEventManager>.Instance != null)
            {
                Singleton<GlobalEventManager>.Instance.Dispatch("UpgradeCompleted", desc);
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), "MarkCurrentLevelAsWon")]
    public class LevelWonPatch
    {
        static void Postfix()
        {
            int totalLevelsWon = LevelManager.Instance.GetNumberOfLevelsWon();

            if (totalLevelsWon >= 1 && totalLevelsWon <= 50)
            {
                Plugin.LocalChecks[totalLevelsWon] = true;
                Plugin.SendCheckToPython(totalLevelsWon);
            }

            if (totalLevelsWon > 0 && totalLevelsWon % 10 == 0)
            {
                int currentChapter = totalLevelsWon / 10;
                int victoryCheckOffset = 100 + currentChapter;
                Plugin.SendCheckToPython(victoryCheckOffset);
            }

            if (EndlessModeManager.Instance != null)
            {
                bool goalAchieved = false;

                if (Plugin.CurrentGoal == "bronze" && EndlessModeManager.Instance.HasReached(DifficultyTier.Bronze))
                {
                    goalAchieved = true;
                }
                else if (Plugin.CurrentGoal == "titanium" && EndlessModeManager.Instance.HasReached(DifficultyTier.Titanium))
                {
                    goalAchieved = true;
                }
                else if (Plugin.CurrentGoal == "uranium" && EndlessModeManager.Instance.HasReached(DifficultyTier.Uranium))
                {
                    goalAchieved = true;
                }
                else if (Plugin.CurrentGoal == "insanium" && EndlessModeManager.Instance.HasReached(DifficultyTier.Insanium))
                {
                    goalAchieved = true;
                }

                if (goalAchieved)
                {
                    Plugin.SendCheckToPython(106);
                }
            }
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "CanUpgrade")]
    public class CanUpgradePatch
    {
        static bool Prefix(UpgradeDescription upgradeDescription, ref bool __result)
        {
            if (Plugin.IsReceivingFromArchipelago) return true;

            bool isUnlocked = Plugin.UnlockedUpgrades.Contains(upgradeDescription.UpgradeType);
            if (!isUnlocked)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UpgradeDescription), "GetSkillPointCost")]
    public class UpgradeCostPatch
    {
        static void Postfix(ref int __result)
        {
            if (Plugin.NextUpgradesFree > 0)
            {
                __result = 0;
            }
            else
            {
                int discountedMultiplier = Mathf.Max(1, 3 - Plugin.ShopPriceReduction);
                if (Plugin.ShopPriceReduction == 3)
                {
                    __result = 1;
                }
                else
                {
                    __result *= discountedMultiplier;
                }
            }
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "TryUpgradeAbility")]
    public class TryUpgradePatch
    {
        static bool Prefix(UpgradeDescription upgradeDescription, bool isRandomSelection, ref bool __result)
        {
            if (Plugin.IsReceivingFromArchipelago) return true;

            if (!UpgradeManager.Instance.CanUpgrade(upgradeDescription))
            {
                __result = false;
                return false;
            }

            // 2. GESTION DU FREE UPGRADE
            if (Plugin.NextUpgradesFree > 0)
            {
                int currentUsed = Plugin.getNextFreeUsed();
                Plugin.SetUsedNextFreeCount(currentUsed + 1);
                Plugin.RecalculateFreeUpgrades();
                Plugin.GiveItemToPlayer(upgradeDescription.UpgradeType, upgradeDescription.Level);
                __result = true;
                return false;
            }

            int currentPoints = UpgradeManager.Instance.GetAvailableSkillPoints();
            int cost = upgradeDescription.GetSkillPointCost();
            if (currentPoints >= cost)
            {
                UpgradeManager.Instance.SetAvailableSkillPoints(currentPoints - cost);
                Plugin.GiveItemToPlayer(upgradeDescription.UpgradeType, upgradeDescription.Level);
                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiplayerButtonController), "OnEnable")]
    public class DisableMultiplayerButtonPatch
    {
        static bool Prefix(MultiplayerButtonController __instance)
        {

            if (__instance.MultiplayerButton != null) __instance.MultiplayerButton.interactable = false;
            if (__instance.ErrorPopup != null) __instance.ErrorPopup.Hide();
            if (__instance.Sawblade != null) __instance.Sawblade.SetActive(false);

            return false;
        }
    }

    [HarmonyPatch(typeof(MultiplayerLoginManager), "AuthorizeWithPlayfab")]
    public class DisableMultiplayerLoginPatch
    {
        static bool Prefix(bool tryResolvePrivileges)
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkshopLevelManager), "WatchAndLoadWorkshopLevels")]
    public class DisableWorkshopWatchPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }
    [HarmonyPatch(typeof(TitleScreenUI), "OnWorkshopBrowserButtonClicked")]
    public class BlockWorkshopClickPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(GameFlowManager), "onCharacterKilled")]
    public class PlayerDeathLinkPatch
    {
        static void Postfix(Character character)
        {
            // 1. Si c'est déjà le DeathLink qui vient de nous tuer, on ne renvoie pas de signal (évite la boucle infinie)
            if (Plugin.IsDyingFromDeathLink) return;

            // 2. On vérifie que le personnage qui vient de mourir est bien le joueur principal (et pas un ennemi ou un allié IA)
            if (character != null && character.IsMainPlayer() && !character.IsNeutral)
            {
                // Ignore la "mort" technique lorsque la station d'amélioration répare ou reconstruit le corps
                if (Singleton<UpgradeManager>.Instance != null &&
                    Singleton<UpgradeManager>.Instance.UpgradeStation != null &&
                    Singleton<UpgradeManager>.Instance.UpgradeStation.IsRepairInProgress())
                {
                    return;
                }

                Plugin.Log.LogInfo("[DEATHLINK] Le joueur est mort ! Envoi du DeathLink à Archipelago...");
                Plugin.SendDeathToPython();
            }
        }
    }

}