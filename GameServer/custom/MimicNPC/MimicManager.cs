using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Realm;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DOL.GS.Scripts
{
    #region Battlegrounds

    public static class MimicBattlegrounds
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static MimicBattleground ThidBattleground;

        public static void Initialize()
        {
            ThidBattleground = new MimicBattleground(252,
                                                    new Point3D(37200, 51200, 3950),
                                                    new Point3D(19820, 19305, 4050),
                                                    new Point3D(53300, 26100, 4270),
                                                    30,
                                                    120,
                                                    20,
                                                    24);
        }

        public class MimicBattleground
        {
            public MimicBattleground(ushort region, Point3D albSpawn, Point3D hibSpawn, Point3D midSpawn, int minMimics, int maxMimics, byte minLevel, byte maxLevel)
            {
                m_region = region;
                m_albSpawnPoint = albSpawn;
                m_hibSpawnPoint = hibSpawn;
                m_midSpawnPoint = midSpawn;
                m_minTotalMimics = minMimics;
                m_maxTotalMimics = maxMimics;
                m_minLevel = minLevel;
                m_maxLevel = maxLevel;
            }

            private ECSGameTimer m_masterTimer;
            private ECSGameTimer m_spawnTimer;

            private int m_timerInterval = 600000; // 10 minutes
            private long m_resetMaxTime = 0;

            private List<MimicNPC> m_albMimics = new List<MimicNPC>();
            private List<MimicNPC> m_albStagingList = new List<MimicNPC>();

            private List<MimicNPC> m_hibMimics = new List<MimicNPC>();
            private List<MimicNPC> m_hibStagingList = new List<MimicNPC>();

            private List<MimicNPC> m_midMimics = new List<MimicNPC>();
            private List<MimicNPC> m_midStagingList = new List<MimicNPC>();

            private readonly List<BattleStats> m_battleStats = new List<BattleStats>();

            private Point3D m_albSpawnPoint;
            private Point3D m_hibSpawnPoint;
            private Point3D m_midSpawnPoint;

            private ushort m_region;

            private byte m_minLevel;
            private byte m_maxLevel;

            private int m_minTotalMimics;
            private int m_maxTotalMimics;

            private int m_currentMinTotalMimics;
            private int m_currentMaxTotalMimics;

            private int m_currentMaxAlb;
            private int m_currentMaxHib;
            private int m_currentMaxMid;

            private int m_groupChance = 50;

            public void Start()
            {
                // For quick mass testing.
                //Parallel.For(0, 2000, TickInternal);

                if (m_masterTimer == null)
                {
                    m_masterTimer = new ECSGameTimer(null, new ECSGameTimer.ECSTimerCallback(MasterTimerCallback));
                    m_masterTimer.Start();
                }
            }

            public void TickInternal(int index)
            {
                MimicNPC albMimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Albion), 24);
                MimicManager.AddMimicToWorld(albMimic, m_albSpawnPoint, m_region);

                MimicNPC hibMimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Hibernia), 24);
                MimicManager.AddMimicToWorld(hibMimic, m_hibSpawnPoint, m_region);

                MimicNPC midMimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Midgard), 24);
                MimicManager.AddMimicToWorld(midMimic, m_midSpawnPoint, m_region);
            }

            public void Stop()
            {
                if (m_masterTimer != null)
                {
                    m_masterTimer.Stop();
                    m_masterTimer = null;
                }

                if (m_spawnTimer != null)
                {
                    m_spawnTimer.Stop();
                    m_spawnTimer = null;
                }

                ValidateLists();

                m_albStagingList.Clear();
                m_hibStagingList.Clear();
                m_midStagingList.Clear();
            }

            public void Clear()
            {
                Stop();

                if (m_albMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_albMimics)
                        mimic.Delete();

                    m_albMimics.Clear();
                }

                if (m_hibMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                        mimic.Delete();

                    m_hibMimics.Clear();
                }

                if (m_midMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_midMimics)
                        mimic.Delete();

                    m_midMimics.Clear();
                }
            }

            private int MasterTimerCallback(ECSGameTimer timer)
            {
                if (GameLoop.GameLoopTime > m_resetMaxTime)
                    ResetMaxMimics();

                ValidateLists();
                RefreshLists();
                SpawnLists();

                int totalMimics = m_albMimics.Count + m_hibMimics.Count + m_midMimics.Count;
                log.Info("Alb: " + m_albMimics.Count + "/" + m_currentMaxAlb);
                log.Info("Hib: " + m_hibMimics.Count + "/" + m_currentMaxHib);
                log.Info("Mid: " + m_midMimics.Count + "/" + m_currentMaxMid);
                log.Info("Total Mimics: " + totalMimics + "/" + m_currentMaxTotalMimics);

                return m_timerInterval + Util.Random(-300000, 300000); // 10 minutes + or - 5 minutes
            }

            /// <summary>
            /// Removes any dead or deleted mimics from each realm list.
            /// </summary>
            private void ValidateLists()
            {
                if (m_albMimics.Count != 0)
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_albMimics = validatedList;
                }

                if (m_hibMimics.Count != 0)
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_hibMimics = validatedList;
                }

                if (m_midMimics.Count != 0)
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_midMimics = validatedList;
                }
            }

            /// <summary>
            /// Adds new mimics to each realm list based on the difference between max and current count
            /// </summary>
            private void RefreshLists()
            {
                if (m_albMimics.Count < m_currentMaxAlb)
                {
                    for (int i = 0; i < m_currentMaxAlb - m_albMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Albion), level);
                        m_albMimics.Add(mimic);
                    }
                }

                if (m_hibMimics.Count < m_currentMaxHib)
                {
                    for (int i = 0; i < m_currentMaxHib - m_hibMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Hibernia), level);
                        m_hibMimics.Add(mimic);
                    }
                }

                if (m_midMimics.Count < m_currentMaxMid)
                {
                    for (int i = 0; i < m_currentMaxMid - m_midMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Midgard), level);
                        m_midMimics.Add(mimic);
                    }
                }
            }

            private void SpawnLists()
            {
                m_albStagingList = new List<MimicNPC>();
                m_hibStagingList = new List<MimicNPC>();
                m_midStagingList = new List<MimicNPC>();

                if (m_albMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_albStagingList.Add(mimic);
                    }
                }

                if (m_hibMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_hibStagingList.Add(mimic);
                    }
                }

                if (m_midMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_midStagingList.Add(mimic);
                    }
                }

                SetGroupMembers(m_albStagingList);
                SetGroupMembers(m_hibStagingList);
                SetGroupMembers(m_midStagingList);

                m_spawnTimer = new ECSGameTimer(null, new ECSGameTimer.ECSTimerCallback(Spawn), 1000);
            }

            private int Spawn(ECSGameTimer timer)
            {
                bool albDone = false;
                bool hibDone = false;
                bool midDone = false;

                if (m_albStagingList.Count != 0)
                {
                    MimicManager.AddMimicToWorld(m_albStagingList[m_albStagingList.Count - 1], m_albSpawnPoint, m_region);
                    m_albStagingList.RemoveAt(m_albStagingList.Count - 1);
                }
                else
                    albDone = true;

                if (m_hibStagingList.Count != 0)
                {
                    MimicManager.AddMimicToWorld(m_hibStagingList[m_hibStagingList.Count - 1], m_hibSpawnPoint, m_region);
                    m_hibStagingList.RemoveAt(m_hibStagingList.Count - 1);
                }
                else
                    hibDone = true;

                if (m_midStagingList.Count != 0)
                {
                    MimicManager.AddMimicToWorld(m_midStagingList[m_midStagingList.Count - 1], m_midSpawnPoint, m_region);
                    m_midStagingList.RemoveAt(m_midStagingList.Count - 1);
                }
                else
                    midDone = true;

                if (albDone && hibDone && midDone)
                    return 0;
                else
                    return 5000;
            }

            private void SetGroupMembers(List<MimicNPC> list)
            {
                if (list.Count > 1)
                {
                    int groupChance = m_groupChance;
                    int groupLeaderIndex = -1;

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i + 1 < list.Count)
                        {
                            if (Util.Chance(groupChance) && !(list[i].Group?.GetMembersInTheGroup().Count > 7))
                            {
                                if (groupLeaderIndex == -1)
                                {
                                    list[i].Group = new Group(list[i]);
                                    list[i].Group.AddMember(list[i]);
                                    groupLeaderIndex = i;
                                }

                                list[groupLeaderIndex].Group.AddMember(list[i + 1]);
                                groupChance -= 5;
                            }
                            else
                            {
                                groupLeaderIndex = -1;
                                groupChance = m_groupChance;
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Gets a new total maximum and minimum of mimics for each realm randomly.
            /// </summary>
            private void ResetMaxMimics()
            {
                m_currentMaxTotalMimics = Util.Random(m_minTotalMimics, m_maxTotalMimics);
                m_currentMaxAlb = 0;
                m_currentMaxHib = 0;
                m_currentMaxMid = 0;

                for (int i = 0; i < m_currentMaxTotalMimics; i++)
                {
                    eRealm randomRealm = (eRealm)Util.Random(1, 3);

                    if (randomRealm == eRealm.Albion)
                        m_currentMaxAlb++;
                    else if (randomRealm == eRealm.Hibernia)
                        m_currentMaxHib++;
                    else if (randomRealm == eRealm.Midgard)
                        m_currentMaxMid++;
                }

                m_resetMaxTime = GameLoop.GameLoopTime + Util.Random(1800000, 3600000);
            }

            public void UpdateBattleStats(MimicNPC mimic)
            {
                m_battleStats.Add(new BattleStats(mimic.Name, mimic.RaceName, mimic.CharacterClass.Name, mimic.Kills, true));
            }

            public void BattlegroundStats(GamePlayer player)
            {
                List<MimicNPC> currentMimics = GetMasterList();
                List<BattleStats> currentStats = new List<BattleStats>();

                if (currentMimics.Count != 0)
                {
                    foreach (MimicNPC mimic in currentMimics)
                        currentStats.Add(new BattleStats(mimic.Name, mimic.RaceName, mimic.CharacterClass.Name, mimic.Kills, false));
                }

                List<BattleStats> masterStatList = new List<BattleStats>();
                masterStatList.AddRange(currentStats);

                lock (m_battleStats)
                {
                    masterStatList.AddRange(m_battleStats);
                }

                List<BattleStats> sortedList = masterStatList.OrderByDescending(obj => obj.TotalKills).ToList();

                string message = "----------------------------------------\n\n";
                int index = Math.Min(25, sortedList.Count);

                if (sortedList.Count != 0)
                {
                    for (int i = 0; i < index; i++)
                    {
                        string stats = string.Format("{0}. {1} - {2} - {3} - Kills: {4}",
                            i + 1,
                            sortedList[i].Name,
                            sortedList[i].Race,
                            sortedList[i].ClassName,
                            sortedList[i].TotalKills);

                        if (sortedList[i].IsDead)
                            stats += " - DEAD";

                        stats += "\n\n";

                        message += stats;
                    }
                }

                switch (player.Realm)
                {
                    case eRealm.Albion:
                    if (m_albMimics.Count != 0)
                        message += "Alb count: " + m_albMimics.Count;
                    break;

                    case eRealm.Hibernia:
                    if (m_hibMimics.Count != 0)
                        message += "Hib count: " + m_hibMimics.Count;
                    break;

                    case eRealm.Midgard:
                    if (m_midMimics.Count != 0)
                        message += "Mid count: " + m_midMimics.Count;
                    break;
                }

                player.Out.SendMessage(message, PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_PopupWindow);
            }

            public List<MimicNPC> GetMasterList()
            {
                List<MimicNPC> masterList = new List<MimicNPC>();

                lock (m_albMimics)
                {
                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                lock (m_hibMimics)
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                lock (m_midMimics)
                {
                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                return masterList;
            }
        }

        private struct BattleStats
        {
            public string Name;
            public string Race;
            public string ClassName;
            public int TotalKills;
            public bool IsDead;

            public BattleStats(string name, string race, string className, int totalKills, bool dead)
            {
                Name = name;
                Race = race;
                ClassName = className;
                TotalKills = totalKills;
                IsDead = dead;
            }
        }
    }

    #endregion Battlegrounds

    public static class MimicManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static List<MimicNPC> MimicNPCs = new List<MimicNPC>();

        public static bool Initialize()
        {
            log.Info("MimicManager Initializing...");

            MimicBattlegrounds.Initialize();

            return true;
        }

        public static bool AddMimicToWorld(MimicNPC mimic, Point3D position, ushort region)
        {
            if (mimic != null)
            {
                mimic.X = position.X;
                mimic.Y = position.Y;
                mimic.Z = position.Z;

                mimic.CurrentRegionID = region;

                if (mimic.AddToWorld())
                    return true;
            }

            return false;
        }

        public static MimicNPC GetMimic(eMimicClass charClass, byte level, string name = "", eGender gender = eGender.Neutral, eSpecType spec = eSpecType.None, bool preventCombat = false)
        {
            if (charClass == eMimicClass.None)
                return null;

            MimicNPC mimic = new MimicNPC(charClass, level, gender, spec);

            if (mimic != null)
            {
                if (name != "")
                    mimic.Name = name;

                if (gender != eGender.Neutral)
                {
                    mimic.Gender = gender;

                    foreach (PlayerRace race in PlayerRace.AllRaces)
                    {
                        if (race.ID == (eRace)mimic.Race)
                        {
                            mimic.Model = (ushort)race.GetModel(gender);
                            break;
                        }
                    }
                }

                if (preventCombat)
                {
                    MimicBrain mimicBrain = mimic.Brain as MimicBrain;

                    if (mimicBrain != null)
                        mimicBrain.PreventCombat = preventCombat;
                }

                return mimic;
            }

            return null;
        }

        public static eMimicClass GetRandomMimicClass(eRealm realm = eRealm.None)
        {
            Array mimicClasses = Enum.GetValues(typeof(eMimicClass));

            if (realm == eRealm.None)
            {
                int randomIndex = Util.Random(mimicClasses.Length - 1);
                return (eMimicClass)mimicClasses.GetValue(randomIndex);
            }

            List<eMimicClass> classes = new List<eMimicClass>();

            foreach (eMimicClass mimicClass in mimicClasses)
            {
                if (GlobalConstants.STARTING_CLASSES_DICT[realm].Contains((eCharacterClass)mimicClass))
                    classes.Add(mimicClass);
            }

            return classes[Util.Random(classes.Count - 1)];
        }

        public static eMimicClass GetRandomMeleeClass(eRealm realm = eRealm.None)
        {
            List<eMimicClass> meleeClasses = new List<eMimicClass>();

            foreach (eMimicClass mimicClass in Enum.GetValues(typeof(eMimicClass)))
            {
                switch (mimicClass)
                {
                    case eMimicClass.None:
                    case eMimicClass.Cabalist:
                    case eMimicClass.Sorcerer:
                    case eMimicClass.Theurgist:
                    case eMimicClass.Wizard:
                    case eMimicClass.Eldritch:
                    case eMimicClass.Enchanter:
                    case eMimicClass.Mentalist:
                    case eMimicClass.Bonedancer:
                    case eMimicClass.Runemaster:
                    case eMimicClass.Spiritmaster:
                    continue;

                    default:
                    if (realm != eRealm.None)
                            if (!GlobalConstants.STARTING_CLASSES_DICT[realm].Contains((eCharacterClass)mimicClass))
                                continue;

                    meleeClasses.Add(mimicClass);

                    break;
                }
            }

            return meleeClasses[Util.Random(meleeClasses.Count - 1)];
        }

        public static eMimicClass GetRandomCasterClass(eRealm realm = eRealm.None)
        {
            List<eMimicClass> casterClasses = new List<eMimicClass>();

            foreach (eMimicClass mimicClass in Enum.GetValues(typeof(eMimicClass)))
            {
                switch (mimicClass)
                {
                    case eMimicClass.Cabalist:
                    case eMimicClass.Sorcerer:
                    case eMimicClass.Theurgist:
                    case eMimicClass.Wizard:
                    case eMimicClass.Eldritch:
                    case eMimicClass.Enchanter:
                    case eMimicClass.Mentalist:
                    case eMimicClass.Bonedancer:
                    case eMimicClass.Runemaster:
                    case eMimicClass.Spiritmaster:

                    if (realm != eRealm.None)
                        if (!GlobalConstants.STARTING_CLASSES_DICT[realm].Contains((eCharacterClass)mimicClass))
                            continue;

                    casterClasses.Add(mimicClass);
                    break;

                    default:
                    continue;
                }
            }

            return casterClasses[Util.Random(casterClasses.Count - 1)];
        }
    }

    #region Equipment

    public static class MimicEquipment
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void SetWeaponROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eDamageType damageType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot, damageType);

            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            living.Inventory.AddItem(slot, item);
        }

        public static void SetArmorROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.HELM; i <= Slot.ARMS; i++)
            {
                if (i == Slot.JEWELRY || i == Slot.CLOAK)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);

                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);

                living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetJewelryROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.JEWELRY; i <= Slot.RIGHTRING; i++)
            {
                if (i is Slot.TORSO or Slot.LEGS or Slot.ARMS or Slot.FOREARMS or Slot.SHIELD)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);

                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);

                if (i == Slot.RIGHTRING || i == Slot.LEFTRING)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                else if (i == Slot.LEFTWRIST || i == Slot.RIGHTWRIST)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                else
                    living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetInstrumentROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot, instrumentType);

            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            living.Inventory.AddItem(slot, item);
        }

        public static void SetMeleeWeapon(IGamePlayer player, eObjectType weapType, eHand hand, eWeaponDamageType damageType = 0)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 4);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));
            if (itemList.Count != 0)
            {
                List<DbItemTemplate> itemsToKeep = new List<DbItemTemplate>();

                foreach (DbItemTemplate item in itemList)
                {
                    bool shouldAddItem = false;

                    switch (hand)
                    {
                        case eHand.oneHand:
                        shouldAddItem = item.Item_Type == Slot.RIGHTHAND || item.Item_Type == Slot.LEFTHAND;
                        break;

                        case eHand.leftHand:
                        shouldAddItem = item.Item_Type == Slot.LEFTHAND;
                        break;

                        case eHand.twoHand:
                        shouldAddItem = item.Item_Type == Slot.TWOHAND && (damageType == 0 || item.Type_Damage == (int)damageType);
                        break;

                        default:
                        break;
                    }

                    if (shouldAddItem)
                        itemsToKeep.Add(item);
                }

                if (itemsToKeep.Count != 0)
                {
                    DbItemTemplate itemTemplate = itemsToKeep[Util.Random(itemsToKeep.Count - 1)];
                    AddItem(player, itemTemplate, hand);
                }
            }
            else
                log.Info("No melee weapon found for " + player.Name);
        }

        public static void SetRangedWeapon(IGamePlayer player, eObjectType weapType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Item_Type").IsEqualTo(13).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);

                return;
            }
            else
                log.Info("No Ranged weapon found for " + player.Name);
        }

        public static void SetShield(IGamePlayer player, int shieldSize)
        {
            if (shieldSize < 1)
                return;

            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Shield).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("Type_Damage").IsEqualTo(shieldSize).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);

                return;
            }
            else
                log.Info("No Shield found for " + player.Name);
        }

        public static void SetArmor(IGamePlayer player, eObjectType armorType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)armorType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));

            if (itemList.Count != 0)
            {
                Dictionary<int, List<DbItemTemplate>> armorSlots = new Dictionary<int, List<DbItemTemplate>>();

                foreach (DbItemTemplate template in itemList)
                {
                    if (!armorSlots.TryGetValue(template.Item_Type, out List<DbItemTemplate> slotList))
                    {
                        slotList = new List<DbItemTemplate>();
                        armorSlots[template.Item_Type] = slotList;
                    }

                    slotList.Add(template);
                }

                foreach (var pair in armorSlots)
                {
                    if (pair.Value.Count != 0)
                    {
                        DbItemTemplate itemTemplate = pair.Value[Util.Random(pair.Value.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }
            }
            else
                log.Info("No armor found for " + player.Name);
        }

        public static void SetInstrument(IGamePlayer player, eObjectType weapType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("DPS_AF").IsEqualTo((int)instrumentType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                DbInventoryItem item = GameInventoryItem.Create(itemTemplate);
                player.Inventory.AddItem(slot, item);

                return;
            }
            else
                log.Info("No instrument found for " + player.Name);
        }

        public static void SetJewelry(IGamePlayer player)
        {
            int min = Math.Max(1, player.Level - 30);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            List<DbItemTemplate> cloakList = new List<DbItemTemplate>();
            List<DbItemTemplate> jewelryList = new List<DbItemTemplate>();
            List<DbItemTemplate> ringList = new List<DbItemTemplate>();
            List<DbItemTemplate> wristList = new List<DbItemTemplate>();
            List<DbItemTemplate> neckList = new List<DbItemTemplate>();
            List<DbItemTemplate> waistList = new List<DbItemTemplate>();

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Magical).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));
            if (itemList.Count != 0)
            {
                foreach (DbItemTemplate template in itemList)
                {
                    if (template.Item_Type == Slot.CLOAK)
                    {
                        template.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                        cloakList.Add(template);
                    }
                    else if (template.Item_Type == Slot.JEWELRY)
                        jewelryList.Add(template);
                    else if (template.Item_Type == Slot.LEFTRING || template.Item_Type == Slot.RIGHTRING)
                        ringList.Add(template);
                    else if (template.Item_Type == Slot.LEFTWRIST || template.Item_Type == Slot.RIGHTWRIST)
                        wristList.Add(template);
                    else if (template.Item_Type == Slot.NECK)
                        neckList.Add(template);
                    else if (template.Item_Type == Slot.WAIST)
                        waistList.Add(template);
                }

                List<List<DbItemTemplate>> masterList = new List<List<DbItemTemplate>>
                {
                cloakList,
                jewelryList,
                neckList,
                waistList
                };

                foreach (List<DbItemTemplate> list in masterList)
                {
                    if (list.Count != 0)
                    {
                        DbItemTemplate itemTemplate = list[Util.Random(list.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                // Add two rings and bracelets
                for (int i = 0; i < 2; i++)
                {
                    if (ringList.Count != 0)
                    {
                        DbItemTemplate itemTemplate = ringList[Util.Random(ringList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }

                    if (wristList.Count != 0)
                    {
                        DbItemTemplate itemTemplate = wristList[Util.Random(wristList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                // Not sure this is needed what were you thinking past self?
                if (player.Inventory.GetItem(eInventorySlot.Cloak) == null)
                {
                    DbItemTemplate cloak = GameServer.Database.FindObjectByKey<DbItemTemplate>("cloak");
                    cloak.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                    AddItem(player, cloak);
                }
            }
            else
                log.Info("No jewelry of any kind found for " + player.Name);
        }

        private static void AddItem(IGamePlayer player, DbItemTemplate itemTemplate, eHand hand = eHand.None)
        {
            if (itemTemplate == null)
                log.Info("itemTemplate in AddItem is null");

            DbInventoryItem item = GameInventoryItem.Create(itemTemplate);

            if (item != null)
            {
                if (item.Item_Type == Slot.LEFTRING || item.Item_Type == Slot.RIGHTRING)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTWRIST || item.Item_Type == Slot.RIGHTWRIST)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTHAND && item.Object_Type != (int)eObjectType.Shield && hand == eHand.oneHand)
                {
                    player.Inventory.AddItem(eInventorySlot.RightHandWeapon, item);
                    return;
                }
                else
                {
                    if (item.Object_Type == (int)eObjectType.Shield &&
                        (player.CharacterClass.ID == (int)eCharacterClass.Infiltrator ||
                        player.CharacterClass.ID == (int)eCharacterClass.Mercenary ||
                        player.CharacterClass.ID == (int)eCharacterClass.Nightshade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Ranger ||
                        player.CharacterClass.ID == (int)eCharacterClass.Blademaster ||
                        player.CharacterClass.ID == (int)eCharacterClass.Shadowblade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Berserker ||
                        (player.CharacterClass.ID == (int)eCharacterClass.Savage)))
                    {
                        player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstEmptyBackpack, eInventorySlot.LastEmptyBackpack), item);
                    }
                    else
                        player.Inventory.AddItem((eInventorySlot)item.Item_Type, item);
                }
            }
            else
                log.Info("Item failed to be created for " + player.Name);
        }
    }

    #endregion Equipment

    #region Spec

    public class MimicSpec
    {
        public static string SpecName;
        public eObjectType WeaponOneType;
        public eObjectType WeaponTwoType;
        public eWeaponDamageType DamageType = 0;
        public eSpecType SpecType;

        public bool Is2H;

        public List<SpecLine> SpecLines = new List<SpecLine>();

        public MimicSpec()
        { }

        protected void Add(string spec, uint cap, float ratio)
        {
            SpecLines.Add(new SpecLine(spec, cap, ratio));
        }

        protected string ObjToSpec(eObjectType obj)
        {
            string spec = SkillBase.ObjectTypeToSpec(obj);

            return spec;
        }

        public static MimicSpec GetSpec(eMimicClass charClass, eSpecType spec = eSpecType.None)
        {
            switch (charClass)
            {
                case eMimicClass.Armsman: return new ArmsmanSpec(spec);
                case eMimicClass.Cabalist: return new CabalistSpec(spec);
                case eMimicClass.Cleric: return new ClericSpec(spec);
                case eMimicClass.Friar: return new FriarSpec(spec);
                case eMimicClass.Infiltrator: return new InfiltratorSpec();
                case eMimicClass.Mercenary: return new MercenarySpec(spec);
                case eMimicClass.Minstrel: return new MinstrelSpec();
                case eMimicClass.Paladin: return new PaladinSpec(spec);
                case eMimicClass.Reaver: return new ReaverSpec();
                case eMimicClass.Scout: return new ScoutSpec();
                case eMimicClass.Sorcerer: return new SorcererSpec(spec);
                case eMimicClass.Theurgist: return new TheurgistSpec(spec);
                case eMimicClass.Wizard: return new WizardSpec(spec);

                case eMimicClass.Bard: return new BardSpec();
                case eMimicClass.Blademaster: return new BlademasterSpec(spec);
                case eMimicClass.Champion: return new ChampionSpec(spec);
                case eMimicClass.Druid: return new DruidSpec(spec);
                case eMimicClass.Eldritch: return new EldritchSpec(spec);
                case eMimicClass.Enchanter: return new EnchanterSpec(spec);
                case eMimicClass.Hero: return new HeroSpec(spec);
                case eMimicClass.Mentalist: return new MentalistSpec(spec);
                case eMimicClass.Nightshade: return new NightshadeSpec();
                case eMimicClass.Ranger: return new RangerSpec();
                case eMimicClass.Valewalker: return new ValewalkerSpec();
                case eMimicClass.Warden: return new WardenSpec(spec);

                case eMimicClass.Berserker: return new BerserkerSpec();
                case eMimicClass.Bonedancer: return new BonedancerSpec(spec);
                case eMimicClass.Healer: return new HealerSpec(spec);
                case eMimicClass.Hunter: return new HunterSpec();
                case eMimicClass.Runemaster: return new RunemasterSpec(spec);
                case eMimicClass.Savage: return new SavageSpec(spec);
                case eMimicClass.Shadowblade: return new ShadowbladeSpec(spec);
                case eMimicClass.Shaman: return new ShamanSpec(spec);
                case eMimicClass.Skald: return new SkaldSpec();
                case eMimicClass.Spiritmaster: return new SpiritmasterSpec(spec);
                case eMimicClass.Thane: return new ThaneSpec(spec);
                case eMimicClass.Warrior: return new WarriorSpec();
            }

            return null;
        }
    }

    public struct SpecLine
    {
        public string Spec;
        public uint SpecCap;
        public float levelRatio;

        public SpecLine(string spec, uint cap, float ratio)
        {
            Spec = spec;
            SpecCap = cap;
            levelRatio = ratio;
        }
    }

    #endregion Spec

    #region LFG

    public static class MimicLFGManager
    {
        public static List<MimicLFGEntry> LFGListAlb = new List<MimicLFGEntry>();
        public static List<MimicLFGEntry> LFGListHib = new List<MimicLFGEntry>();
        public static List<MimicLFGEntry> LFGListMid = new List<MimicLFGEntry>();

        private static long _respawnTimeAlb = 0;
        private static long _respawnTimeHib = 0;
        private static long _respawnTimeMid = 0;

        private static int minRespawnTime = 60000;
        private static int maxRespawnTime = 600000;

        private static int minRemoveTime = 300000;
        private static int maxRemoveTime = 3600000;

        private static int maxMimics = 20;
        private static int chance = 25;

        public static List<MimicLFGEntry> GetLFG(eRealm realm, byte level)
        {
            switch (realm)
            {
                case eRealm.Albion:
                {
                    if (_respawnTimeAlb == 0)
                    {
                        _respawnTimeAlb = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListAlb = GenerateList(LFGListAlb, realm, level);
                    }

                    lock (LFGListAlb)
                    {
                        LFGListAlb = ValidateList(LFGListAlb);

                        if (GameLoop.GameLoopTime > _respawnTimeAlb)
                        {
                            LFGListAlb = GenerateList(LFGListAlb, realm, level);
                            _respawnTimeAlb = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListAlb;
                }

                case eRealm.Hibernia:
                {
                    if (_respawnTimeHib == 0)
                    {
                        _respawnTimeHib = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListHib = GenerateList(LFGListHib, realm, level);
                    }

                    lock (LFGListHib)
                    {
                        LFGListHib = ValidateList(LFGListHib);

                        if (GameLoop.GameLoopTime > _respawnTimeHib)
                        {
                            LFGListHib = GenerateList(LFGListHib, realm, level);
                            _respawnTimeHib = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListHib;
                }

                case eRealm.Midgard:
                {
                    if (_respawnTimeMid == 0)
                    {
                        _respawnTimeMid = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListMid = GenerateList(LFGListMid, realm, level);
                    }

                    lock (LFGListMid)
                    {
                        LFGListMid = ValidateList(LFGListMid);

                        if (GameLoop.GameLoopTime > _respawnTimeMid)
                        {
                            LFGListMid = GenerateList(LFGListMid, realm, level);
                            _respawnTimeMid = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListMid;
                }
            }

            return null;
        }

        public static void Remove(eRealm realm, MimicLFGEntry entryToRemove)
        {
            switch (realm)
            {
                case eRealm.Albion:
                if (LFGListAlb.Count != 0)
                {
                    lock (LFGListAlb)
                    {
                        foreach (MimicLFGEntry entry in LFGListAlb)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime - 1;
                                break;
                            }
                        }
                    }
                }
                break;

                case eRealm.Hibernia:
                if (LFGListHib.Count != 0)
                {
                    lock (LFGListHib)
                    {
                        foreach (MimicLFGEntry entry in LFGListHib)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime;
                                break;
                            }
                        }
                    }
                }
                break;

                case eRealm.Midgard:
                if (LFGListMid.Count != 0)
                {
                    lock (LFGListMid)
                    {
                        foreach (MimicLFGEntry entry in LFGListMid)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime;
                                break;
                            }
                        }
                    }
                }
                break;
            }
        }

        private static List<MimicLFGEntry> GenerateList(List<MimicLFGEntry> entries, eRealm realm, byte level)
        {
            if (entries.Count < maxMimics)
            {
                int mimicsToAdd = maxMimics - entries.Count;

                for (int i = 0; i < mimicsToAdd; i++)
                {
                    if (Util.Chance(chance))
                    {
                        int levelMin = Math.Max(1, level - 3);
                        int levelMax = Math.Min(50, level + 3);
                        int levelRand = Util.Random(levelMin, levelMax);
                        long removeTime = GameLoop.GameLoopTime + Util.Random(minRemoveTime, maxRemoveTime);

                        MimicLFGEntry entry = new MimicLFGEntry(MimicManager.GetRandomMimicClass(realm), (byte)levelRand, realm, removeTime);

                        entries.Add(entry);
                    }
                }
            }

            List<MimicLFGEntry> generateList = new List<MimicLFGEntry>();
            generateList.AddRange(entries);

            return generateList;
        }

        private static List<MimicLFGEntry> ValidateList(List<MimicLFGEntry> entries)
        {
            List<MimicLFGEntry> validList = new List<MimicLFGEntry>();

            if (entries.Count != 0)
            {
                foreach (MimicLFGEntry entry in entries)
                {
                    if (GameLoop.GameLoopTime < entry.RemoveTime)
                        validList.Add(entry);
                }
            }

            return validList;
        }

        public class MimicLFGEntry
        {
            public string Name;
            public eGender Gender;
            public eMimicClass MimicClass;
            public byte Level;
            public eRealm Realm;
            public long RemoveTime;
            public bool RefusedGroup;

            public MimicLFGEntry(eMimicClass mimicClass, byte level, eRealm realm, long removeTime)
            {
                Gender = Util.RandomBool() ? eGender.Male : eGender.Female;
                Name = MimicNames.GetName(Gender, realm);
                MimicClass = mimicClass;
                Level = level;
                Realm = realm;
                RemoveTime = removeTime;
            }
        }
    }

    #endregion LFG

    public class SetupMimicsEvent
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        [ScriptLoadedEvent]
        public static void OnScriptsCompiled(DOLEvent e, object sender, EventArgs args)
        {
            if (MimicManager.Initialize())
                log.Info("MimicNPCs Initialized.");
            else
                log.Error("MimicNPCs Failed to Initialize.");
        }
    }

    // Just a quick way to get names...
    public static class MimicNames
    {
        private const string albMaleNames = "Gareth,Lancelot,Cedric,Tristan,Percival,Gawain,Arthur,Merlin,Galahad,Ector,Uther,Mordred,Bors,Lionel,Agravain,Bedivere,Kay,Lamorak,Erec,Gaheris,Pellinore,Loholt,Leodegrance,Aglovale,Tor,Ywain,Uri,Cador,Elayne,Tristram,Cei,Gavain,Kei,Launcelot,Meleri,Isolde,Dindrane,Ragnelle,Lunete,Morgause,Yseult,Bellicent,Brangaine,Blanchefleur,Enid,Vivian,Laudine,Selivant,Lisanor,Ganelon,Cundrie,Guinevere,Norgal,Vivienne,Clarissant,Ettard,Morgaine,Serene,Serien,Selwod,Siraldus,Corbenic,Gurnemanz,Terreban,Malory,Dodinel,Serien,Gurnemanz,Manessen,Herzeleide,Taulat,Serien,Bohort,Ysabele,Karados,Dodinel,Peronell,Dinadan,Segwarides,Lucan,Lamorat,Enide,Parzival,Aelfric,Geraint,Rivalin,Blanchefleur,Gurnemanz,Terreban,Launceor,Clarissant,Herzeleide,Taulat,Zerbino,Serien,Bohort,Ysabele,Dodinel,Peronell,Serenadine,Dinadan,Caradoc,Segwarides,Lucan,Lamorat,Enide,Parzival,Aelfric,Geraint,Rivalin,Blanchefleur,Kaherdin,Gurnemanz,Terreban,Launceor,Clarissant,Patrise,Navarre,Taulat,Iseut,Guivret,Madouc,Ygraine,Tristran,Perceval,Lanzarote,Lamorat,Ysolt,Evaine,Guenever,Elisena,Rowena,Deirdre,Maelis,Clarissant,Palamedes,Yseult,Iseult,Palomides,Brangaine,Laudine,Herlews,Tristram,Alundyne,Blasine,Dinas";
        private const string albFemaleNames = "Guinevere,Isolde,Morgana,Elaine,Vivienne,Nimue,Lynette,Rhiannon,Enid,Iseult,Bellicent,Brangaine,Blanchefleur,Laudine,Selivant,Lisanor,Elidor,Brisen,Linet,Serene,Serien,Selwod,Ysabele,Karados,Peronell,Serenadine,Dinadan,Clarissant,Igraine,Aelfric,Herzeleide,Taulat,Zerbino,Iseut,Guivret,Madouc,Ygraine,Elisena,Rowena,Deirdre,Maelis,Herlews,Alundyne,Blasine,Dinas,Evalach,Rohais,Soredamors,Orguelleuse,Egletine,Fenice,Amide,Lionesse,Eliduc,Silvayne,Amadas,Amadis,Iaonice,Emerause,Ysabeau,Idonia,Alardin,Lessele,Evelake,Herzeleide,Carahes,Elyabel,Igrayne,Laudine,Guenloie,Isolt,Urgan,Yglais,Nimiane,Arabele,Amabel,Clarissant,Patrise,Navarre,Iseut,Guivret,Madouc,Ygraine,Elisena,Rowena,Deirdre,Maelis,Herlews,Alundyne,Blasine,Dinas,Evalach,Rohais,Soredamors,Orguelleuse,Egletine,Fenice,Amide,Lionesse,Eliduc,Silvayne,Amadas,Amadis,Iaonice,Emerause,Ysabeau,Idonia,Alardin,Lessele,Evelake,Herzeleide,Carahes,Elyabel,Igrayne,Laudine,Guenloie,Isolt,Urgan,Yglais,Nimiane,Arabele,Amabel";

        private const string hibMaleNames = "Aonghus,Breandán,Cian,Dallán,Eógan,Fearghal,Gréagóir,Iomhar,Lorcán,Máirtín,Neachtan,Odhrán,Páraic,Ruairí,Seosamh,Toiréasa,Áed,Beircheart,Colm,Domhnall,Éanna,Fergus,Goll,Irial,Liam,MacCon,Naoimhín,Ódhran,Pádraig,Ronán,Seánán,Tadhgán,Úilliam,Ailill,Bran,Cairbre,Daithi,Eoghan,Faolan,Gorm,Iollan,Lughaidh,Manannan,Niall,Oisin,Pádraig,Rónán,Séadna,Tadhg,Ultán,Alastar,Bairre,Caoilte,Dáire,Énna,Fiachra,Gairm,Imleach,Jarlath,Kian,Laoiseach,Malachy,Naoise,Odhrán,Páidín,Roibéard,Seamus,Turlough,Uilleag,Alastriona,Bairrfhionn,Caoimhe,Dymphna,Éabha,Fionnuala,Gráinne,Isolt,Laoise,Máire,Niamh,Oonagh,Pádraigín,Róisín,Saoirse,Teagan,Úna,Aoife,Bríd,Caitríona,Deirdre,Éibhlin,Fia,Gormlaith,Iseult,Jennifer,Kerstin,Léan,Máighréad,Nóirín,Órlaith,Plurabelle,Ríoghnach,Siobhán,Treasa,Úrsula,Aodh,Baird,Caoimhín,Dáire,Éamon,Fearghas,Gartlach,Íomhar,József,Lochlainn,Mánus,Naois,Óisin,Páidín,Roibeárd,Seaán,Tomás,Uilliam,Ailbhe,Bairrionn,Caoilinn,Dairine,Eabhnat,Fearchara,Gormfhlaith,Ite,Juliana,Kaitlín,Laochlann,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann,Ailbín,Bairrionn,Caoimhín,Dairine,Eabhnat,Fearchara,Gormfhlaith,Ite,Juliana,Kaitlín,Laochlann,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann";
        private const string hibFemaleNames = "Aibhlinn,Brighid,Caoilfhionn,Deirdre,Éabha,Fionnuala,Gráinne,Iseult,Jennifer,Kerstin,Léan,Máire,Niamh,Oonagh,Pádraigín,Róisín,Saoirse,Teagan,Úna,Aoife,Aisling,Bláthnat,Clíodhna,Dymphna,Éidín,Fíneachán,Gormfhlaith,Íomhar,Juliana,Kaitlín,Laoise,Máighréad,Nóirín,Órlaith,Plurabelle,Ríoghnach,Siobhán,Treasa,Úrsula,Ailbhe,Bairrfhionn,Caoilinn,Dairine,Éabhnat,Fearchara,Gormlaith,Ite,Laochlann,Máirtín,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann,Ailbín,Ailis,Bláth,Dairín,Éadaoin,Fionn,Grá,Iseabal,Jacinta,Káit,Laoiseach,Máire,Nuala,Órfhlaith,Póilín,Saibh,Téadgh";

        private const string midMaleNames = "Agnar,Bjorn,Dagur,Eirik,Fjolnir,Geir,Haldor,Ivar,Jarl,Kjartan,Leif,Magnus,Njall,Orvar,Ragnald,Sigbjorn,Thrain,Ulf,Vifil,Arni,Bardi,Dain,Einar,Faldan,Grettir,Hogni,Ingvar,Jokul,Koll,Leiknir,Mord,Nikul,Ornolf,Ragnvald,Sigmund,Thorfinn,Ulfar,Vali,Yngvar,Asgeir,Bolli,Darri,Egill,Flosi,Gisli,Hjortur,Ingolf,Jokull,Kolbeinn,Leikur,Mordur,Nils,Orri,Ragnaldur,Sigurdur,Thormundur,Ulfur,Valur,Yngvi,Arnstein,Bardur,David,Egill,Flosi,Gisli,Hjortur,Ingolf,Jokull,Kolbeinn,Leikur,Mordur,Nils,Orri,Ragnaldur,Sigurdur,Thormundur,Ulfur,Valur,Yngvi,Arnstein,Bardur,David,Eik,Fridgeir,Grimur,Hafthor,Ivar,Jorundur,Kari,Ljotur,Mord,Nokkvi,Oddur,Rafn,Steinar,Thorir,Valgard,Yngve,Askur,Baldur,Dagr,Eirikur,Fridleif";
        private const string midFemaleNames = "Aesa,Bjorg,Dalla,Edda,Fjola,Gerd,Halla,Inga,Jora,Kari,Lina,Marna,Njola,Orna,Ragna,Sif,Thora,Ulfhild,Vika,Alva,Bodil,Dagny,Eira,Frida,Gisla,Hildur,Ingibjorg,Jofrid,Kolfinna,Leidr,Mina,Olina,Ragnheid,Sigrid,Thordis,Una,Yrsa,Asgerd,Bergthora,Eilif,Flosa,Gudrid,Hjordis,Ingimund,Jolninna,Lidgerd,Mjoll,Oddny,Ranveig,Sigrun,Thorhalla,Valdis,Alfhild,Bardis,Davida,Eilika,Fridleif,Gudrun,Hjortur,Jokulina,Kolfinna,Leiknir,Mordur,Njall,Orvar,Ragnald,Sigbjorn,Thrain,Ulf,Vifil,Arnstein,Bardur,David,Egill,Fridgeir,Grimur,Hafthor,Ivar,Jorundur,Kari,Ljotur,Mord,Nokkvi,Oddur,Rafn,Steinar,Thorir,Valgard,Yngve,Askur,Baldur,Dagr,Eirikur,Fridleif,Grimur,Halfdan,Ivarr,Kjell,Ljung,Nikul,Ornolf,Ragnvald,Sigurdur,Thormundur,Ulfur,Valur,Yngvi";

        public static string GetName(eGender gender, eRealm realm)
        {
            string[] names = new string[0];

            switch (realm)
            {
                case eRealm.Albion:
                if (gender == eGender.Male)
                    names = albMaleNames.Split(',');
                else
                    names = albFemaleNames.Split(',');
                break;

                case eRealm.Hibernia:
                if (gender == eGender.Male)
                    names = hibMaleNames.Split(',');
                else
                    names = hibFemaleNames.Split(",");
                break;

                case eRealm.Midgard:
                if (gender == eGender.Male)
                    names = midMaleNames.Split(',');
                else
                    names = midFemaleNames.Split(",");
                break;
            }

            int randomIndex = Util.Random(names.Length - 1);

            return names[randomIndex];
        }
    }
}