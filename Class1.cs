using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace CloneDroneAP
{
    public enum UpgradesClass : byte
    {
        None = 0,
        BlockArrows_1 = 1,
        BlockArrows_2 = 2,
        BlockArrows_3 = 3,
        EnergyCapacity_1 = 4,
        EnergyCapacity_2 = 5,
        EnergyCapacity_3 = 6,
        EnergyRecharge_1 = 7,
        EnergyRecharge_2 = 8,
        KickUnlock = 9,
        KickPower = 10,
        GetUp = 11,
        BowUnlock = 12,
        ArrowWidth_1 = 13,
        ArrowWidth_2 = 14,
        AimTime_1 = 15,
        AimTime_2 = 16,
        FireArrow_1 = 17,
        FireArrow_2 = 18,
        Jetpack_1 = 19,
        Jetpack_2 = 20,
        Clone = 21,
        Armor = 22,
        Repair = 23,
        Hammer_1 = 24,
        Hammer_2 = 25,
        Hammer_3 = 26,
        FireSword_1 = 27,
        FireSword_2 = 28,
        FireHammer = 29,
        FireSpear = 30,
        SpearUnlock = 31,
        ShieldSize = 32,
        ShieldBash = 33,
        FlameBreath = 34,
        FireResistance = 35,
        SummonFlameRaptor = 36,
        SummonSawbladeUnicycle = 37,
        SwordUnlock = 38
    }

    public static class UpgradeLevels
    {

        public static readonly Dictionary<UpgradeType, UpgradesClass[]> ByType = new Dictionary<UpgradeType, UpgradesClass[]>
        {
            { UpgradeType.BlockArrows, new[] { UpgradesClass.BlockArrows_1, UpgradesClass.BlockArrows_2, UpgradesClass.BlockArrows_3 } },
            { UpgradeType.EnergyCapacity, new[] { UpgradesClass.EnergyCapacity_1, UpgradesClass.EnergyCapacity_2, UpgradesClass.EnergyCapacity_3 } },
            { UpgradeType.EnergyRecharge, new[] { UpgradesClass.EnergyRecharge_1, UpgradesClass.EnergyRecharge_2 } },
            { UpgradeType.KickUnlock, new[] { UpgradesClass.KickUnlock } },
            { UpgradeType.KickPower, new[] { UpgradesClass.KickPower } },
            { UpgradeType.GetUp, new[] { UpgradesClass.GetUp } },
            { UpgradeType.BowUnlock, new[] { UpgradesClass.BowUnlock } },
            { UpgradeType.ArrowWidth, new[] { UpgradesClass.ArrowWidth_1, UpgradesClass.ArrowWidth_2 } },
            { UpgradeType.AimTime, new[] { UpgradesClass.AimTime_1, UpgradesClass.AimTime_2 } },
            { UpgradeType.FireArrow, new[] { UpgradesClass.FireArrow_1, UpgradesClass.FireArrow_2 } },
            { UpgradeType.Jetpack, new[] { UpgradesClass.Jetpack_1, UpgradesClass.Jetpack_2 } },
            { UpgradeType.Clone, new[] { UpgradesClass.Clone } },
            { UpgradeType.Armor, new[] { UpgradesClass.Armor } },
            { UpgradeType.Repair, new[] { UpgradesClass.Repair } },
            { UpgradeType.Hammer, new[] { UpgradesClass.Hammer_1, UpgradesClass.Hammer_2, UpgradesClass.Hammer_3 } },
            { UpgradeType.FireSword, new[] { UpgradesClass.FireSword_1, UpgradesClass.FireSword_2 } },
            { UpgradeType.FireHammer, new[] { UpgradesClass.FireHammer } },
            { UpgradeType.FireSpear, new[] { UpgradesClass.FireSpear } },
            { UpgradeType.SpearUnlock, new[] { UpgradesClass.SpearUnlock } },
            { UpgradeType.ShieldSize, new[] { UpgradesClass.ShieldSize } },
            { UpgradeType.ShieldBash, new[] { UpgradesClass.ShieldBash } },
            { UpgradeType.FlameBreath, new[] { UpgradesClass.FlameBreath } },
            { UpgradeType.FireResistance, new[] { UpgradesClass.FireResistance } },
            { UpgradeType.SummonFlameRaptor, new[] { UpgradesClass.SummonFlameRaptor } },
            { UpgradeType.SummonSawbladeUnicycle, new[] { UpgradesClass.SummonSawbladeUnicycle } },
            { UpgradeType.SwordUnlock, new[] { UpgradesClass.SwordUnlock } }
        };
        private static readonly Dictionary<UpgradesClass, KeyValuePair<UpgradeType, int>> Reverse = BuildReverse();

        private static Dictionary<UpgradesClass, KeyValuePair<UpgradeType, int>> BuildReverse()
        {
            var map = new Dictionary<UpgradesClass, KeyValuePair<UpgradeType, int>>();
            foreach (KeyValuePair<UpgradeType, UpgradesClass[]> pair in ByType)
            {
                for (int i = 0; i < pair.Value.Length; i++)
                {
                    map[pair.Value[i]] = new KeyValuePair<UpgradeType, int>(pair.Key, i + 1);
                }
            }
            return map;
        }

        public static UpgradesClass ToClass(UpgradeType type, int level)
        {
            UpgradesClass[] levels;
            if (ByType.TryGetValue(type, out levels) && level >= 1 && level <= levels.Length)
                return levels[level - 1];
            return UpgradesClass.None;
        }

        public static bool TryGetLevel(UpgradesClass id, out UpgradeType type, out int level)
        {
            KeyValuePair<UpgradeType, int> found;
            if (Reverse.TryGetValue(id, out found))
            {
                type = found.Key;
                level = found.Value;
                return true;
            }
            type = default(UpgradeType);
            level = 0;
            return false;
        }

        public static string BuyKey(UpgradeType type, int level)
        {
            UpgradesClass id = ToClass(type, level);
            return id != UpgradesClass.None ? ((byte)id).ToString() : type.ToString();
        }
    }

    internal static class Refl
    {
        public static readonly FieldInfo UpgradeCollField = AccessTools.Field(typeof(FirstPersonMover), "_upgradeCollection");
        public static readonly FieldInfo EquippedWeaponsField = AccessTools.Field(typeof(FirstPersonMover), "_equippedWeapons");
        public static readonly FieldInfo IsAliveField = AccessTools.Field(typeof(Character), "_isAlive");
        public static readonly MethodInfo OnDeathMethod = AccessTools.Method(typeof(FirstPersonMover), "onDeath");

        public static UpgradeCollection GetUpgrades(FirstPersonMover mover)
        {
            return UpgradeCollField != null ? UpgradeCollField.GetValue(mover) as UpgradeCollection : null;
        }
    }

    [BepInPlugin("com.tonnom.clonedrone.ap", "Clone Drone Archipelago", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static bool RandomizeVoices = false;
        public static bool IsReceivingFromArchipelago = false;
        public static byte TotalFreeUpgradesReceived = 0;
        public static byte NextUpgradesFree = 0;
        public static byte NextFreeUsed = 0;
        public static byte TotalStartArmorReceived = 0;
        public static byte StartArmorUsed = 0;
        public static byte StartWithArmor_left = 0;
        public static Dictionary<byte, byte> StepShopDiscounts = new Dictionary<byte, byte>();
        public static byte ShopPriceReduction = 0;
        public static byte OverpricedShop = 0;
        public static HashSet<UpgradesClass> OverpricedShop_map = new HashSet<UpgradesClass> { };
        public static int MaxShopDiscount = 3;

        public static Dictionary<int, bool> LocalChecks = new Dictionary<int, bool>();
        public static List<UpgradeType> UnlockedUpgrades = new List<UpgradeType>();
        public static Dictionary<int, List<UpgradeType>> StepUnlockedUpgrades = new Dictionary<int, List<UpgradeType>>();
        public static byte HammerLevelsGlobal = 0;
        public static Dictionary<byte, byte> StepHammerLevels = new Dictionary<byte, byte>();
        public static ConcurrentQueue<string> PendingItems = new ConcurrentQueue<string>();
        private static TcpListener tcpServer;
        private static Thread listenerThread;
        private static volatile bool _running = false;
        private static readonly BlockingCollection<string> Outbox = new BlockingCollection<string>();
        private static Thread senderThread;
        public static string CurrentGoal = "uranium";
        public static int KeysMode = 0;
        public static int KeysReceived = 0;
        public static int LockedStep = 0;
        public static bool StepKeyRequired(byte step)
        {
            return KeysMode == 2 || (KeysMode == 1 && step < Scene.Count);
        }
        public static bool CanWinCurrentStep()
        {
            return !StepKeyRequired(CurrentStepNumber) || KeysReceived >= CurrentStepNumber;
        }
        public static void OnKeyReceived()
        {
            if (KeysMode == 0 || Scene.Count == 0 || SceneComplete) return;
            if (LevelManager.Instance == null) return;

            if (LockedStep != CurrentStepNumber) return;

            sbyte goal = GetGoalLevel();
            if (goal > 0 && LevelManager.Instance.GetNumberOfLevelsWon() >= goal && CanWinCurrentStep())
            {
                MarkCurrentStepDone();
                NeedsNewRun = true;
                ScheduleStepReset();
                SendCheckToPython(goal);
            }
        }
        public static string CurrentSlotName = "DefaultSlot";
        public static string CurrentRoomName = "DefaultRoom";
        public static bool IsDyingFromDeathLink = false;
        public enum Scenario : sbyte
        {
            End = -1,
            Normal = 0, Inferno, Sprinter, Raptor, Random, Armor, LevelUp, EasyMode, Undead, Doggos, Summoners
        }
        public enum Weapons : byte
        {
            WeaponSword, WeaponBow, WeaponHammer, WeaponSpear
        }
        public static List<Scenario> Scene = new List<Scenario>();
        public static Dictionary<Scenario, byte> Confusion = new Dictionary<Scenario, byte>();
        public static List<Weapons> SceneWeapons = new List<Weapons>();
        public static bool NeedsNewRun = false;
        public static Plugin Instance;
        private static bool _resetScheduled = false;
        private static int _finishedSteps = 0;
        private static int _currentStepIndex = -1;
        private static Scenario _currentScenario = Scenario.End;
        private static void RecomputeSceneCache()
        {
            sbyte finished = 0;
            sbyte index = -1;
            for (sbyte i = 0; i < Scene.Count; i++)
            {
                if (Scene[i] == Scenario.End) finished++;
                else if (index < 0) index = i;
            }
            _finishedSteps = finished;
            _currentStepIndex = index;
            _currentScenario = index >= 0 ? Scene[index] : Scenario.End;
        }

        public static bool ScenarioActive(byte scenario)
        {
            if (CurrentScenario != (Scenario)scenario) return false;
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
            yield return new WaitForSeconds(3f);
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

        public static bool isConfusionOk(Scenario scenario)
        {
            byte confusion;
            if (scenario == CurrentScenario) return false;
            if (!Confusion.TryGetValue(scenario, out confusion) || confusion == 0) return false;
            return UnityEngine.Random.Range(1, 101) <= confusion; 
        }

        public static sbyte GetGoalLevel()
        {
            switch (CurrentGoal)
            {
                case "titanium": return 15;
                case "uranium": return 23;
                case "insanium": return 33;
                default: return -1;
            }
        }

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
                    scenarios.Add((Scenario)x);
                    weapons.Add((Weapons)y);
                }
            }
            Scene = scenarios;
            SceneWeapons = weapons;
            RecomputeSceneCache();
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
            get { return _finishedSteps; }
        }

        public static bool SceneComplete
        {
            get { return Scene.Count > 0 && _finishedSteps >= Scene.Count; }
        }

        public static Scenario CurrentScenario
        {
            get { return _currentScenario; }
        }

        public static bool IsInfernoActive
        {
            get { return _currentScenario == Scenario.Inferno; }
        }

        public static int CurrentStepIndex
        {
            get { return _currentStepIndex; }
        }

        public static Weapons CurrentWeapon
        {
            get
            {
                int i = _currentStepIndex;
                return (i >= 0 && i < SceneWeapons.Count) ? SceneWeapons[i] : Weapons.WeaponSword;
            }
        }

        public static bool HasStartingWeaponOverride
        {
            get { return Scene.Count > 0 && !SceneComplete && CurrentWeapon != Weapons.WeaponSword; }
        }

        public static byte CurrentStepNumber
        {
            get
            {
                if (Scene.Count == 0) return 0;
                return SceneComplete ? (byte)Scene.Count : (byte)(_finishedSteps + 1);
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
                StepHammerLevels.TryGetValue((byte)CurrentStepNumber, out stepLevels);
                return HammerLevelsGlobal + stepLevels;
            }
        }

        public static int EffectiveShopDiscount
        {
            get
            {
                byte stepCount;
                StepShopDiscounts.TryGetValue(CurrentStepNumber, out stepCount);
                return Mathf.Min(MaxShopDiscount, ShopPriceReduction + stepCount);
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

        private static readonly MethodInfo _iconRefresh = AccessTools.Method(typeof(UpgradeUIIcon), "refreshDisplay");
        private static bool _shopDirty = false;

        public static void RefreshUpgradeShop()
        {
            _shopDirty = true;
        }

        private static void DoRefreshUpgradeShop()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.RefreshUpgradeStationsVisibility();
            }

            var events = Singleton<GlobalEventManager>.Instance;
            if (events != null)
            {
                events.Dispatch("AvailableSkillPointsChanged");
            }

            var root = Singleton<GameUIRoot>.Instance;
            if (root != null && root.UpgradeUI != null && root.UpgradeUI.IsVisible() && _iconRefresh != null)
            {
                foreach (UpgradeUIIcon icon in root.UpgradeUI.GetComponentsInChildren<UpgradeUIIcon>())
                {
                    _iconRefresh.Invoke(icon, null);
                }
            }
        }
        public class ApHud : MonoBehaviour
        {
            public static ApHud Instance;
            private Text _text;
            private bool _initialized = false;
            private string _last;
            private float _nextUpdate;
            private readonly StringBuilder _sb = new StringBuilder(128);

            public static void Create()
            {
                if (Instance != null) return;
                GameObject go = new GameObject("AP_HUD");
                UnityEngine.Object.DontDestroyOnLoad(go);
                Instance = go.AddComponent<ApHud>();
            }

            private void Start()
            {
                StartCoroutine(InitializeRoutine());
            }

            private IEnumerator InitializeRoutine()
            {
                yield return new WaitForSeconds(0.5f);
                Build();
                _initialized = true;
            }

            private void Build()
            {
                try
                {
                    Canvas canvas = gameObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 5000;

                    CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;

                    GameObject label = new GameObject("Label");
                    label.transform.SetParent(transform, false);
                    label.AddComponent<CanvasRenderer>();

                    _text = label.AddComponent<Text>();

                    Font targetFont = null;
                    Font[] allFonts = Resources.FindObjectsOfTypeAll<Font>();
                    if (allFonts != null && allFonts.Length > 0)
                    {
                        targetFont = allFonts[0];
                    }
                    else
                    {
                        targetFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }

                    _text.font = targetFont;
                    _text.fontSize = 22;
                    _text.alignment = TextAnchor.UpperLeft;
                    _text.color = Color.white;
                    _text.raycastTarget = false;

                    label.AddComponent<Outline>();

                    RectTransform rt = _text.rectTransform;
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(30f, -30f);
                    rt.sizeDelta = new Vector2(800f, 200f);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError("[AP HUD] Erreur durant le Build : " + ex.Message);
                }
            }

            private void Update()
            {
                if (!_initialized || _text == null) return;
                if (Time.unscaledTime < _nextUpdate) return;
                _nextUpdate = Time.unscaledTime + 0.25f;

                string s = BuildText();
                if (s != _last)
                {
                    _last = s;
                    _text.text = s;
                }
            }

            private string BuildText()
            {
                string levelId = "Menu";
                LevelManager lm = Singleton<LevelManager>.Instance;
                if (lm != null && lm.HasAnyLevelLoaded())
                {
                    levelId = lm.GetCurrentLevelID();
                    if (string.IsNullOrEmpty(levelId))
                    {
                        levelId = lm.GetLastSpawnedLevelID() ?? "Inconnu";
                    }
                }

                _sb.Length = 0;
                _sb.Append(levelId).Append("  Step ").Append(Plugin.CurrentStepNumber).Append("  Keys ").Append(Plugin.KeysReceived).Append('\n');

                GameFlowManager flow = Singleton<GameFlowManager>.Instance;
                bool inCombat = flow != null && flow.IsInCombat();

                if (Plugin.StartWithArmor_left > 0 && lm != null && lm.GetNumberOfLevelsWon() < 1)
                    _sb.Append("Starting armor: ").Append(Plugin.StartWithArmor_left).Append('\n');
                if (Plugin.NextUpgradesFree > 0 && !inCombat)
                    _sb.Append("Next upgrades free: ").Append(Plugin.NextUpgradesFree).Append('\n');
                if (Plugin.infinite_armor_seconds > 0 && inCombat)
                    _sb.Append("Infinite Armor (seconds): ").Append(Plugin.infinite_armor_seconds.ToString("0.##")).Append('\n');

                return _sb.ToString();
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
            if (!Outbox.IsAddingCompleted) Outbox.Add(message);
        }
        private static void SenderLoop()
        {
            foreach (string msg in Outbox.GetConsumingEnumerable())
            {
                try
                {
                    using (TcpClient client = new TcpClient("127.0.0.1", 38283))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(msg);
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch
                {
                    if (msg == "SYNC" || msg == "PING") _connResult = -1;
                }
            }
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

            if (done != null) done(ok);
        }

        public static void RefreshMultiplayerLabel()
        {
            if (MultiplayerButtonRef == null) return;
            Text label = MultiplayerButtonRef.GetComponentInChildren<Text>(true);
            if (label != null) label.text = ConnectionLabel;
        }

        private static readonly FieldInfo UpgradeLevelsField =
            AccessTools.Field(typeof(UpgradeCollection), "_upgradeLevels");

        public static bool TryRemoveUpgrade(object collection, UpgradeType type)
        {
            if (collection == null || UpgradeLevelsField == null)
                return false;

            try
            {
                var levels = UpgradeLevelsField.GetValue(collection)
                    as System.Collections.Generic.Dictionary<UpgradeType, int>;

                return levels != null && levels.Remove(type);
            }
            catch (System.Exception ex)
            {
                Log.LogWarning("[AP] TryRemoveUpgrade failed: " + ex.Message);
                return false;
            }
        }

        public static void StripStartingSword(FirstPersonMover player, object collection)
        {
            if (player == null) return;

            UpgradeType type = UpgradeType.SwordUnlock;

            TryRemoveUpgrade(collection, type);

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

            List<WeaponType> equippedWeapons = Refl.EquippedWeaponsField != null
                ? Refl.EquippedWeaponsField.GetValue(player) as List<WeaponType>
                : null;
            if (equippedWeapons != null && equippedWeapons.Contains(WeaponType.Sword))
            {
                equippedWeapons.Remove(WeaponType.Sword);
            }

            if (player.GetEquippedWeaponType() == WeaponType.Sword)
            {
                if (equippedWeapons != null && equippedWeapons.Count > 0)
                {
                    player.SetEquippedWeaponType(equippedWeapons[0], checkForWeaponAttackCooldown: false);
                }
                else
                {
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

        public static WeaponType ToWeaponType(int weapon)
        {
            switch (weapon)
            {
                case 1: return WeaponType.Bow;
                case 2: return WeaponType.Hammer;
                case 3: return WeaponType.Spear;
                default: return WeaponType.Sword;
            }
        }
        public static bool TryEquipWeapon(FirstPersonMover player, int weapon)
        {
            WeaponType wanted = ToWeaponType(weapon);
            try
            {
                player.SetEquippedWeaponType(wanted, checkForWeaponAttackCooldown: false);
                return player.GetEquippedWeaponType() == wanted;
            }
            catch (System.Exception ex)
            {
                Log.LogWarning("[AP] SetEquippedWeaponType failed: " + ex.Message);
                return false;
            }
        }

        public static bool MarkCurrentStepDone()
        {
            for (int i = 0; i < Scene.Count; i++)
            {
                if (Scene[i] != Scenario.End)
                {
                    Scene[i] = Scenario.End;
                    LockedStep = 0;
                    RecomputeSceneCache();
                    RefreshUpgradeShop();
                    return true;
                }
            }
            return false;
        }



        public static void KillPlayerFromDeathLink()
        {
            if (Singleton<CharacterTracker>.Instance == null || Singleton<GameFlowManager>.Instance == null) return;
            if (Singleton<GameFlowManager>.Instance.IsOnTitleScreen()) return;
            FirstPersonMover player = Singleton<CharacterTracker>.Instance.GetPlayerRobot();
            if (player != null && player.IsAlive())
            {
                IsDyingFromDeathLink = true;
                try
                {
                    if (Refl.IsAliveField != null) Refl.IsAliveField.SetValue(player, false);
                    if (Refl.OnDeathMethod != null)
                    {
                        Refl.OnDeathMethod.Invoke(player, new object[] { null, DamageSourceType.None, -1 });
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[DEATHLINK] Can't kill player : {ex.Message}");
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
            { "Clone (All Steps)", new[] { UpgradeType.Clone } },
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

                UnlockedUpgrades.Add(UpgradeType.BlockSwords);

                for (int i = 1; i <= 55; i++)
                {
                    LocalChecks[i] = false;
                }

                RecomputeSceneCache();

                senderThread = new Thread(SenderLoop) { IsBackground = true };
                senderThread.Start();

                ApHud.Create();
                StartTcpServer();
            }
            catch (System.Exception ex)
            {
                Log.LogError("Error in Awake : " + ex.ToString());
            }
        }

        private void OnDestroy()
        {
            _running = false;
            try { if (tcpServer != null) tcpServer.Stop(); } catch { }
            tcpServer = null;
            try { Outbox.CompleteAdding(); } catch { }
        }

        private void StartTcpServer()
        {
            try
            {
                tcpServer = new TcpListener(IPAddress.Loopback, 38282);
                tcpServer.Start();
                _running = true;
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
            while (_running && tcpServer != null)
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
                    if (!_running) break;
                    Log.LogWarning("[AP TCP] Listener error: " + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        public static void RecalculateFreeUpgrades()
        {
            NextUpgradesFree = (byte)Mathf.Max(0, (int)TotalFreeUpgradesReceived - NextFreeUsed);
        }

        public static void RecalculateStartArmor()
        {
            StartWithArmor_left = (byte)Mathf.Max(0, (int)TotalStartArmorReceived - StartArmorUsed);
        }

        public void ApplyArmorToSpecificPart(Character character, MechBodyPartType targetPart)
        {
            List<BaseBodyPart> allBaseBodyParts = character.GetAllBaseBodyParts();
            ArmorPiece[] armorPrefabsUsed = character.GetArmorPrefabsUsed();

            for (int i = 0; i < armorPrefabsUsed.Length; i++)
            {
                if (armorPrefabsUsed[i].ShieldingBodyPart != targetPart)
                {
                    continue;
                }

                for (int j = 0; j < allBaseBodyParts.Count; j++)
                {
                    if (allBaseBodyParts[j] != null && allBaseBodyParts[j].CanApplyArmorPiece(armorPrefabsUsed[i]))
                    {
                        ArmorPiece armorPiece = UnityEngine.Object.Instantiate(
                            armorPrefabsUsed[i],
                            allBaseBodyParts[j].transform.parent,
                            worldPositionStays: false
                        );
                        armorPiece.transform.localPosition = Vector3.zero;
                        armorPiece.Initialize(allBaseBodyParts[j], character);
                        allBaseBodyParts[j].SetArmor(armorPiece);
                        character.TrackArmorPiece(armorPiece);
                    }
                }
            }
            character.OnAttemptedToAddArmorPieces();
            if (character.IsMainPlayer())
            {
                var playerArmorList = Singleton<GameDataManager>.Instance.GetPlayerArmorList();
                if (!playerArmorList.Contains(targetPart))
                {
                    playerArmorList.Add(targetPart);
                    Singleton<GameDataManager>.Instance.SetDirty();
                }
            }
        }

        static List<MechBodyPartType> armor_before_infinite = new List<MechBodyPartType> { };
        public static float infinite_armor_seconds = 0;
        public static byte TotalInfiniteArmorReceived = 0;
        public static byte InfiniteArmorUsed = 0;
        public static byte InfiniteArmor_left = 0;
        public static void RecalculateInfiniteArmor()
        {
            InfiniteArmor_left = (byte)Mathf.Max(0, (int)TotalInfiniteArmorReceived - InfiniteArmorUsed);
        }

        public static void ConsumeInfiniteArmor(Character p)
        {
            if (infinite_armor_seconds <= 0f)
            {
                foreach (MechBodyPartType mbpt in p.GetBodyPartTypesCoveredByArmor())
                {
                    armor_before_infinite.Add(mbpt);
                }
            }
            infinite_armor_seconds += 60f;
            if (InfiniteArmorUsed < byte.MaxValue) InfiniteArmorUsed++;
            RecalculateInfiniteArmor();
            SendToClient("INFINITEARMOR_USED");
        }

        private void OnInfiniteArmorEnded(Character c)
        {
            ArmorManager.Instance.RemoveArmorFrom(c);
            foreach (MechBodyPartType mbpt in armor_before_infinite)
            {
                ApplyArmorToSpecificPart(c, mbpt);
            }
            armor_before_infinite.Clear();
        }

        Character player;

        private void Update()
        {
            if (player == null)
            {
                CharacterTracker tracker = Singleton<CharacterTracker>.Instance;
                if (tracker != null) player = tracker.GetPlayer();
            }

            GameFlowManager flow = Singleton<GameFlowManager>.Instance;
            bool inCombat = flow != null && flow.IsInCombat();

            if (InfiniteArmor_left != 0 && player != null && player.IsAlive() && inCombat)
            {
                ConsumeInfiniteArmor(player);
            }

            if (infinite_armor_seconds > 0f && player != null && player.IsAlive() && inCombat)
            {
                infinite_armor_seconds -= Time.deltaTime;
                if (ArmorManager.Instance != null)
                {
                    ArmorManager.Instance.ApplyArmorTo(player, forceGiveArmor: true);
                }

                if (infinite_armor_seconds <= 0f)
                {
                    infinite_armor_seconds = 0f;
                    if (InfiniteArmor_left != 0) ConsumeInfiniteArmor(player);
                    else OnInfiniteArmorEnded(player);
                }
            }

            while (PendingItems.TryDequeue(out string message))
            {
                if (message == "DEATHLINK")
                {
                    KillPlayerFromDeathLink();
                    continue;
                }

                if (message.StartsWith("SESSION:"))
                {
                    HandleSession(message.Substring(8));
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

                if (message.StartsWith("USED:"))
                {
                    string[] up = message.Split(':');
                    int n;
                    if (up.Length == 3 && int.TryParse(up[2], out n))
                    {
                        byte v = (byte)Mathf.Clamp(n, 0, 255);
                        if (up[1] == "NextFree")
                        {
                            NextFreeUsed = (byte)Mathf.Max((int)NextFreeUsed, (int)v);
                            RecalculateFreeUpgrades();
                        }
                        else if (up[1] == "StartArmor")
                        {
                            StartArmorUsed = (byte)Mathf.Max((int)StartArmorUsed, (int)v);
                            RecalculateStartArmor();
                        }
                        else if (up[1] == "60sInfiniteArmor")
                        {
                            InfiniteArmorUsed = (byte)Mathf.Max((int)InfiniteArmorUsed, (int)v);
                            RecalculateInfiniteArmor();
                        }
                        RefreshUpgradeShop();
                    }
                    continue;
                }

                ap_grant_item(message);
            }

            if (_shopDirty)
            {
                _shopDirty = false;
                DoRefreshUpgradeShop();
            }
        }

        private static void HandleSession(string data)
        {
            string[] parts = data.Split('|');
            if (parts.Length < 3) return;

            CurrentSlotName = parts[0].Trim();
            CurrentRoomName = parts[1].Trim();
            string[] goalParts = parts[2].Trim().ToLower().Split(':');
            CurrentGoal = goalParts[0];
            int sessionKeysMode;
            KeysMode = (goalParts.Length > 1 && int.TryParse(goalParts[1], out sessionKeysMode))
                ? Mathf.Clamp(sessionKeysMode, 0, 2)
                : 0;
            KeysReceived = 0;
            LockedStep = 0;
            int sessionPrice;
            MaxShopDiscount = (parts.Length >= 5 && int.TryParse(parts[4].Trim(), out sessionPrice))
                ? Mathf.Clamp(sessionPrice, 1, 5)
                : 3;
            byte sessionOverpriced;
            OverpricedShop = (parts.Length >= 6 && byte.TryParse(parts[5].Trim(), out sessionOverpriced))
                ? (byte)Mathf.Min((int)sessionOverpriced, 10)
                : (byte)0;
            OverpricedShop_map.Clear();
            Confusion.Clear();
            if (parts.Length >= 7)
            {
                string[] conf = parts[6].Trim().Split(',');
                for (int i = 0; i < conf.Length && i < 10; i++)
                {
                    byte c;
                    if (byte.TryParse(conf[i].Trim(), out c)) Confusion[(Scenario)(i + 1)] = (byte)Mathf.Min((int)c, 75);
                }
            }
            RandomizeVoices = (parts.Length >= 8 && int.TryParse(parts[7].Trim(), out int sessionVoices)) && sessionVoices != 0;
            RandomizeAllSpeechSequencesPatch.ApplyVoiceRandomization();
            LoadScene(parts.Length >= 4 ? parts[3] : "");
            StepUnlockedUpgrades.Clear();
            UnlockedUpgrades.Clear();
            HammerLevelsGlobal = 0;
            StepHammerLevels.Clear();
            ShopPriceReduction = 0;
            StepShopDiscounts.Clear();
            TotalFreeUpgradesReceived = 0;
            TotalStartArmorReceived = 0;
            NextFreeUsed = 0;
            StartArmorUsed = 0;
            NeedsNewRun = false;
            SessionActive = true;
            _connResult = 1;
            RefreshMultiplayerLabel();

            TotalInfiniteArmorReceived = 0;
            InfiniteArmorUsed = 0;
            infinite_armor_seconds = 0f;
            armor_before_infinite.Clear();
            RecalculateInfiniteArmor();
            RecalculateFreeUpgrades();
            RecalculateStartArmor();
        }

        public static void SendBuyToPython(string upgradeType, int level)
        {
            SendToClient("BUY:" + upgradeType + ":" + level);
        }

        public static void SendCheckToPython(int level)
        {
            SendToClient(level.ToString());
        }

        public static void ConsumeNextFree()
        {
            if (NextFreeUsed < byte.MaxValue) NextFreeUsed++;
            RecalculateFreeUpgrades();
            SendToClient("NEXTFREE_USED");
        }

        // Idem pour Start with Armor
        public static void ConsumeStartArmor()
        {
            if (StartArmorUsed < byte.MaxValue) StartArmorUsed++;
            RecalculateStartArmor();
            SendToClient("STARTARMOR_USED");
        }

        public static void ap_grant_item(string itemName)
        {
            Log.LogInfo($"[ARCHIPELAGO] Got : {itemName}");

            if (itemName.StartsWith("ShopDiscount"))
            {
                ShopPriceReduction++;
                RefreshUpgradeShop();
                return;
            }

            if (itemName.StartsWith("NextUpgradeFree") || itemName.Contains("FreeUpgrade"))
            {
                TotalFreeUpgradesReceived++;
                RecalculateFreeUpgrades();
                RefreshUpgradeShop();
                return;
            }

            if (itemName == "60sInfiniteArmor")
            {
                TotalInfiniteArmorReceived++;
                RecalculateInfiniteArmor();
                return;
            }

            if (itemName == "StartWithArmor")
            {
                TotalStartArmorReceived = (byte)Mathf.Min(255, TotalStartArmorReceived + 3);
                RecalculateStartArmor();
                return;
            }

            if (itemName == "Progressive Key")
            {
                KeysReceived++;
                OnKeyReceived();
                return;
            }

            byte step;
            string baseName = SplitStepSuffix(itemName, out step);

            if (baseName == "Shop Discount")
            {
                if (step > 0)
                {
                    byte ownedDiscounts;
                    StepShopDiscounts.TryGetValue(step, out ownedDiscounts);
                    StepShopDiscounts[step] = (byte)(ownedDiscounts + 1);
                }
                else
                {
                    ShopPriceReduction++;
                }
                RefreshUpgradeShop();
                return;
            }

            if (baseName == "Progressive Hammer")
            {
                if (step > 0)
                {
                    byte owned;
                    StepHammerLevels.TryGetValue((byte)step, out owned);
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
        public static bool isSpawningMount = false;

        static void Postfix(FirstPersonMover __instance)
        {
            if (isSpawningMount || __instance == null || !__instance.IsAlive())
                return;
            if (__instance.IsMainPlayer() || __instance.IsPlayerTeam || __instance.IsNeutral)
                return;
            if (!__instance.CanRideOthers())
                return;
            if (__instance.IsRidingOtherCharacter() || __instance.HasCharacterRiding())
                return;
            if (Plugin.CurrentScenario != Plugin.Scenario.Raptor && !Plugin.isConfusionOk(Plugin.Scenario.Raptor))
                return;

            __instance.StartCoroutine(SpawnAndMountRaptorRoutine(__instance));
        }

        private static IEnumerator SpawnAndMountRaptorRoutine(FirstPersonMover rider)
        {
            yield return null;

            if (rider == null || !rider.IsAlive() || rider.IsRidingOtherCharacter() || Singleton<EnemyFactory>.Instance == null)
                yield break;

            Transform mountTransform = null;
            isSpawningMount = true;
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
                isSpawningMount = false;
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
            if (__instance == null || !__instance.IsRidingOtherCharacter()) return;
            if (__instance.IsPlayer() || !__instance.IsAlive()) return;

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

    [HarmonyPatch(typeof(LevelManager), "MarkCurrentLevelAsWon")]
    public class LevelWonPatch
    {
        static void Postfix()
        {
            if (LevelManager.Instance == null) return;
            int totalLevelsWon = LevelManager.Instance.GetNumberOfLevelsWon();
            if (totalLevelsWon < 1) return;

            Plugin.LocalChecks[totalLevelsWon] = true;

            if (Plugin.infinite_armor_seconds > 0f)
            {
                Plugin.infinite_armor_seconds += 8f;
            }

            int goalLevel = Plugin.GetGoalLevel();
            if (goalLevel > 0)
            {
                bool inScene = Plugin.Scene.Count > 0 && !Plugin.SceneComplete;
                bool locked = inScene && totalLevelsWon >= goalLevel && !Plugin.CanWinCurrentStep();

                if (locked)
                {
                    Plugin.LockedStep = Plugin.CurrentStepNumber;
                    Plugin.NeedsNewRun = false;
                }
                else if (totalLevelsWon > goalLevel)
                {
                    if (inScene && Plugin.KeysMode != 0) Plugin.MarkCurrentStepDone();
                    Plugin.NeedsNewRun = true;
                    Plugin.ScheduleStepReset();
                }
                else if (totalLevelsWon == goalLevel)
                {
                    if (Plugin.Scene.Count > 0 && !Plugin.SceneComplete)
                    {
                        Plugin.MarkCurrentStepDone();
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
        static void Postfix(UpgradeDescription __instance, ref int __result)
        {
            if (Plugin.NextUpgradesFree > 0)
            {
                __result = 0;
            }
            else
            {
                int maxDiscount = Plugin.MaxShopDiscount;
                int discount = Plugin.EffectiveShopDiscount;
                int discountedMultiplier = Mathf.Max(1, maxDiscount - discount);
                if (discount >= maxDiscount)
                {
                    __result = 1;
                }
                else
                {
                    __result *= discountedMultiplier;
                }

                if (Plugin.OverpricedShop != 0 && __instance != null
                    && Plugin.OverpricedShop_map.Contains(UpgradeLevels.ToClass(__instance.UpgradeType, __instance.Level)))
                {
                    __result += 3;
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
            if (__result)
            {
                if (upgradeDescription != null)
                {
                    int realPlayerLevel = UpgradeManager.Instance != null
                        ? UpgradeManager.Instance.GetPlayerUpgradeLevel(upgradeDescription.UpgradeType)
                        : upgradeDescription.Level;

                    int level = upgradeDescription.IsRepeatable ? realPlayerLevel : upgradeDescription.Level;

                    Plugin.SendBuyToPython(UpgradeLevels.BuyKey(upgradeDescription.UpgradeType, upgradeDescription.Level), level);
                }

                if (__state)
                {
                    Plugin.ConsumeNextFree();
                }
            }
        }
    }

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
            return false; 
        }
    }

    [HarmonyPatch(typeof(TitleScreenUI), "OnMultiplayerButtonClicked")]
    public class MultiplayerButtonSearchPatch
    {
        static bool Prefix()
        {
            Plugin.FindConnection(null);
            return false;
        }
    }

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
            if (__instance == null) return;

            if (!__instance.IsPlayerTeam && !__instance.IsNeutral)
            {
                string killerName = (killer != null) ? killer.CharacterType.ToString() : "Inconnu";
                Plugin.Log.LogInfo($"[ARCHIPELAGO/ENNEMI TUÉ] Type: {__instance.CharacterType} | Tué par: {killerName} | Arme/Source: {damageSourceType}");
            }
        }
    }

    [HarmonyPatch(typeof(EnemyFactory), "SpawnEnemyWithRotation", new System.Type[] { typeof(Transform), typeof(Vector3), typeof(Vector3), typeof(CharacterModel) })]
    public class HijackAllEnemiesToSprinterPatch
    {
        private static readonly EnemyType[] RandomBase =
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

        private static readonly EnemyType[] RandomEnemies = RandomBase.Concat(new[]
        {
            EnemyType.SawDog,
            EnemyType.SummonWalker
        }).ToArray();

        private static readonly EnemyType[] RandomEnemiesLate = RandomBase.Concat(new[]
        {
            EnemyType.Bowman5,
            EnemyType.LaserRaptor,
            EnemyType.JetpackHammer,
            EnemyType.Spear5,
            EnemyType.SawDog,
            EnemyType.SummonWalker,
            EnemyType.Sawberus
        }).ToArray();

        private static readonly Dictionary<EnemyType, EnemyType> LevelUpMap =
            new Dictionary<EnemyType, EnemyType>()
            {
                { EnemyType.Bowman1, EnemyType.Bowman2 },
                { EnemyType.Bowman2, EnemyType.Bowman3 },
                { EnemyType.Bowman3, EnemyType.Bowman4 },
                { EnemyType.Bowman4, EnemyType.Bowman5 },
                { EnemyType.Zombie1, EnemyType.Zombie2 },
                { EnemyType.Zombie2, EnemyType.Zombie3 },
                { EnemyType.Zombie3, EnemyType.Zombie4 },
                { EnemyType.ZombieArcher1, EnemyType.ZombieArcher2 },
                { EnemyType.Swordsman1, EnemyType.Swordsman2 },
                { EnemyType.Swordsman2, EnemyType.Swordsman3 },
                { EnemyType.Swordsman3, EnemyType.Swordsman4 },
                { EnemyType.Swordsman4, EnemyType.Swordsman5 },
                { EnemyType.Bowman2_Stationary, EnemyType.Bowman4_Stationary },
                { EnemyType.FireRaptor, EnemyType.LaserRaptor },
                { EnemyType.FleetAnalysisBot1, EnemyType.FleetAnalysisBot2 },
                { EnemyType.FleetAnalysisBot2, EnemyType.FleetAnalysisBot3 },
                { EnemyType.FleetAnalysisBot3, EnemyType.FleetAnalysisBot4 },
                { EnemyType.Hammer1, EnemyType.Hammer2 },
                { EnemyType.Hammer2, EnemyType.Hammer3 },
                { EnemyType.Hammer3, EnemyType.Hammer5 },
                { EnemyType.Spear1, EnemyType.Spear2 },
                { EnemyType.Spear2, EnemyType.Spear3 },
                { EnemyType.Spear3, EnemyType.Spear4 },
                { EnemyType.Spear4, EnemyType.Spear5 },
                { EnemyType.SawDog, EnemyType.Sawberus },
                { EnemyType.Spidertron5000, EnemyType.Spidertron6000 },
                { EnemyType.Sprinter1, EnemyType.Sprinter2 },
                { EnemyType.FleetOverseer, EnemyType.FleetCaptain },
                { EnemyType.BusinessSword, EnemyType.BusinessJetpack },
                { EnemyType.BusinessJetpack, EnemyType.BusinessHammer }
            };

        private static readonly Dictionary<EnemyType, EnemyType> EasyModeMap =
            new Dictionary<EnemyType, EnemyType>()
            {
                { EnemyType.Bowman5, EnemyType.Bowman4 },
                { EnemyType.Bowman2, EnemyType.Bowman1 },
                { EnemyType.Bowman3, EnemyType.Bowman2 },
                { EnemyType.Bowman4, EnemyType.Bowman3 },
                { EnemyType.Zombie2, EnemyType.Zombie1 },
                { EnemyType.Zombie3, EnemyType.Zombie2 },
                { EnemyType.Zombie4, EnemyType.Zombie3 },
                { EnemyType.ZombieArcher2, EnemyType.ZombieArcher1 },
                { EnemyType.Swordsman2, EnemyType.Swordsman1 },
                { EnemyType.Swordsman3, EnemyType.Swordsman2 },
                { EnemyType.Swordsman4, EnemyType.Swordsman3 },
                { EnemyType.Swordsman5, EnemyType.Swordsman4 },
                { EnemyType.Bowman4_Stationary, EnemyType.Bowman2_Stationary },
                { EnemyType.LaserRaptor, EnemyType.FireRaptor },
                { EnemyType.FleetAnalysisBot3, EnemyType.FleetAnalysisBot2 },
                { EnemyType.FleetAnalysisBot2, EnemyType.FleetAnalysisBot1 },
                { EnemyType.FleetAnalysisBot4, EnemyType.FleetAnalysisBot3 },
                { EnemyType.Hammer2, EnemyType.Hammer1 },
                { EnemyType.Hammer3, EnemyType.Hammer2 },
                { EnemyType.Hammer5, EnemyType.Hammer3 },
                { EnemyType.Spear5, EnemyType.Spear4 },
                { EnemyType.Spear2, EnemyType.Spear1 },
                { EnemyType.Spear3, EnemyType.Spear2 },
                { EnemyType.Spear4, EnemyType.Spear3 },
                { EnemyType.Spidertron6000, EnemyType.Spidertron5000 },
                { EnemyType.Sprinter2, EnemyType.Sprinter1 },
                { EnemyType.Sawberus, EnemyType.SawDog },
                { EnemyType.FleetCaptain, EnemyType.FleetOverseer },
                { EnemyType.BusinessJetpack, EnemyType.BusinessSword },
                { EnemyType.BusinessHammer, EnemyType.BusinessJetpack }
            };

        private static readonly Dictionary<EnemyType, EnemyType> UndeadMap =
            new Dictionary<EnemyType, EnemyType>()
            {
                { EnemyType.Bowman5, EnemyType.ZombieArcher2 },
                { EnemyType.Bowman2, EnemyType.ZombieArcher1 },
                { EnemyType.Bowman1, EnemyType.ZombieArcher1 },
                { EnemyType.Bowman3, EnemyType.ZombieArcher2 },
                { EnemyType.Bowman4, EnemyType.ZombieArcher2 },
                { EnemyType.Swordsman1, EnemyType.Zombie1 },
                { EnemyType.Swordsman2, EnemyType.Zombie1 },
                { EnemyType.Swordsman3, EnemyType.Zombie2 },
                { EnemyType.Swordsman4, EnemyType.Zombie3 },
                { EnemyType.Swordsman5, EnemyType.Zombie4 },
                { EnemyType.Bowman4_Stationary, EnemyType.ZombieArcher2 },
                { EnemyType.LaserRaptor, EnemyType.ZombieRaptor },
                { EnemyType.FireRaptor, EnemyType.ZombieRaptor },
                { EnemyType.SawDog, EnemyType.ZombieRaptor },
                { EnemyType.Sawberus, EnemyType.ZombieRaptor },
                { EnemyType.Hammer1, EnemyType.Zombie1 },
                { EnemyType.Hammer2, EnemyType.Zombie2 },
                { EnemyType.Hammer3, EnemyType.Zombie3 },
                { EnemyType.Hammer5, EnemyType.Zombie4 },
                { EnemyType.Spear5, EnemyType.Zombie4 },
                { EnemyType.Spear1, EnemyType.Zombie1 },
                { EnemyType.Spear2, EnemyType.Zombie2 },
                { EnemyType.Spear3, EnemyType.Zombie3 },
                { EnemyType.Spear4, EnemyType.Zombie4 },
                { EnemyType.Sprinter2, EnemyType.ZombieJetpack1 },
                { EnemyType.Sprinter1, EnemyType.ZombieJetpack1 },
                { EnemyType.FleetCaptain, EnemyType.ZombieFleetCaptain },
                { EnemyType.FleetCaptain2, EnemyType.ZombieFleetCaptain },
                { EnemyType.FleetCaptain3, EnemyType.ZombieFleetCaptain },
                { EnemyType.FleetCaptain4, EnemyType.ZombieFleetCaptain },
                { EnemyType.FleetCaptain5, EnemyType.ZombieFleetCaptain },
                { EnemyType.BusinessJetpack, EnemyType.ZombieJetpack1 },
                { EnemyType.BusinessHammer, EnemyType.ZombieJetpack1 }
            };

        private const string PillarArchersLevelID = "u3bronze2";

        private static bool IsPillarArcherLevel()
        {
            LevelManager lm = LevelManager.Instance;
            if (lm == null) return false;

            string id = lm.GetCurrentLevelID();
            if (string.IsNullOrEmpty(id)) id = lm.GetLastSpawnedLevelID();
            return string.Equals(id, PillarArchersLevelID, System.StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Plugin.Scenario[] HandledScenarios =
        {
            Plugin.Scenario.Sprinter, Plugin.Scenario.Random,
            Plugin.Scenario.LevelUp, Plugin.Scenario.EasyMode, Plugin.Scenario.Undead, Plugin.Scenario.Doggos, Plugin.Scenario.Summoners
        };

        private static readonly List<Plugin.Scenario> _active = new List<Plugin.Scenario>(8);

        private static bool IsMount(EnemyType type)
        {
            return type == EnemyType.FireRaptor || type == EnemyType.LaserRaptor || type == EnemyType.ZombieRaptor || type == EnemyType.SawDog
                || type == EnemyType.Sawberus;
        }

        static void Prefix(EnemyFactory __instance, ref Transform enemyPrefab)
        {
            if (enemyPrefab == null) return;

            Character charComp = enemyPrefab.GetComponent<Character>();
            if (charComp == null) return;

            if (charComp.IsPlayerTeam || charComp.IsNeutral) return;
            if (LevelManager.Instance == null) return;
            bool isMount = IsMount(charComp.CharacterType);

            _active.Clear();
            foreach (Plugin.Scenario scenario in HandledScenarios)
            {
                if (isMount && scenario != Plugin.Scenario.LevelUp && scenario != Plugin.Scenario.EasyMode) continue;
                if (SummonAllyGuardPatch.InSummon) continue;

                if (scenario == Plugin.CurrentScenario || Plugin.isConfusionOk(scenario)) _active.Add(scenario);
            }
            if (_active.Count == 0) return;

            EnemyType targetType = charComp.CharacterType;
            int endLevel = LevelManager.Instance.GetNumberOfLevelsWon();

            foreach (Plugin.Scenario scenario in _active)
            {
                if (charComp.CharacterType == EnemyType.Bowman2 && scenario == Plugin.Scenario.EasyMode && IsPillarArcherLevel()) continue;
                ApplyScenario(scenario, ref targetType, endLevel);
            }

            if (charComp.CharacterType != targetType)
            {
                if (SummonAllyGuardPatch.InSummon && charComp.GetComponent<FirstPersonMover>() == null) return;
                Character newPrefabChar = __instance.GetEnemyPrefab(targetType);
                if (newPrefabChar != null)
                {
                    enemyPrefab = newPrefabChar.transform;
                }
            }
        }

        public static bool isSummoner = false;

        private static void ApplyScenario(Plugin.Scenario scenario, ref EnemyType targetType, int endLevel)
        {
            if (scenario == Plugin.Scenario.Undead || scenario == Plugin.Scenario.LevelUp || scenario == Plugin.Scenario.EasyMode)
            {
                Dictionary<EnemyType, EnemyType> takeFrom =
                    scenario == Plugin.Scenario.Undead ? UndeadMap :
                    scenario == Plugin.Scenario.LevelUp ? LevelUpMap : EasyModeMap;

                if (takeFrom.TryGetValue(targetType, out EnemyType mappedType))
                {
                    targetType = mappedType;
                }
            }
            else if (scenario == Plugin.Scenario.Summoners)
            {
                isSummoner = !isSummoner;
                if (isSummoner) targetType = EnemyType.SummonWalker;
                else targetType = EnemyType.FleetAnalysisBot3;
            }
            else if (scenario == Plugin.Scenario.Doggos)
            {
                float randomRoll = UnityEngine.Random.Range(0f, 100f);
                if (randomRoll < 25f)
                {
                    targetType = EnemyType.Spidertron5000;
                }
                else if (randomRoll < 66f)
                {
                    targetType = EnemyType.SawDog;
                }
                else
                {
                    targetType = EnemyType.FireRaptor;
                }

                float randomRollBoss = UnityEngine.Random.Range(0f, 100f);
                if (randomRollBoss < 2.44f && targetType == EnemyType.SawDog)
                {
                    targetType = EnemyType.Sawberus;
                }
                else if (randomRollBoss < 2.94f && targetType == EnemyType.FireRaptor)
                {
                    targetType = EnemyType.LaserRaptor;
                }
                else if (randomRollBoss < 4f && targetType == EnemyType.Spidertron5000)
                {
                    targetType = EnemyType.Spidertron6000;
                }
            }
            else if (scenario == Plugin.Scenario.Sprinter)
            {
                float chanceSprinter2 = Mathf.Floor(endLevel / 3f) * 1f;
                float chanceSprinterBowman = Mathf.Floor(endLevel / 5f) * 2f;

                float randomRoll = UnityEngine.Random.Range(0f, 100f);

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
            else if (scenario == Plugin.Scenario.Random)
            {
                EnemyType[] enemies = endLevel > 13
                    ? RandomEnemiesLate
                    : RandomEnemies;

                targetType = enemies[UnityEngine.Random.Range(0, enemies.Length)];
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

                Plugin.SendToClient("DEATH");
            }
        }
    }

    [HarmonyPatch(typeof(FirstPersonMover), "RefreshUpgrades")]
    public class RefreshUpgradesPatch
    {
        private static readonly ConditionalWeakTable<FirstPersonMover, object> _infernoRolls =
            new ConditionalWeakTable<FirstPersonMover, object>();
        private static readonly ConditionalWeakTable<FirstPersonMover, object> _armorRolls =
            new ConditionalWeakTable<FirstPersonMover, object>();
        private static readonly ConditionalWeakTable<FirstPersonMover, object>.CreateValueCallback InfernoRoll =
            _ => Plugin.isConfusionOk(Plugin.Scenario.Inferno);
        private static readonly ConditionalWeakTable<FirstPersonMover, object>.CreateValueCallback ArmorRoll =
            _ => Plugin.isConfusionOk(Plugin.Scenario.Armor);
        private static long _weaponKey = long.MinValue;
        private static long _armorKey = long.MinValue;
        private static long _overpricedKey = long.MinValue;

        static void Prefix(FirstPersonMover __instance)
        {
            if (__instance == null) return;

            if (__instance.IsMainPlayer())
            {
                PlayerPrefix(__instance);
                return;
            }

            if (!__instance.IsAlive() || __instance.IsPlayerTeam) return;
            EnemyPrefix(__instance);
        }

        private static void EnemyPrefix(FirstPersonMover enemy)
        {
            UpgradeCollection upgrades = null;

            if (Plugin.IsInfernoActive || (bool)_infernoRolls.GetValue(enemy, InfernoRoll))
            {
                upgrades = Refl.GetUpgrades(enemy);
                if (upgrades != null)
                {
                    upgrades.AddUpgradeIfMissing(UpgradeType.FireSword, 1);
                    upgrades.AddUpgradeIfMissing(UpgradeType.FireHammer, 1);
                    upgrades.AddUpgradeIfMissing(UpgradeType.FireSpear, 1);
                    upgrades.AddUpgradeIfMissing(UpgradeType.FireArrow, 1);
                    upgrades.AddUpgradeIfMissing(UpgradeType.FireClaws, 1);
                }
            }

            if (enemy.IsNeutral) return;

            bool armored = Plugin.ScenarioActive(5);
            if (!armored)
            {
                GameFlowManager flow = Singleton<GameFlowManager>.Instance;
                if (flow != null && !flow.IsOnTitleScreen())
                    armored = (bool)_armorRolls.GetValue(enemy, ArmorRoll);
            }

            if (armored)
            {
                if (upgrades == null) upgrades = Refl.GetUpgrades(enemy);
                if (upgrades != null) upgrades.AddUpgradeIfMissing(UpgradeType.Armor, 1);
            }
        }

        private static void PlayerPrefix(FirstPersonMover player)
        {
            if (!Plugin.HasStartingWeaponOverride) return;

            UpgradeCollection upgrades = Refl.GetUpgrades(player);
            if (upgrades == null) return;

            if (player.GetTimeSinceCreation() < 1f)
            {
                Plugin.StripStartingSword(player, upgrades);
            }

            upgrades.AddUpgradeIfMissing(Plugin.WeaponToUpgrade((int)Plugin.CurrentWeapon), 1);
        }

        static void Postfix(FirstPersonMover __instance)
        {
            if (__instance == null || !__instance.IsMainPlayer()) return;
            if (__instance.GetTimeSinceCreation() >= 1f) return;

            long key = (long)__instance.GetInstanceID() * 10L + Plugin.FinishedSteps;

            EquipStartingWeapon(__instance, key);

            if (!__instance.IsAlive()) return;

            RollOverpriced(key);
            ApplyStartArmor(__instance, key);
        }

        private static void EquipStartingWeapon(FirstPersonMover player, long key)
        {
            if (!Plugin.HasStartingWeaponOverride) return;

            if (key == _weaponKey) return;
            _weaponKey = key;

            byte weapon = (byte)Plugin.CurrentWeapon;
            bool ok = Plugin.TryEquipWeapon(player, weapon);
            Plugin.Log.LogInfo($"[AP] Starting weapon {Plugin.WeaponName(weapon)} equipped: {ok}");
        }
        private static void RollOverpriced(long key)
        {
            if (Plugin.OverpricedShop == 0) return;
            if (key == _overpricedKey) return;
            _overpricedKey = key;

            Plugin.OverpricedShop_map.Clear();
            int count = Mathf.Min((int)Plugin.OverpricedShop, (int)38);

            while (Plugin.OverpricedShop_map.Count < count)
            {
                Plugin.OverpricedShop_map.Add((UpgradesClass)UnityEngine.Random.Range(1, 38 + 1));
            }
        }

        private static void ApplyStartArmor(FirstPersonMover player, long key)
        {
            if (Plugin.StartWithArmor_left == 0) return;
            if (key == _armorKey) return;
            _armorKey = key;

            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ApplyArmorRoutine(player));
        }

        private static IEnumerator ApplyArmorRoutine(FirstPersonMover player)
        {
            yield return null; 

            if (player == null || !player.IsAlive() || Plugin.StartWithArmor_left == 0) yield break;

            ArmorManager armorManager = Singleton<ArmorManager>.Instance;
            if (armorManager == null) yield break;

            armorManager.ApplyArmorTo(player, forceGiveArmor: true);

            if (armorManager.HasAnyArmor(player))
            {
                Plugin.ConsumeStartArmor();
            }
            else
            {
                Plugin.Log.LogWarning("[AP] Start with Armor: no armor applied, not consumed");
            }
        }
    }

    [HarmonyPatch(typeof(SpeechAudioManager), "ReadSpreadsheetsFromDisk")]
    public class RandomizeAllSpeechSequencesPatch
    {
        private static Dictionary<string, SpeechSequence> _loadedDict;
        private static Dictionary<string, SpeechSequence> _originalBackup;

        static void Postfix(ref Dictionary<string, SpeechSequence> __result)
        {
            if (__result == null || __result.Count <= 1) return;

            _loadedDict = __result;

            if (_originalBackup == null)
            {
                _originalBackup = new Dictionary<string, SpeechSequence>(__result);
            }

            ApplyVoiceRandomization();
        }

        public static void ApplyVoiceRandomization()
        {
            if (_loadedDict == null || _originalBackup == null || _originalBackup.Count <= 1) return;

            // Restaure l'original
            foreach (var pair in _originalBackup)
                _loadedDict[pair.Key] = pair.Value;

            if (!Plugin.RandomizeVoices) return;

            List<string> keys = _originalBackup
                .Where(p => p.Value != null && p.Value.Sentences != null && p.Value.Sentences.Count > 0)
                .Select(p => p.Key)
                .ToList();

            if (keys.Count < 2) return;

            List<SpeechSequence> values = keys.Select(k => _originalBackup[k]).ToList();

            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                SpeechSequence tmp = values[i]; values[i] = values[j]; values[j] = tmp;
            }

            for (int i = 0; i < keys.Count; i++)
                _loadedDict[keys[i]] = values[i];
        }
    }

    [HarmonyPatch(typeof(SummonAllyAbility), "StartConstructingEnemyIfTime")]
    public class SummonAllyGuardPatch
    {
        public static bool InSummon = false;

        static void Prefix() { InSummon = true; }

        static System.Exception Finalizer(System.Exception __exception)
        {
            InSummon = false;
            if (__exception != null)
                Plugin.Log.LogError("[AP] SummonAlly: " + __exception); 
            return __exception; 
        }
    }

    [HarmonyPatch]
    public class SpeechCoroutineSafetyPatch
    {
        static MethodBase TargetMethod()
        {
            var inner = AccessTools.FirstInner(typeof(SpeechAudioManager),
                t => t.Name.Contains("LoadAndPlayAudioClipOnCurrentAudioSource"));
            return AccessTools.Method(inner, "MoveNext");
        }

        static System.Exception Finalizer(System.Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;

            Plugin.Log.LogWarning("[AP] Speech coroutine error ignored: " + __exception.Message);
            var mgr = Singleton<SpeechAudioManager>.Instance;
            if (mgr != null)
                AccessTools.Field(typeof(SpeechAudioManager), "_loadingClipFromResources")?.SetValue(mgr, false);
            __result = false; 
            return null; 
        }
    }
}