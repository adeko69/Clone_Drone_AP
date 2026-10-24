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

        public const int ScenarioNormal = 0;
        public const int ScenarioInferno = 1;
        public const int ScenarioSprinter = 2;  
        public const int ScenarioRaptor = 3;
        public const int ScenarioRandom = 4;
        public const int ScenarioArmor = 5;

        public static List<int> Scene = new List<int>();
        public static bool NeedsNewRun = false; 
        public static Plugin Instance;
        public static bool AutoResetOnRedoComplete = true;
        public static float ResetDelaySeconds = 4f;
        private static bool _resetScheduled = false;

        public static bool ScenarioActive(int scenario)
        {
            if (CurrentScenario != scenario) return false;
            GameFlowManager flow = Singleton<GameFlowManager>.Instance;
            return flow != null && !flow.IsOnTitleScreen();
        }

        public static void ScheduleRedoReset()
        {
            if (!AutoResetOnRedoComplete || _resetScheduled || Instance == null) return;
            if (Scene.Count == 0 || SceneComplete) return;
            _resetScheduled = true;
            Instance.StartCoroutine(RedoResetRoutine());
        }

        private static IEnumerator RedoResetRoutine()
        {
            yield return new WaitForSeconds(ResetDelaySeconds);
            try
            {
                GameFlowManager flow = Singleton<GameFlowManager>.Instance;
                if (flow != null && !flow.IsOnTitleScreen() && !SceneComplete)
                {
                    flow.ResetGameAndSpawnHuman(resetIsFromLoss: false);
                    NeedsNewRun = false;
                    UpdateHudDisplay();
                }
            }
            catch (System.Exception ex)
            {
                // NeedsNewRun reste à true : le HUD demandera un reset manuel
                Log.LogError("[AP] Auto reset failed: " + ex.Message);
            }
            finally
            {
                _resetScheduled = false;
            }
        }

        public static int GetGoalLevel()
        {
            switch (CurrentGoal)
            {
                case "titanium": return 15;
                case "uranium": return 23;
                case "insanium": return 33;
                case "silver": return 4;
                default: return -1;
            }
        }

        public static List<int> ParseScene(string s)
        {
            List<int> result = new List<int>();
            if (string.IsNullOrEmpty(s)) return result;
            foreach (string part in s.Split(';'))
            {
                int v;
                if (int.TryParse(part.Trim(), out v)) result.Add(v);
            }
            return result;
        }

        public static string SceneToString()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Scene.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(Scene[i]);
            }
            return sb.ToString();
        }

        public static int FinishedRedos
        {
            get
            {
                int n = 0;
                foreach (int v in Scene) if (v == -1) n++;
                return n;
            }
        }

        public static bool SceneComplete
        {
            get { return Scene.Count > 0 && FinishedRedos >= Scene.Count; }
        }

        public static int CurrentScenario
        {
            get
            {
                foreach (int v in Scene) if (v != -1) return v;
                return -1;
            }
        }

        public static bool IsInfernoActive
        {
            get { return CurrentScenario == ScenarioInferno; }
        }

        public static bool MarkCurrentRedoDone()
        {
            for (int i = 0; i < Scene.Count; i++)
            {
                if (Scene[i] != -1)
                {
                    Scene[i] = -1;
                    return true;
                }
            }
            return false;
        }

        public static string ScenarioName(int scenario)
        {
            switch (scenario)
            {
                case ScenarioNormal: return "None";
                case ScenarioInferno: return "Inferno";
                case ScenarioSprinter: return "Jetpack";
                case ScenarioRaptor: return "Raptor Rider";
                case ScenarioRandom: return "Enemy Randomized";
                case ScenarioArmor: return "Armored";
                default: return "Scenario " + scenario;
            }
        }

        // ------------------------------------------------------------------

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
            { "HammerUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Hammer } } },
            { "FireSword", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireSword } } },
            { "FireHammer+FireSpear", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireHammer, UpgradeType.FireSpear } } },
            { "SpearUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SpearUnlock } } },
            { "SpearShield+ShieldBash", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.ShieldBash, UpgradeType.ShieldSize, UpgradeType.EMPSpear, UpgradeType.UnbalanceOpponentSpear } } },
            { "FireBreath+FireResistance", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FlameBreath, UpgradeType.FireResistance } } },
            { "Riding", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SummonFlameRaptor, UpgradeType.SummonSawbladeUnicycle } } },
            { "Raptor Riding", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SummonFlameRaptor } } },
            { "Unicycle Riding", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SummonSawbladeUnicycle } } },
            { "SpearShield", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.ShieldSize } } },
            { "ShieldBash", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.ShieldBash } } },
            { "Armor", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Armor } } },
            { "Repair", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.Repair } } },
            { "KickPower", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.KickPower } } },
            { "GetUp", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.GetUp } } },
            { "FireBreath", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FlameBreath } } },
            { "FireResistance", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireResistance } } },
            { "FireHammer", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireHammer } } },
            { "FireSpear", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.FireSpear } } },
            { "SwordUnlock", new ApItemDefinition { UpgradesToUnlock = { UpgradeType.SwordUnlock } } },
        };

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            try
            {
                Harmony harmony = new Harmony("com.tonnom.clonedrone.ap");
                harmony.PatchAll();

                try
                {
                    foreach (System.Type t in new[] { typeof(EnemyConfiguration), typeof(EnemyCount) })
                    {
                        StringBuilder f = new StringBuilder($"[AP DISCOVER] fields of {t.Name}: ");
                        foreach (var fi in AccessTools.GetDeclaredFields(t)) f.Append(fi.Name).Append(", ");
                        Log.LogInfo(f.ToString());
                    }
                    foreach (System.Type t in new[] { typeof(Character), typeof(FirstPersonMover), typeof(UpgradeCollection) })
                    {
                        StringBuilder m = new StringBuilder($"[AP DISCOVER] 'armor' members of {t.Name}: ");
                        foreach (var mi in AccessTools.GetDeclaredMethods(t))
                            if (mi.Name.IndexOf("armor", System.StringComparison.OrdinalIgnoreCase) >= 0) m.Append("M:").Append(mi.Name).Append(", ");
                        foreach (var fi in AccessTools.GetDeclaredFields(t))
                            if (fi.Name.IndexOf("armor", System.StringComparison.OrdinalIgnoreCase) >= 0) m.Append("F:").Append(fi.Name).Append(", ");
                        Log.LogInfo(m.ToString());
                    }
                }
                catch (System.Exception ex) { Log.LogWarning("[AP DISCOVER] failed: " + ex.Message); }

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
            while (tcpServer != null)
            {
                try
                {
                    using (TcpClient client = tcpServer.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        // Le client Python ferme la connexion après l'envoi : on lit jusqu'à la fin
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string message = reader.ReadToEnd().Trim();

                            if (!string.IsNullOrEmpty(message))
                            {
                                foreach (string line in message.Split('\n'))
                                {
                                    string clean = line.Trim();
                                    if (!string.IsNullOrEmpty(clean))
                                    {
                                        PendingItems.Enqueue(clean);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // Une erreur sur une connexion ne doit pas tuer le listener
                    Log.LogWarning("[AP TCP] Listener error: " + ex.Message);
                    Thread.Sleep(200);
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
                    continue;
                }

                if (message.StartsWith("SESSION:"))
                {
                    // SESSION:slot|room|goal|scene
                    string data = message.Substring(8);
                    string[] parts = data.Split('|');
                    if (parts.Length >= 3)
                    {
                        CurrentSlotName = parts[0].Trim();
                        CurrentRoomName = parts[1].Trim();
                        CurrentGoal = parts[2].Trim().ToLower();
                        Scene = parts.Length >= 4 ? ParseScene(parts[3]) : new List<int>();
                        NeedsNewRun = false;

                        Log.LogInfo($"[AP SESSION] Slot : {CurrentSlotName} | Room : {CurrentRoomName} | Goal : {CurrentGoal.ToUpper()} | Scene : {SceneToString()}");
                        RecalculateFreeUpgrades();
                    }
                    continue;
                }

                if (message.StartsWith("SCENE:"))
                {
                    // Resynchronisation envoyée par le client (fait foi)
                    Scene = ParseScene(message.Substring(6));
                    Log.LogInfo($"[AP SCENE] Scene : {SceneToString()}");
                    UpdateHudDisplay();
                    continue;
                }

                ap_grant_item(message);
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

        private static string BuildHudText()
        {
            StringBuilder sb = new StringBuilder();

            if (NextUpgradesFree > 0)
            {
                sb.AppendLine($"Free Upgrade : {NextUpgradesFree}");
            }

            if (Scene.Count > 0)
            {
                if (SceneComplete)
                {
                    sb.AppendLine("All Steps complete!");
                }
                else
                {
                    sb.AppendLine($"Step {FinishedRedos + 1}/{Scene.Count} - {ScenarioName(CurrentScenario)}");
                    if (NeedsNewRun)
                    {
                        sb.AppendLine("Run finished: start a NEW game");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static void UpdateHudDisplay()
        {
            string content = BuildHudText();

            if (string.IsNullOrEmpty(content))
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
                    hudText.alignment = TextAnchor.UpperRight;
                    hudText.horizontalOverflow = HorizontalWrapMode.Overflow;

                    RectTransform rect = hudText.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(1, 1);
                    rect.anchorMax = new Vector2(1, 1);
                    rect.pivot = new Vector2(1, 1);
                    rect.anchoredPosition = new Vector2(-20, -20);
                    rect.sizeDelta = new Vector2(450, 110);
                }
            }

            if (hudText != null)
            {
                hudText.text = content;
            }
        }

        // Envoie au client le NIVEAU gagné (ex: "7"). Le client décide de la location.
        public static void SendCheckToPython(int level)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(level.ToString());
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

    [HarmonyPatch(typeof(FirstPersonMover), "initializeCharacterModelAndUpgrades")]
    public class EnemyAlwaysRideRaptorPatch
    {
        private static bool _isSpawningMount = false;

        static void Postfix(FirstPersonMover __instance)
        {
            if (Plugin.CurrentScenario != Plugin.ScenarioRaptor) return;
            if (_isSpawningMount || __instance == null || !__instance.IsAlive())
                return;

            if (__instance.IsMainPlayer() || __instance.IsPlayerTeam || __instance.IsNeutral)
                return;

            if (!__instance.CanRideOthers())
                return;

            if (__instance.IsRidingOtherCharacter() || __instance.HasCharacterRiding())
                return;

            __instance.StartCoroutine(SpawnAndMountRaptorRoutine(__instance));
        }

        private static IEnumerator SpawnAndMountRaptorRoutine(FirstPersonMover rider)
        {
            // Attend 1 frame que le cavalier soit bien placé
            
            yield return null;

            if (rider == null || !rider.IsAlive() || rider.IsRidingOtherCharacter() || Singleton<EnemyFactory>.Instance == null)
                yield break;

            Transform mountTransform = null;
            _isSpawningMount = true;
            try
            {
                float randomRoll = UnityEngine.Random.Range(0f, 100f);
                if(randomRoll > 5)
                mountTransform = Singleton<EnemyFactory>.Instance.SpawnEnemyWithRotation(
                    EnemyType.FireRaptor,
                    rider.transform.position,
                    rider.transform.eulerAngles
                );
                else mountTransform = Singleton<EnemyFactory>.Instance.SpawnEnemyWithRotation(
                    EnemyType.LaserRaptor,
                    rider.transform.position,
                    rider.transform.eulerAngles
                );
            }
            finally
            {
                _isSpawningMount = false;
            }

            if (mountTransform == null) yield break;

            FirstPersonMover mount = mountTransform.GetComponent<FirstPersonMover>();
            if (mount == null) yield break;

            // Copie la section d'arène
            SectionMember riderSection = rider.GetComponent<SectionMember>();
            SectionMember mountSection = mount.GetComponent<SectionMember>();
            if (riderSection != null && mountSection != null)
            {
                mountSection.Sections = riderSection.Sections;
            }

            // Bloque l'IA du Raptor pendant qu'il s'initialise si le cavalier dort encore
            if (!rider.IsAIEnabled())
            {
                mount.DisableAI();
            }

            // Attend que le Raptor ait initialisé son CharacterModel (RiderContainer)
            int timeout = 0;
            while (!mount.IsInitialized() && timeout < 30)
            {
                if (!rider.IsAIEnabled()) mount.DisableAI();
                timeout++;
                yield return null;
            }

            if (rider != null && rider.IsAlive() && mount != null && mount.IsAlive())
            {
                rider.StartRiding(mount);

                // Synchronise immédiatement l'IA après le montage
                if (rider.IsAIEnabled())
                    mount.EnableAI();
                else
                    mount.DisableAI();
            }
        }
    }

    // Synchronise automatiquement l'IA de la monture sur celle de son cavalier à chaque frame
    [HarmonyPatch(typeof(FirstPersonMover), "Update")]
    public class SyncMountAIWithRiderPatch
    {
        static void Postfix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsAlive()) return;

            // Si ce personnage monte une monture (et que ce n'est pas un joueur)
            if (__instance.IsRidingOtherCharacter() && !__instance.IsPlayer())
            {
                FirstPersonMover mount = __instance.GetCharacterWeAreRiding();
                if (mount != null && mount.IsAlive())
                {
                    bool riderAI = __instance.IsAIEnabled();
                    bool mountAI = mount.IsAIEnabled();

                    // Si le cavalier a son IA activée mais que le Raptor dort, on réveille le Raptor !
                    if (riderAI && !mountAI)
                    {
                        mount.EnableAI();
                    }
                    // Si le cavalier a son IA coupée (pendant l'intro de l'ascenseur) mais que le Raptor bouge, on coupe le Raptor !
                    else if (!riderAI && mountAI)
                    {
                        mount.DisableAI();
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), "MarkCurrentLevelAsWon")]
    public class LevelWonPatch
    {
        static void Postfix()
        {
            if (LevelManager.Instance == null) return;

            int totalLevelsWon = LevelManager.Instance.GetNumberOfLevelsWon();
            if (totalLevelsWon < 1) return;

            Plugin.LocalChecks[totalLevelsWon] = true;

            int goalLevel = Plugin.GetGoalLevel();
            if (goalLevel > 0)
            {
                if (totalLevelsWon > goalLevel)
                {
                    Plugin.NeedsNewRun = true;
                    Plugin.ScheduleRedoReset();
                }
                else if (totalLevelsWon == goalLevel)
                {
                    if (Plugin.Scene.Count > 0 && !Plugin.SceneComplete)
                    {
                        Plugin.MarkCurrentRedoDone();
                        Plugin.Log.LogInfo($"[AP] Redo finished. Scene : {Plugin.SceneToString()}");
                    }
                    Plugin.NeedsNewRun = true;
                    Plugin.ScheduleRedoReset();
                }
                else
                {
                    Plugin.NeedsNewRun = false;
                }
            }

            Plugin.SendCheckToPython(totalLevelsWon);
            Plugin.UpdateHudDisplay();
        }
    }

    [HarmonyPatch(typeof(SteamInitFailedDialog), "Show")]
    public class BlockSteamInitFailedDialogPatch
    {
        static bool Prefix()
        {
            return false; // l'original ne s'exécute pas, la fenêtre n'apparaît jamais
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

    [HarmonyPatch(typeof(UpgradeManager), "IsUpgradeLockedByChallengeCompletion")]
    public class UnlockChallengeUpgradesPatch
    {
        static bool Prefix(ref bool __result)
        {
            __result = false; // N'est jamais verrouillé par un challenge
            return false;     // Ignore la méthode originale
        }
    }

    // Scénario INFERNO : les ennemis spawnent avec des armes de feu
    [HarmonyPatch(typeof(FirstPersonMover), "RefreshUpgrades")]
    public class EnemyFireVariantPatch
    {
        static void Prefix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsAlive() || __instance.IsMainPlayer() || __instance.IsPlayerTeam)
                return;

            if (!Plugin.IsInfernoActive) return;

            UpgradeCollection upgrades = AccessTools.Field(typeof(FirstPersonMover), "_upgradeCollection")?.GetValue(__instance) as UpgradeCollection;

            if (upgrades != null)
            {
                upgrades.AddUpgradeIfMissing(UpgradeType.FireSword, 1);
                upgrades.AddUpgradeIfMissing(UpgradeType.FireHammer, 1);
                upgrades.AddUpgradeIfMissing(UpgradeType.FireSpear, 1);
                upgrades.AddUpgradeIfMissing(UpgradeType.FireArrow, 1);
                upgrades.AddUpgradeIfMissing(UpgradeType.FireClaws, 1);
            }
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "IsUpgradeLockedByLifetimeMetagameProgress")]
    public class UnlockLifetimeMetagameUpgradesPatch
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "IsUpgradeLockedByCurrentMetagameProgress")]
    public class UnlockCurrentMetagameUpgradesPatch
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "TryUpgradeAbility")]
    public class TryUpgradePatch
    {
        static void Prefix(out bool __state)
        {
            __state = Plugin.NextUpgradesFree > 0;
        }
        static void Postfix(bool __state, bool __result)
        {
            if (__result && __state)
            {
                int currentUsed = Plugin.getNextFreeUsed();
                Plugin.SetUsedNextFreeCount(currentUsed + 1);
                Plugin.RecalculateFreeUpgrades();
            }
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

    [HarmonyPatch(typeof(Character), "onDeath")]
    public class CharacterDeathTrackerPatch
    {
        static void Postfix(Character __instance, Character killer, DamageSourceType damageSourceType, int attackID)
        {
            if (__instance != null)
            {
                // Vérifie si c'est un ennemi (pas dans l'équipe du joueur et pas neutre/allié)
                if (!__instance.IsPlayerTeam && !__instance.IsNeutral)
                {
                    string killerName = (killer != null) ? killer.CharacterType.ToString() : "Inconnu";
                    Plugin.Log.LogInfo($"[ARCHIPELAGO/ENNEMI TUÉ] Type: {__instance.CharacterType} | Tué par: {killerName} | Arme/Source: {damageSourceType}");
                }
            }
        }
    }


    [HarmonyPatch(typeof(EnemyFactory), "SpawnEnemyWithRotation", new System.Type[] { typeof(Transform), typeof(Vector3), typeof(Vector3), typeof(CharacterModel) })]
    public class HijackAllEnemiesToSprinterPatch
    {
        static void Prefix(EnemyFactory __instance, ref Transform enemyPrefab)
        {
            if (Plugin.CurrentScenario != Plugin.ScenarioSprinter && Plugin.CurrentScenario != Plugin.ScenarioRandom) return;
            if (enemyPrefab == null) return;

            Character charComp = enemyPrefab.GetComponent<Character>();
            if (charComp == null) return;

            if(charComp.IsPlayerTeam || charComp.IsNeutral) return;
            if (LevelManager.Instance == null) return;

            EnemyType targetType = EnemyType.ArmorBot;
            int endLevel = LevelManager.Instance.GetNumberOfLevelsWon();

            if (Plugin.CurrentScenario == Plugin.ScenarioSprinter)
            {
                

                float chanceSprinter2 = Mathf.Floor(endLevel / 3f) * 1f;

                // +2% pour SprinterBowman tous les 5 niveaux
                float chanceSprinterBowman = Mathf.Floor(endLevel / 5f) * 2f;

                // Le reste est attribué à Sprinter1 (total = 100%)
                float chanceSprinter1 = Mathf.Max(0f, 100f - (chanceSprinter2 + chanceSprinterBowman));

                // Tirage aléatoire entre 0 et 100
                float randomRoll = UnityEngine.Random.Range(0f, 100f);

                targetType = EnemyType.Sprinter1;

                // Détermination du type d'ennemi selon les seuils cumulés
                if (randomRoll < chanceSprinterBowman)
                {
                    targetType = EnemyType.SprinterBowman;
                }
                else if (randomRoll < chanceSprinterBowman + chanceSprinter2)
                {
                    targetType = EnemyType.Sprinter2;
                }
                else
                {
                    targetType = EnemyType.Sprinter1;
                }
            }

            else if (Plugin.CurrentScenario == Plugin.ScenarioRandom)
            {
                List<EnemyType> listedAs = new List<EnemyType>{EnemyType.Bowman1, EnemyType.Bowman2, EnemyType.Bowman3, EnemyType.Bowman4,
                    EnemyType.Swordsman2, EnemyType.Swordsman3, EnemyType.Swordsman4, EnemyType.Swordsman5, EnemyType.Swordsman1, EnemyType.FireRaptor,
                    EnemyType.FleetAnalysisBot1,EnemyType.FleetAnalysisBot2,EnemyType.FleetAnalysisBot3,EnemyType.FleetAnalysisBot4, EnemyType.Hammer1
                , EnemyType.Hammer3, EnemyType.Hammer2, EnemyType.Spear1, EnemyType.Spear2, EnemyType.Spear3, EnemyType.Spear4, EnemyType.Spidertron5000
                , EnemyType.Spidertron6000, EnemyType.Sprinter1, EnemyType.Sprinter2, EnemyType.FleetCaptain, EnemyType.FleetOverseer};

                if(endLevel > 13)
                {
                    listedAs.AddRange(new[] {
                        EnemyType.Bowman5,
                        EnemyType.LaserRaptor,
                        EnemyType.JetpackHammer,
                        EnemyType.Spear5
                    });
                }

                int randomRoll = UnityEngine.Random.Range(0, listedAs.Count);

                targetType = listedAs[randomRoll];
            }

            

            if (charComp.CharacterType != targetType)
            {
                Character newPrefabChar = __instance.GetEnemyPrefab(targetType);
                if (newPrefabChar != null)
                {
                    enemyPrefab = newPrefabChar.transform;
                }
            }
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

    [HarmonyPatch(typeof(FirstPersonMover), "RefreshUpgrades")]
    public class EnemyArmorPatch
    {
        static void Prefix(FirstPersonMover __instance)
        {
            if (!Plugin.ScenarioActive(Plugin.ScenarioArmor)) return;
            if (__instance == null || !__instance.IsAlive()) return;
            if (__instance.IsMainPlayer() || __instance.IsPlayerTeam || __instance.IsNeutral) return;

            UpgradeCollection upgrades = AccessTools.Field(typeof(FirstPersonMover), "_upgradeCollection")?.GetValue(__instance) as UpgradeCollection;
            if (upgrades == null) return;

            upgrades.AddUpgradeIfMissing(UpgradeType.Armor, 1);
        }
    }

}