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
        public static byte TotalFreeUpgradesReceived = 0;
        public static byte NextUpgradesFree = 0;
        public static byte ShopPriceReduction = 0;

        public static Dictionary<int, bool> LocalChecks = new Dictionary<int, bool>();
        public static List<UpgradeType> UnlockedUpgrades = new List<UpgradeType>();

        public static Dictionary<int, List<UpgradeType>> StepUnlockedUpgrades = new Dictionary<int, List<UpgradeType>>();
        public static byte HammerLevelsGlobal = 0;
        public static Dictionary<byte, byte> StepHammerLevels = new Dictionary<byte, byte>();

        public static ConcurrentQueue<string> PendingItems = new ConcurrentQueue<string>();
        private static TcpListener tcpServer;
        private static Thread listenerThread;
        public static string CurrentGoal = "uranium";
        public static string CurrentSlotName = "DefaultSlot";
        public static string CurrentRoomName = "DefaultRoom";
        public static bool IsDyingFromDeathLink = false;

        public enum Scenario : sbyte
        {
            End = -1,
            Normal = 0, Inferno, Sprinter, Raptor, Random, Armor, LevelUp
        }

        public enum Weapons : byte
        {
            WeaponSword, WeaponBow, WeaponHammer, WeaponSpear
        }

        public static List<Scenario> Scene = new List<Scenario>();          // X : scénario par step (-1 = step terminé)
        public static List<Weapons> SceneWeapons = new List<Weapons>();   // Y : arme de départ par step

        public static bool NeedsNewRun = false;
        public static Plugin Instance;
        private static bool _resetScheduled = false;

        public static bool ScenarioActive(int scenario)
        {
            if (CurrentScenario != (Scenario) scenario) return false;
            GameFlowManager flow = Singleton<GameFlowManager>.Instance;
            return flow != null && !flow.IsOnTitleScreen();
        }

        public static void ScheduleStepReset()
        {
            if (_resetScheduled || Instance == null) return;
            if (Scene.Count == 0 || SceneComplete) return;
            _resetScheduled = true;
            Instance.StartCoroutine(StepResetRoutine());
        }

        private static IEnumerator StepResetRoutine()
        {
            yield return new WaitForSeconds(4f);
            try
            {
                GameFlowManager flow = Singleton<GameFlowManager>.Instance;
                if (flow != null && !flow.IsOnTitleScreen() && !SceneComplete)
                {
                    flow.ResetGameAndSpawnHuman(resetIsFromLoss: false);
                    NeedsNewRun = false;
                }
            }
            catch (System.Exception ex)
            {
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

        // Format : "X:Y;X:Y;..." (l'ancien format "X;X;..." reste accepté, Y vaut alors 0)
        public static void LoadScene(string s)
        {
            List<Scenario> scenarios = new List<Scenario>();
            List<Weapons> weapons = new List<Weapons>();
            if (!string.IsNullOrEmpty(s))
            {
                foreach (string part in s.Split(';'))
                {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    string[] xy = p.Split(':');
                    int x;
                    int y = 0;
                    if (!int.TryParse(xy[0].Trim(), out x)) continue;
                    if (xy.Length > 1) int.TryParse(xy[1].Trim(), out y);
                    scenarios.Add((Scenario) x);
                    weapons.Add((Weapons) y);
                }
            }
            Scene = scenarios;
            SceneWeapons = weapons;
            RefreshUpgradeShop();
        }

        public static string SceneToString()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Scene.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(Scene[i]).Append(':').Append(i < SceneWeapons.Count ? SceneWeapons[i] : 0);
            }
            return sb.ToString();
        }

        public static int FinishedSteps
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
            get { return Scene.Count > 0 && FinishedSteps >= Scene.Count; }
        }

        public static Scenario CurrentScenario
        {
            get
            {
                foreach (int v in Scene) if (v != -1) return (Scenario) v;
                return Scenario.End;
            }
        }

        public static bool IsInfernoActive
        {
            get { return CurrentScenario == (Scenario) 1; }
        }

        public static int CurrentStepIndex
        {
            get
            {
                for (int i = 0; i < Scene.Count; i++) if (Scene[i] != Scenario.End) return i;
                return -1;
            }
        }

        public static Weapons CurrentWeapon
        {
            get
            {
                int i = CurrentStepIndex;
                return (i >= 0 && i < SceneWeapons.Count) ? (Weapons) SceneWeapons[i] : (Weapons) Weapons.WeaponSword;
            }
        }

        public static bool HasStartingWeaponOverride
        {
            get { return Scene.Count > 0 && !SceneComplete && CurrentWeapon != (int)Weapons.WeaponSword; }
        }

        public static byte CurrentStepNumber
        {
            get
            {
                if (Scene.Count == 0) return 0;
                return SceneComplete ? (byte) Scene.Count : (byte) (FinishedSteps + 1);
            }
        }

        public static bool IsUpgradeUnlocked(UpgradeType type)
        {
            if (UnlockedUpgrades.Contains(type)) return true;
            List<UpgradeType> stepList;
            return StepUnlockedUpgrades.TryGetValue(CurrentStepNumber, out stepList) && stepList.Contains(type);
        }

        public static int HammerLevelsAvailable
        {
            get
            {
                byte stepLevels;
                StepHammerLevels.TryGetValue((byte) CurrentStepNumber, out stepLevels);
                return HammerLevelsGlobal + stepLevels;
            }
        }

        public static bool IsUpgradeUnlocked(UpgradeType type, int level)
        {
            if (type == UpgradeType.Hammer) return level <= HammerLevelsAvailable;
            return IsUpgradeUnlocked(type);
        }

        public static string SplitStepSuffix(string itemName, out byte step)
        {
            step = 0;
            int idx = itemName.LastIndexOf(" (Step ");
            if (idx < 0 || !itemName.EndsWith(")")) return itemName;
            string num = itemName.Substring(idx + 7, itemName.Length - idx - 8);
            byte parsed;
            if (!byte.TryParse(num, out parsed)) return itemName;
            step = parsed;
            return itemName.Substring(0, idx);
        }

        public static void RefreshUpgradeShop()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.RefreshUpgradeStationsVisibility();
            }
        }

        public static bool SessionActive = false;
        public static Selectable MultiplayerButtonRef = null;

        private static volatile sbyte _connResult = 0;


        public static string ConnectionLabel
        {
            get { return (SessionActive ? "Connected" : "Not Connected"); }
        }

        public static void SendToClient(string message)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(message);
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch
                {
                    _connResult = -1;
                }
            });
        }

        public static void FindConnection(System.Action<bool> done)
        {
            if (Instance == null)
            {
                if (done != null) done(false);
                return;
            }
            Instance.StartCoroutine(FindConnectionRoutine(done));
        }

        private static IEnumerator FindConnectionRoutine(System.Action<bool> done)
        {
            _connResult = 0;
            RefreshMultiplayerLabel();

            SendToClient(SessionActive ? "PING" : "SYNC");

            float waited = 0f;
            while (_connResult == 0 && waited < 4f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            bool ok = _connResult == 1;
            SessionActive = ok;
            RefreshMultiplayerLabel();
            Log.LogInfo("[AP] Connection search: " + (ok ? "connected" : "no session"));

            if (done != null) done(ok);
        }

        public static void RefreshMultiplayerLabel()
        {
            if (MultiplayerButtonRef == null) return;
            Text label = MultiplayerButtonRef.GetComponentInChildren<Text>(true);
            if (label != null) label.text = ConnectionLabel;
        }


        private static bool UpgradeMatches(object element, UpgradeType type)
        {
            if (element == null) return false;
            if (element is UpgradeType) return (UpgradeType)element == type;

            System.Type t = element.GetType();
            System.Reflection.FieldInfo f = t.GetField("UpgradeType");
            if (f != null && f.FieldType == typeof(UpgradeType)) return (UpgradeType)f.GetValue(element) == type;
            System.Reflection.PropertyInfo p = t.GetProperty("UpgradeType");
            if (p != null && p.PropertyType == typeof(UpgradeType)) return (UpgradeType)p.GetValue(element, null) == type;
            return false;
        }

        public static bool TryRemoveUpgrade(object collection, UpgradeType type)
        {
            if (collection == null) return false;

            System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            // Voie 1 : une méthode de suppression
            foreach (string name in new[] { "RemoveUpgrade", "RemoveUpgradeIfPresent", "ClearUpgrade", "RemoveUpgradeType" })
            {
                foreach (System.Reflection.MethodInfo m in collection.GetType().GetMethods(flags))
                {
                    if (m.Name != name) continue;
                    System.Reflection.ParameterInfo[] ps = m.GetParameters();
                    try
                    {
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(UpgradeType))
                        {
                            m.Invoke(collection, new object[] { type });
                            return true;
                        }
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(UpgradeType) && ps[1].ParameterType == typeof(int))
                        {
                            m.Invoke(collection, new object[] { type, 0 });
                            return true;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogWarning("[AP] " + name + " failed: " + ex.Message);
                    }
                }
            }

            bool removed = false;
            for (System.Type t = collection.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (System.Reflection.FieldInfo f in t.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    object value;
                    try { value = f.GetValue(collection); }
                    catch { continue; }
                    if (value == null || value is string) continue;

                    System.Collections.IList list = value as System.Collections.IList;
                    if (list != null && !list.IsReadOnly && !list.IsFixedSize)
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (UpgradeMatches(list[i], type))
                            {
                                list.RemoveAt(i);
                                removed = true;
                            }
                        }
                    }

                    // HashSet<UpgradeType>, Dictionary<UpgradeType, ...> : Remove(UpgradeType)
                    System.Reflection.MethodInfo remove = value.GetType().GetMethod("Remove", new[] { typeof(UpgradeType) });
                    if (remove != null)
                    {
                        try
                        {
                            object result = remove.Invoke(value, new object[] { type });
                            if (result is bool && (bool)result) removed = true;
                        }
                        catch { }
                    }
                }
            }
            return removed;
        }

        public static void StripStartingSword(FirstPersonMover player, object collection)
        {
            if (player == null) return;

            long key = (long)player.GetInstanceID() * 10L + FinishedSteps;

            UpgradeType type = UpgradeType.SwordUnlock;

            bool removed = TryRemoveUpgrade(collection, type);

            if (GameDataManager.Instance != null)
            {
                try
                {
                    Dictionary<UpgradeType, int> playerUpgrades =
                        GameDataManager.Instance.GetPlayerUpgradeDictionary();

                    if (playerUpgrades != null && playerUpgrades.Remove(type))
                    {
                        GameDataManager.Instance.SetDirty();
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogWarning("[AP] GameData sword strip failed: " + ex.Message);
                }
            }

            // 2. Accède à la liste privée _equippedWeapons de CE joueur via Harmony AccessTools
            List<WeaponType> equippedWeapons = AccessTools.Field(typeof(FirstPersonMover), "_equippedWeapons")?.GetValue(player) as List<WeaponType>;
            if (equippedWeapons != null && equippedWeapons.Contains(WeaponType.Sword))
            {
                equippedWeapons.Remove(WeaponType.Sword);
                Log.LogInfo("[AP] WeaponType.Sword retiré de _equippedWeapons !");
            }

            // 3. Si le joueur tenait déjà l'épée dans ses mains, on change d'arme (ou on déséquipe)
            if (player.GetEquippedWeaponType() == WeaponType.Sword)
            {
                if (equippedWeapons != null && equippedWeapons.Count > 0)
                {
                    // Équipe la première autre arme disponible (ex: Hammer, Bow, Spear)
                    player.SetEquippedWeaponType(equippedWeapons[0], checkForWeaponAttackCooldown: false);
                }
                else
                {
                    // S'il n'a aucune autre arme, on lui vide les mains et on cache le modèle 3D
                    player.SetEquippedWeaponType(WeaponType.None, checkForWeaponAttackCooldown: false);
                    player.GetCharacterModel()?.SetAllWeaponModelsInactive();
                }
            }
        }

        public static UpgradeType WeaponToUpgrade(int weapon)
        {
            switch (weapon)
            {
                case 1: return UpgradeType.BowUnlock;
                case 2: return UpgradeType.Hammer;
                case 3: return UpgradeType.SpearUnlock;
                default: return UpgradeType.SwordUnlock;
            }
        }

        public static string WeaponName(int weapon)
        {
            switch (weapon)
            {
                case 1: return "Bow";
                case 2: return "Hammer";
                case 3: return "Spear";
                default: return "Sword";
            }
        }

        private static System.Reflection.MethodInfo FindMethod(System.Type t, string name)
        {
            foreach (System.Reflection.MethodInfo m in t.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic))
            {
                if (m.Name == name) return m;
            }
            return null;
        }

        public static bool TryEquipWeapon(FirstPersonMover player, int weapon)
        {
            string wanted = WeaponName(weapon);
            try
            {
                // Voie 1 : SetEquippedWeaponType(enum, ...)
                System.Reflection.MethodInfo setter = FindMethod(typeof(FirstPersonMover), "SetEquippedWeaponType");
                if (setter != null)
                {
                    System.Reflection.ParameterInfo[] ps = setter.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType.IsEnum)
                    {
                        object[] args = new object[ps.Length];
                        args[0] = System.Enum.Parse(ps[0].ParameterType, wanted, true);
                        for (int i = 1; i < ps.Length; i++)
                        {
                            args[i] = ps[i].HasDefaultValue
                                ? ps[i].DefaultValue
                                : (ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null);
                        }
                        setter.Invoke(player, args);
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.LogWarning("[AP] SetEquippedWeaponType failed: " + ex.Message);
            }

            try
            {
                // Voie 2 : on fait défiler les armes jusqu'à tomber sur la bonne
                System.Reflection.MethodInfo getter = FindMethod(typeof(FirstPersonMover), "GetEquippedWeaponType");
                if (getter != null)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        object current = getter.Invoke(player, null);
                        if (current != null && string.Equals(current.ToString(), wanted, System.StringComparison.OrdinalIgnoreCase))
                            return true;
                        player.GoToNextWeapon();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.LogWarning("[AP] Weapon cycling failed: " + ex.Message);
            }

            return false;
        }

        public static bool MarkCurrentStepDone()
        {
            for (int i = 0; i < Scene.Count; i++)
            {
                if (Scene[i] != Scenario.End)
                {
                    Scene[i] = Scenario.End;
                    RefreshUpgradeShop();
                    return true;
                }
            }
            return false;
        }

        public static string ScenarioName(int scenario)
        {
            switch (scenario)
            {
                case 0: return "None";
                case 1: return "Inferno";
                case 2: return "Jetpack";
                case 3: return "Raptor Rider";
                case 4: return "Enemy Randomized";
                case 5: return "Armored";
                default: return "Scenario " + scenario;
            }
        }


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

            if (Singleton<GameFlowManager>.Instance.IsOnTitleScreen()) return;

            FirstPersonMover player = Singleton<CharacterTracker>.Instance.GetPlayerRobot();

            if (player != null && player.IsAlive())
            {
                Log.LogInfo("[DEATHLINK] Un allié est mort ! Destruction du robot...");
                IsDyingFromDeathLink = true;
                try
                {
                    AccessTools.Field(typeof(Character), "_isAlive")?.SetValue(player, false);

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

        private static readonly Dictionary<string, UpgradeType[]> ApItemDatabase =
            new Dictionary<string, UpgradeType[]>
        {
            { "BlockArrows", new[] { UpgradeType.BlockArrows } },
            { "EnergyCapacity", new[] { UpgradeType.EnergyCapacity } },
            { "EnergyRecharge", new[] { UpgradeType.EnergyRecharge } },
            { "KickUnlock", new[] { UpgradeType.KickUnlock } },
            { "KickPower+GetUp", new[] { UpgradeType.KickPower, UpgradeType.GetUp } },
            { "BowUnlock", new[] { UpgradeType.BowUnlock } },
            { "ArrowWidth", new[] { UpgradeType.ArrowWidth } },
            { "AimTime", new[] { UpgradeType.AimTime } },
            { "FireArrow", new[] { UpgradeType.FireArrow } },
            { "Jetpack", new[] { UpgradeType.Jetpack } },
            { "Clone", new[] { UpgradeType.Clone } },
            { "Armor+Repair", new[] { UpgradeType.Armor, UpgradeType.Repair } },
            { "FireSword", new[] { UpgradeType.FireSword } },
            { "FireHammer+FireSpear", new[] { UpgradeType.FireHammer, UpgradeType.FireSpear } },
            { "SpearUnlock", new[] { UpgradeType.SpearUnlock } },
            { "SpearShield+ShieldBash", new[] { UpgradeType.ShieldBash, UpgradeType.ShieldSize } },
            { "FireBreath+FireResistance", new[] { UpgradeType.FlameBreath, UpgradeType.FireResistance } },
            { "Riding", new[] { UpgradeType.SummonFlameRaptor, UpgradeType.SummonSawbladeUnicycle } },
            { "Raptor Riding", new[] { UpgradeType.SummonFlameRaptor } },
            { "Unicycle Riding", new[] { UpgradeType.SummonSawbladeUnicycle } },
            { "SpearShield", new[] { UpgradeType.ShieldSize } },
            { "ShieldBash", new[] { UpgradeType.ShieldBash } },
            { "Armor", new[] { UpgradeType.Armor } },
            { "Repair", new[] { UpgradeType.Repair } },
            { "KickPower", new[] { UpgradeType.KickPower } },
            { "GetUp", new[] { UpgradeType.GetUp } },
            { "FireBreath", new[] { UpgradeType.FlameBreath } },
            { "FireResistance", new[] { UpgradeType.FireResistance } },
            { "FireHammer", new[] { UpgradeType.FireHammer } },
            { "FireSpear", new[] { UpgradeType.FireSpear } },
            { "SwordUnlock", new[] { UpgradeType.SwordUnlock } }
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
                    StringBuilder w = new StringBuilder("[AP DISCOVER] weapon members of FirstPersonMover: ");
                    foreach (System.Reflection.MethodInfo mi in typeof(FirstPersonMover).GetMethods(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic))
                    {
                        if (mi.Name.IndexOf("weapon", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        w.Append(mi.Name).Append('(');
                        foreach (System.Reflection.ParameterInfo p in mi.GetParameters()) w.Append(p.ParameterType.Name).Append(' ');
                        w.Append("), ");
                    }
                    Log.LogInfo(w.ToString());
                }
                catch (System.Exception ex) { Log.LogWarning("[AP DISCOVER] failed: " + ex.Message); }

                try
                {
                    System.Reflection.BindingFlags df =
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.DeclaredOnly;

                    foreach (System.Type t in new[] { typeof(UpgradeCollection), typeof(GameDataManager) })
                    {
                        bool all = t == typeof(UpgradeCollection);
                        StringBuilder u = new StringBuilder($"[AP DISCOVER] upgrade members of {t.Name}: ");
                        foreach (System.Reflection.MethodInfo mi in t.GetMethods(df))
                        {
                            if (!all && mi.Name.IndexOf("upgrade", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            u.Append("M:").Append(mi.Name).Append('(');
                            foreach (System.Reflection.ParameterInfo p in mi.GetParameters()) u.Append(p.ParameterType.Name).Append(' ');
                            u.Append("), ");
                        }
                        foreach (System.Reflection.FieldInfo fi in t.GetFields(df))
                        {
                            if (!all && fi.Name.IndexOf("upgrade", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            u.Append("F:").Append(fi.Name).Append(':').Append(fi.FieldType.Name).Append(", ");
                        }
                        Log.LogInfo(u.ToString());
                    }
                }
                catch (System.Exception ex) { Log.LogWarning("[AP DISCOVER] upgrades failed: " + ex.Message); }

                UnlockedUpgrades.Add(UpgradeType.BlockSwords);

                for (int i = 1; i <= 55; i++)
                {
                    LocalChecks[i] = false;
                }

                StartTcpServer();
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
                    Log.LogWarning("[AP TCP] Listener error: " + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        public static void RecalculateFreeUpgrades()
        {
            int usedCount = getNextFreeUsed();
            NextUpgradesFree = (byte) Mathf.Max(0, (int) TotalFreeUpgradesReceived - usedCount);
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
                    string data = message.Substring(8);
                    string[] parts = data.Split('|');
                    if (parts.Length >= 3)
                    {
                        CurrentSlotName = parts[0].Trim();
                        CurrentRoomName = parts[1].Trim();
                        CurrentGoal = parts[2].Trim().ToLower();
                        LoadScene(parts.Length >= 4 ? parts[3] : "");
                        StepUnlockedUpgrades.Clear();
                        HammerLevelsGlobal = 0;
                        StepHammerLevels.Clear();
                        ShopPriceReduction = 0;
                        TotalFreeUpgradesReceived = 0;
                        NeedsNewRun = false;
                        SessionActive = true;
                        _connResult = 1;
                        RefreshMultiplayerLabel();

                        Log.LogInfo($"[AP SESSION] Slot : {CurrentSlotName} | Room : {CurrentRoomName} | Goal : {CurrentGoal.ToUpper()} | Scene : {SceneToString()}");
                        RecalculateFreeUpgrades();
                    }
                    continue;
                }

                if (message.StartsWith("SCENE:"))
                {
                    LoadScene(message.Substring(6));
                    Log.LogInfo($"[AP SCENE] Scene : {SceneToString()}");
                    continue;
                }

                if (message == "PONG")
                {
                    SessionActive = true;
                    _connResult = 1;
                    RefreshMultiplayerLabel();
                    continue;
                }

                if (message == "NOSESSION")
                {
                    SessionActive = false;
                    _connResult = -1;
                    RefreshMultiplayerLabel();
                    continue;
                }

                ap_grant_item(message);
            }
        }

        public static void SendBuyToPython(string upgradeType, int level)
        {
            string message = "BUY:" + upgradeType + ":" + level;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(message);
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch
                {
                }
            });
        }

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
                    if (byte.TryParse(content, out byte value))
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

            byte step;
            string baseName = SplitStepSuffix(itemName, out step);

            if (baseName == "Progressive Hammer")
            {
                if (step > 0)
                {
                    byte owned;
                    StepHammerLevels.TryGetValue((byte) step, out owned);
                    StepHammerLevels[step] = (byte)(owned + 1);
                }
                else
                {
                    HammerLevelsGlobal++;
                }
                RefreshUpgradeShop();
                return;
            }

            UpgradeType[] itemUpgrades;
            if (ApItemDatabase.TryGetValue(baseName, out itemUpgrades))
            {

                List<UpgradeType> target = UnlockedUpgrades;
                if (step > 0)
                {
                    if (!StepUnlockedUpgrades.TryGetValue(step, out target))
                    {
                        target = new List<UpgradeType>();
                        StepUnlockedUpgrades[step] = target;
                    }
                }

                foreach (UpgradeType type in itemUpgrades)
                {
                    if (!target.Contains(type))
                    {
                        target.Add(type);
                    }
                }

                RefreshUpgradeShop();
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
            if (Plugin.CurrentScenario != (Plugin.Scenario) 3) return;
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

            yield return null;

            if (rider == null || !rider.IsAlive() || rider.IsRidingOtherCharacter() || Singleton<EnemyFactory>.Instance == null)
                yield break;

            Transform mountTransform = null;
            _isSpawningMount = true;
            try
            {
                float randomRoll = UnityEngine.Random.Range(0f, 100f);
                if (randomRoll > 5)
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

            SectionMember riderSection = rider.GetComponent<SectionMember>();
            SectionMember mountSection = mount.GetComponent<SectionMember>();
            if (riderSection != null && mountSection != null)
            {
                mountSection.Sections = riderSection.Sections;
            }

            if (!rider.IsAIEnabled())
            {
                mount.DisableAI();
            }

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

                if (rider.IsAIEnabled())
                    mount.EnableAI();
                else
                    mount.DisableAI();
            }
        }
    }

    [HarmonyPatch(typeof(FirstPersonMover), "Update")]
    public class SyncMountAIWithRiderPatch
    {
        static void Postfix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsAlive()) return;

            if (__instance.IsRidingOtherCharacter() && !__instance.IsPlayer())
            {
                FirstPersonMover mount = __instance.GetCharacterWeAreRiding();
                if (mount != null && mount.IsAlive())
                {
                    bool riderAI = __instance.IsAIEnabled();
                    bool mountAI = mount.IsAIEnabled();

                    if (riderAI && !mountAI)
                    {
                        mount.EnableAI();
                    }

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
                    Plugin.ScheduleStepReset();
                }
                else if (totalLevelsWon == goalLevel)
                {
                    if (Plugin.Scene.Count > 0 && !Plugin.SceneComplete)
                    {
                        Plugin.MarkCurrentStepDone();
                        Plugin.Log.LogInfo($"[AP] Step finished. Scene : {Plugin.SceneToString()}");
                    }
                    Plugin.NeedsNewRun = true;
                    Plugin.ScheduleStepReset();
                }
                else
                {
                    Plugin.NeedsNewRun = false;
                }
            }

            Plugin.SendCheckToPython(totalLevelsWon);
        }
    }

    [HarmonyPatch(typeof(SteamInitFailedDialog), "Show")]
    public class BlockSteamInitFailedDialogPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(UpgradeManager), "CanUpgrade")]
    public class CanUpgradePatch
    {
        static bool Prefix(UpgradeDescription upgradeDescription, ref bool __result)
        {
            if (Plugin.IsReceivingFromArchipelago) return true;

            bool isUnlocked = Plugin.IsUpgradeUnlocked(upgradeDescription.UpgradeType, upgradeDescription.Level);
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
            __result = false;
            return false;
        }
    }

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
        static void Postfix(UpgradeDescription upgradeDescription, bool __state, bool __result)
        {
            // Si l'achat de l'amélioration a réussi
            if (__result)
            {
                if (upgradeDescription != null)
                {
                    // Pour les upgrades répétables (comme Clone ou Armor), on récupère aussi le niveau réel du joueur
                    int realPlayerLevel = UpgradeManager.Instance != null
                        ? UpgradeManager.Instance.GetPlayerUpgradeLevel(upgradeDescription.UpgradeType)
                        : upgradeDescription.Level;

                    // Niveau acheté : celui de la station, ou le niveau réel du joueur pour une upgrade répétable
                    int level = upgradeDescription.IsRepeatable ? realPlayerLevel : upgradeDescription.Level;

                    Plugin.Log.LogInfo(
                        $"[UPGRADE ACHETÉE] " +
                        $"Type (Enum): UpgradeType.{upgradeDescription.UpgradeType} (ID: {(int)upgradeDescription.UpgradeType}) | " +
                        $"Level (Station): {upgradeDescription.Level} | Level (Joueur): {realPlayerLevel} | " +
                        $"Répétable: {upgradeDescription.IsRepeatable}"
                    );

                    Plugin.SendBuyToPython(upgradeDescription.UpgradeType.ToString(), level);
                }

                // Ta logique existante pour les Free Upgrades
                if (__state)
                {
                    int currentUsed = Plugin.getNextFreeUsed();
                    Plugin.SetUsedNextFreeCount(currentUsed + 1);
                    Plugin.RecalculateFreeUpgrades();
                }
            }
        }
    }

    // Le bouton Multiplayer est dégrisé : son clic lance une recherche de connexion (voir MultiplayerButtonSearchPatch)
    [HarmonyPatch(typeof(MultiplayerButtonController), "OnEnable")]
    public class EnableMultiplayerButtonPatch
    {
        static bool Prefix(MultiplayerButtonController __instance)
        {
            if (__instance.MultiplayerButton != null)
            {
                __instance.MultiplayerButton.interactable = true;
                Plugin.MultiplayerButtonRef = __instance.MultiplayerButton;
            }
            if (__instance.ErrorPopup != null) __instance.ErrorPopup.Hide();
            if (__instance.Sawblade != null) __instance.Sawblade.SetActive(false);

            Plugin.RefreshMultiplayerLabel();
            return false;   // l'original (qui grise le bouton hors ligne) ne s'exécute pas
        }
    }

    // Clic sur "Multiplayer" : cherche le client et sa session au lieu d'ouvrir le menu multijoueur
    [HarmonyPatch(typeof(TitleScreenUI), "OnMultiplayerButtonClicked")]
    public class MultiplayerButtonSearchPatch
    {
        static bool Prefix()
        {
            Plugin.FindConnection(null);
            return false;
        }
    }

    // Clic sur "Singleplayer" : refusé sans session Archipelago, sinon on va directement en Endless
    [HarmonyPatch(typeof(TitleScreenUI), "OnPlaySingleplayerButtonClicked")]
    public class SingleplayerStartsEndlessPatch
    {
        static bool Prefix(TitleScreenUI __instance)
        {
            LevelManager levelManager = Singleton<LevelManager>.Instance;
            if (levelManager != null && levelManager.IsSpawningCurrentLevel()) return false;

            Plugin.FindConnection(ok =>
            {
                if (ok)
                {
                    if (__instance != null) __instance.OnPlayEndlessButtonClicked();
                }
                else
                {
                    Plugin.Log.LogWarning("[AP] No Archipelago session: start refused");
                }
            });
            return false;
        }
    }

    // Textes du menu principal. Le premier argument est lu par position (__0) pour ne pas dépendre de son nom.
    [HarmonyPatch(typeof(LocalizationManager), "GetTranslatedString", new System.Type[] { typeof(string), typeof(int) })]
    public class RenameMultiplayerButtonPatch
    {
        private static bool Has(string id, string needle)
        {
            return id.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Postfix(string __0, ref string __result)
        {
            if (string.IsNullOrEmpty(__0)) return;

            string id = __0.Trim();

            if (Has(id, "play multiplayer"))
            {
                __result = Plugin.ConnectionLabel;
            }
            if (Has(id, "play singleplayer"))
            {
                __result = "Play Archipelago";
            }
            if (Has(id, "mint?"))
            {
                __result = "Archipelago?";
            }
            if (Has(id, "female_olympian"))
            {
                __result = "Archipelago!";
            }
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

        private static readonly EnemyType[] RandomEnemies =
            {
                EnemyType.Bowman1,
                EnemyType.Bowman2,
                EnemyType.Bowman3,
                EnemyType.Bowman4,
                EnemyType.Swordsman2,
                EnemyType.Swordsman3,
                EnemyType.Swordsman4,
                EnemyType.Swordsman5,
                EnemyType.Swordsman1,
                EnemyType.FireRaptor,
                EnemyType.FleetAnalysisBot1,
                EnemyType.FleetAnalysisBot2,
                EnemyType.FleetAnalysisBot3,
                EnemyType.FleetAnalysisBot4,
                EnemyType.Hammer1,
                EnemyType.Hammer3,
                EnemyType.Hammer2,
                EnemyType.Spear1,
                EnemyType.Spear2,
                EnemyType.Spear3,
                EnemyType.Spear4,
                EnemyType.Spidertron5000,
                EnemyType.Spidertron6000,
                EnemyType.Sprinter1,
                EnemyType.Sprinter2,
                EnemyType.FleetCaptain,
                EnemyType.FleetOverseer
            };

        private static readonly EnemyType[] RandomEnemiesLate =
        {
            EnemyType.Bowman1,
                EnemyType.Bowman2,
                EnemyType.Bowman3,
                EnemyType.Bowman4,
                EnemyType.Swordsman2,
                EnemyType.Swordsman3,
                EnemyType.Swordsman4,
                EnemyType.Swordsman5,
                EnemyType.Swordsman1,
                EnemyType.FireRaptor,
                EnemyType.FleetAnalysisBot1,
                EnemyType.FleetAnalysisBot2,
                EnemyType.FleetAnalysisBot3,
                EnemyType.FleetAnalysisBot4,
                EnemyType.Hammer1,
                EnemyType.Hammer3,
                EnemyType.Hammer2,
                EnemyType.Spear1,
                EnemyType.Spear2,
                EnemyType.Spear3,
                EnemyType.Spear4,
                EnemyType.Spidertron5000,
                EnemyType.Spidertron6000,
                EnemyType.Sprinter1,
                EnemyType.Sprinter2,
                EnemyType.FleetCaptain,
                EnemyType.FleetOverseer,
                EnemyType.Bowman5,
                EnemyType.LaserRaptor,
                EnemyType.JetpackHammer,
                EnemyType.Spear5
        };

        static void Prefix(EnemyFactory __instance, ref Transform enemyPrefab)
        {
            if (Plugin.CurrentScenario != (Plugin.Scenario) 2 && Plugin.CurrentScenario != (Plugin.Scenario) 4) return;
            if (enemyPrefab == null) return;

            Character charComp = enemyPrefab.GetComponent<Character>();
            if (charComp == null) return;

            if (charComp.IsPlayerTeam || charComp.IsNeutral) return;
            if (LevelManager.Instance == null) return;

            EnemyType targetType = EnemyType.ArmorBot;
            int endLevel = LevelManager.Instance.GetNumberOfLevelsWon();

            if (Plugin.CurrentScenario == (Plugin.Scenario) 2)
            {


                float chanceSprinter2 = Mathf.Floor(endLevel / 3f) * 1f;

                float chanceSprinterBowman = Mathf.Floor(endLevel / 5f) * 2f;

                float chanceSprinter1 = Mathf.Max(0f, 100f - (chanceSprinter2 + chanceSprinterBowman));

                float randomRoll = UnityEngine.Random.Range(0f, 100f);

                targetType = EnemyType.Sprinter1;

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

            else if (Plugin.CurrentScenario == (Plugin.Scenario) 4)
            {
                EnemyType[] enemies = endLevel > 13
                    ? RandomEnemiesLate
                    : RandomEnemies;

                int randomRoll = UnityEngine.Random.Range(0, enemies.Length);
                targetType = enemies[randomRoll];
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
            if (Plugin.IsDyingFromDeathLink) return;

            if (character != null && character.IsMainPlayer() && !character.IsNeutral)
            {
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
            if (!Plugin.ScenarioActive(5)) return;
            if (__instance == null || !__instance.IsAlive()) return;
            if (__instance.IsMainPlayer() || __instance.IsPlayerTeam || __instance.IsNeutral) return;

            UpgradeCollection upgrades = AccessTools.Field(typeof(FirstPersonMover), "_upgradeCollection")?.GetValue(__instance) as UpgradeCollection;
            if (upgrades == null) return;

            upgrades.AddUpgradeIfMissing(UpgradeType.Armor, 1);
        }
    }

    // Applique l'arme de départ du step en cours (Y de la Scene). Sans effet si Y = 0 (épée).
    [HarmonyPatch(typeof(FirstPersonMover), "RefreshUpgrades")]
    public class StartingWeaponOverridePatch
    {
        private static long _equippedKey = long.MinValue;

        static void Prefix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsMainPlayer()) return;
            if (!Plugin.HasStartingWeaponOverride) return;

            UpgradeCollection upgrades = AccessTools.Field(typeof(FirstPersonMover), "_upgradeCollection")?.GetValue(__instance) as UpgradeCollection;
            if (upgrades == null) return;

            // Le step démarre avec une autre arme : on retire l'épée de base (et son amélioration) du loadout de départ.
            // Seulement pendant la première seconde : une épée achetée ensuite (item SwordUnlock) reste.
            if (__instance.GetTimeSinceCreation() < 1f)
            {
                Plugin.StripStartingSword(__instance, upgrades);
            }

            upgrades.AddUpgradeIfMissing(Plugin.WeaponToUpgrade((int) Plugin.CurrentWeapon), 1);
        }

        static void Postfix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsMainPlayer()) return;
            if (!Plugin.HasStartingWeaponOverride) return;
            if (__instance.GetTimeSinceCreation() >= 1f) return;

            // Une seule tentative par joueur et par step (RefreshUpgrades est appelé plusieurs fois)
            long key = (long)__instance.GetInstanceID() * 10L + Plugin.FinishedSteps;
            if (key == _equippedKey) return;
            _equippedKey = key;

            byte weapon = (byte) Plugin.CurrentWeapon;
            bool ok = Plugin.TryEquipWeapon(__instance, weapon);
            Plugin.Log.LogInfo($"[AP] Starting weapon {Plugin.WeaponName(weapon)} equipped: {ok}");
        }
    }
}