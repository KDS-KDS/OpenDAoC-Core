using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using DOL.GS.Realm;
using DOL.GS.RealmAbilities;
using DOL.GS.ServerProperties;
using DOL.GS.Spells;
using DOL.GS.Styles;
using DOL.GS.Utils;
using DOL.Language;
using JNogueira.Discord.Webhook.Client;
using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static DOL.GS.AttackData;
using static DOL.GS.GamePlayer;

namespace DOL.GS.Scripts
{
    public class MimicNPC : GameNPC, IGamePlayer
    {
        private DummyPacketLib _dummyLib;
        private DummyClient _dummyClient;

        public AttackComponent AttackComponent { get { return attackComponent; } }
        public RangeAttackComponent RangeAttackComponent { get { return rangeAttackComponent; } }
        public StyleComponent StyleComponent { get { return styleComponent; } }
        public EffectListComponent EffectListComponent { get { return effectListComponent; } }

        public IPacketLib Out { get { return _dummyLib; } }
        public GameClient Client { get { return _dummyClient; } }
        public GamePlayer Dev { private get; set; }

        public void DevOut(string message)
        {
            Dev.Out.SendMessage(message, eChatType.CT_Say, eChatLoc.CL_ChatWindow);
        }

        public bool CanUseSideStyles { get { return StylesSide != null && StylesSide.Count > 0; } }
        public bool CanUseBackStyles { get { return StylesBack != null && StylesBack.Count > 0; } }
        public bool CanUseFrontStyle { get { return StylesFront != null && StylesFront.Count > 0; } }
        public bool CanUseAnytimeStyles { get { return StylesAnytime != null && StylesAnytime.Count > 0; } }
        public bool CanUsePositionalStyles { get { return CanUseSideStyles || CanUseBackStyles; } }
        public bool CanCastInstantCrowdControlSpells { get { return InstantCrowdControlSpells != null && InstantCrowdControlSpells.Count > 0; } }
        public bool CanCastCrowdControlSpells { get { return CrowdControlSpells != null && CrowdControlSpells.Count > 0; } }
        public bool CanCastBolts { get { return BoltSpells != null && BoltSpells.Count > 0; } }

        public List<Style> StylesTaunt { get; protected set; } = null;
        public List<Style> StylesDetaunt { get; protected set; } = null;
        public List<Style> StylesShield { get; protected set; } = null;

        public override int InteractDistance => WorldMgr.VISIBILITY_DISTANCE;

        /// <summary>
		/// Instant Crowd Controll spell list and accessor
		/// </summary>
		public List<Spell> InstantCrowdControlSpells { get; set; } = null;

        /// <summary>
		/// Crowd Controll spell list and accessor
		/// </summary>
        public List<Spell> CrowdControlSpells { get; set; } = null;

        /// <summary>
		/// Bolt spell list and accessor
		/// </summary>
        public List<Spell> BoltSpells { get; set; } = null;

        public MimicSpec MimicSpec = new MimicSpec();
        public int Kills;

        private MimicBrain m_mimicBrain;

        public MimicBrain MimicBrain
        {
            get { return m_mimicBrain; }
            set { m_mimicBrain = value; }
        }

        private int m_leftOverSpecPoints = 0;

        public MimicNPC(eMimicClass cClass, byte level, eGender gender = eGender.Neutral, eSpecType spec = eSpecType.None)
        {
            _dummyClient = new DummyClient(GameServer.Instance);
            _dummyLib = new DummyPacketLib();

            Inventory = new MimicNPCInventory();
            MaxSpeedBase = PLAYER_BASE_SPEED;

            MimicSpec = MimicSpec.GetSpec(cClass, spec);
            SetCharacterClass((int)cClass);
            SetRaceAndName();
            SetBrain(cClass);
            SetLevel(level);

            SetWeapons();
            SetShield();
            SetRanged();
            SetArmor();
            SetJewelry();
            RefreshItemBonuses();
            IsCloakHoodUp = Util.RandomBool();

            Health = MaxHealth;
            Endurance = MaxEndurance;
            Mana = MaxMana;

            RespawnInterval = -1;

            m_combatTimer = new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(_ =>
            {
                return 0;
            }));

            Dev = ClientService.GetPlayers()[0];
        }

        private void SetBrain(eMimicClass mimicClass)
        {
            switch (mimicClass)
            {
                case eMimicClass.Infiltrator:
                case eMimicClass.Nightshade:
                case eMimicClass.Shadowblade:
                MimicBrain = new AssassinBrain();
                break;

                case eMimicClass.Scout:
                case eMimicClass.Ranger:
                case eMimicClass.Hunter:
                MimicBrain = new ArcherBrain();
                break;

                default:
                MimicBrain = new MimicBrain();
                break;
            }
            
            MimicBrain.MimicBody = this;
            SetOwnBrain(MimicBrain);
        }

        private void SetWeapons()
        {
            switch (MimicSpec.SpecType)
            {
                case eSpecType.DualWield:
                case eSpecType.DualWieldAndShield:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.leftHand);
                break;

                case eSpecType.LeftAxe:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.twoHand);
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponTwoType, eHand.leftHand);
                break;

                case eSpecType.OneHandAndShield:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                break;

                case eSpecType.OneHandHybrid:
                case eSpecType.TwoHandHybrid:
                case eSpecType.TwoHanded when CharacterClass.ID != (int)eCharacterClass.Valewalker:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponTwoType, eHand.twoHand, MimicSpec.DamageType);
                break;

                case eSpecType.Mid:
                case eSpecType.PacHealer:
                case eSpecType.AugHealer:
                case eSpecType.MendHealer:
                case eSpecType.MendShaman:
                case eSpecType.AugShaman:
                case eSpecType.SubtShaman:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.twoHand);
                break;

                case eSpecType.Instrument:
                MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                MimicEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.TwoHandWeapon, eInstrumentType.Flute);
                MimicEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.DistanceWeapon, eInstrumentType.Drum);
                MimicEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.FirstEmptyBackpack, eInstrumentType.Lute);
                break;

                default:
                if (CharacterClass.ClassType == eClassType.ListCaster ||
                    CharacterClass.ID == (int)eCharacterClass.Friar ||
                    CharacterClass.ID == (int)eCharacterClass.Valewalker)
                    MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.twoHand);
                else if (CharacterClass.ID != (int)eCharacterClass.Hunter)
                    MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                else
                    MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.twoHand);

                if (MimicSpec.WeaponOneType == eObjectType.Sword)
                    MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponOneType, eHand.oneHand);
                break;
            }

            if (MimicSpec.Is2H)
                SwitchWeapon(eActiveWeaponSlot.TwoHanded);
            else
                SwitchWeapon(eActiveWeaponSlot.Standard);
        }

        private void SetRanged()
        {
            foreach (Ability ability in GetAllAbilities())
            {
                switch (ability.KeyName)
                {
                    case GS.Abilities.Weapon_Thrown: MimicEquipment.SetRangedWeapon(this, eObjectType.Thrown); break;
                    case GS.Abilities.Weapon_Shortbows: MimicEquipment.SetRangedWeapon(this, eObjectType.Fired); break;
                    case GS.Abilities.Weapon_Crossbow: MimicEquipment.SetRangedWeapon(this, eObjectType.Crossbow); break;
                    case GS.Abilities.Weapon_RecurvedBows: MimicEquipment.SetRangedWeapon(this, eObjectType.RecurvedBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case GS.Abilities.Weapon_Longbows: MimicEquipment.SetRangedWeapon(this, eObjectType.Longbow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case GS.Abilities.Weapon_CompositeBows: MimicEquipment.SetRangedWeapon(this, eObjectType.CompositeBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                }
            }
        }

        private void SetJewelry()
        {
            MimicEquipment.SetJewelryROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Magical);
        }

        private void SetShield()
        {
            MimicEquipment.SetShield(this, BestShieldLevel);
        }

        private void SetArmor()
        {
            int armorLevel = BestArmorLevel;
            eObjectType armorType = eObjectType.GenericArmor;

            switch (armorLevel)
            {
                case 1: armorType = eObjectType.Cloth; break;
                case 2: armorType = eObjectType.Leather; break;

                case 3:
                {
                    if (Realm == eRealm.Hibernia)
                        armorType = eObjectType.Reinforced;
                    else
                        armorType = eObjectType.Studded;
                    break;
                }

                case 4:
                {
                    if (Realm == eRealm.Hibernia)
                        armorType = eObjectType.Scale;
                    else
                        armorType = eObjectType.Chain;
                    break;
                }

                case 5: armorType = eObjectType.Plate; break;
            }

            MimicEquipment.SetArmor(this, armorType);
        }

        public override bool Interact(GamePlayer player)
        {
            if (this.InCombat || !base.Interact(player))
                return false;

            player.Out.SendMessage(
                "---------------------------------------\n" +
                "[State] [Prevent Combat] [Brain] [Debug]\n\n " +
                "[Group] - [Leader] - [MainPuller] - [MainCC] - [MainTank] - [MainAssist]\n\n " +
                "[Guard] [Protect] [Intercept]\n\n " +
                "[Spells] - [Inst Harmful] - [Harmful Spells] - [Inst Misc] - [Misc Spells] - [Inst Heal] - [Heal Spells] - [CC]\n\n " +
                "[Styles] - [Abilities]\n\n " +
                "[Spec] - [Stats] - [Ability Effects] - [Spell Effects]\n\n " +
                "[Inventory]\n\n" +
                "[Hood] [RightHand] [LeftHand] [TwoHand] [Ranged] [Helm] [Torso] [Legs] [Arms] [Hands] [Boots] [Jewelry]\n\n " +
                "[Delete]", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string str)
        {
            if (!base.WhisperReceive(source, str))
                return false;

            GamePlayer player = source as GamePlayer;

            if (player == null)
                return false;

            string message = string.Empty;
            int itemIndex;

            if (int.TryParse(str, out itemIndex) )
            {
                itemIndex += (int)eInventorySlot.FirstBackpack - 1;

                if (itemIndex < (int)eInventorySlot.LastBackpack)
                {
                    DbInventoryItem item = Inventory.GetItem((eInventorySlot)itemIndex);

                    if (item != null && RemoveAndAddItemToEmptyPlayerInventorySlot(item, player))
                        return true;
                }
            }

            switch (str)
            {
                case "Debug":
                {
                    MimicBrain.Debug = !MimicBrain.Debug;

                    message = "Debug is " + MimicBrain.Debug;
                    break;
                }

                case "Brain":
                MimicBrain newBrain = new MimicBrain();
                newBrain.MimicBody = this;
                SetOwnBrain(newBrain);
                break;

                case "State":
                {
                    message = Brain.FSM.GetCurrentState().ToString() + "\n";
                    message += "IsSitting: " + IsSitting + "\n";
                    message += "IsMezzed: " + IsMezzed + "\n";
                    message += "IsStunned: " + IsStunned + "\n";
                    message += "IsRooted: " + IsRooted + "\n";
                    break;
                }

                case "Prevent Combat": MimicBrain.PreventCombat = !MimicBrain.PreventCombat; break;

                case "Group":
                {
                    if (Group != null && Group.GetMembersInTheGroup().Count < 8)
                    {
                        Group.AddMember(player);
                        break;
                    }

                    if (player.Group == null)
                    {
                        player.Group = new Group(player);
                        player.Group.AddMember(player);
                    }
                    else
                    {
                        if (player.Group.GetMembersInTheGroup().Contains(this) || player.Group.MemberCount > 8)
                            break;
                    }

                    player.Group.AddMember(this);
                    MimicBrain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER);
                }
                break;

                case "Leader": Group?.MimicGroup.SetLeader(this); break;
                case "MainPuller": Group?.MimicGroup.SetMainPuller(this); break;
                case "MainCC": Group?.MimicGroup.SetMainCC(this); break;
                case "MainTank": Group?.MimicGroup.SetMainTank(this); break;
                case "MainAssist": Group?.MimicGroup.SetMainAssist(this); break;

                case "Guard":
                {
                    if (!HasAbility("Guard"))
                    {
                        message = "I do not have that ability.";
                        break;
                    }

                    if (Group == null || Group != null && !Group.IsInTheGroup(player))
                    {
                        message = "We must be in the same group.";
                        break;
                    }

                    if (MimicBrain.SetGuard(player, out bool foundOurEffect))
                        message = "I will guard you.";
                    else
                    {
                        if (foundOurEffect)
                            message = "I will no longer guard you.";
                        else
                            message = "I cannot guard you.";
                    }

                    break;
                }

                case "Protect":
                    {
                        if (!HasAbility("Protect"))
                        {
                            message = "I do not have that ability.";
                            break;
                        }

                        if (Group == null || Group != null && !Group.IsInTheGroup(player))
                        {
                            message = "We must be in the same group.";
                            break;
                        }

                        if (MimicBrain.SetProtect(player, out bool foundOurEffect))
                            message = "I will protect you.";
                        else
                        {
                            if (foundOurEffect)
                                message = "I will no longer protect you.";
                            else
                                message = "I cannot protect you.";
                        }

                        break;
                    }

                case "Intercept":
                    {
                        if (!HasAbility("Intercept"))
                        {
                            message = "I do not have that ability.";
                            break;
                        }

                        if (Group == null || Group != null && !Group.IsInTheGroup(player))
                        {
                            message = "We must be in the same group.";
                            break;
                        }

                        if (MimicBrain.SetIntercept(player, out bool foundOurEffect))
                            message = "I will intercept for you.";
                        else
                        {
                            if (foundOurEffect)
                                message = "I will no longer intercept for you.";
                            else
                                message = "I cannot intercept for you.";
                        }

                        break;
                    }

                case "Spells":
                {
                    foreach (Spell spell in Spells)
                        message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";

                    break;
                }

                case "Inst Harmful":
                {
                    if (CanCastInstantHarmfulSpells)
                    {
                        foreach (Spell spell in InstantHarmfulSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Harmful Spells":
                {
                    if (CanCastHarmfulSpells)
                    {
                        foreach (Spell spell in HarmfulSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Inst Misc":
                {
                    if (CanCastInstantMiscSpells)
                    {
                        foreach (Spell spell in InstantMiscSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Misc Spells":
                {
                    if (CanCastMiscSpells)
                    {
                        foreach (Spell spell in MiscSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Inst Heal":
                {
                    if (CanCastInstantHealSpells)
                    {
                        foreach (Spell spell in InstantHealSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Heal Spells":
                {
                    if (CanCastHealSpells)
                    {
                        foreach (Spell spell in HealSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "CC":
                {
                    if (CanCastCrowdControlSpells)
                    {
                        foreach (Spell spell in CrowdControlSpells)
                            message += spell.Name + " " + spell.Level + " " + spell.SpellType + "\n";
                    }

                    break;
                }

                case "Styles":
                {
                    foreach (Style style in Styles)
                        message += style.Name + " " + style.Level + "\n";

                    break;
                }

                case "Abilities":
                {
                    foreach (Ability ability in GetAllAbilities())
                        message += ability.Name + " " + ability.Level + "\n";

                    break;
                }

                case "Ability Effects":
                {
                    List<ECSGameAbilityEffect> abilityEffects = player.EffectListComponent.GetAbilityEffects();

                    foreach (ECSGameAbilityEffect abilityEffect in abilityEffects)
                    {
                        if (abilityEffect != null)
                            message += abilityEffect?.Name + "\n";
                    }

                    if (message == string.Empty)
                        message = "No active ability effects.";

                    break;
                }

                case "Spell Effects":
                {
                    foreach (ECSGameSpellEffect spellEffect in EffectListComponent.GetSpellEffects())
                    {
                        if (spellEffect != null)
                            message += spellEffect.Name + " " + spellEffect.EffectType + "\n";
                    }

                    if (message == string.Empty)
                        message = "No active spell effects.";

                    break;
                }

                case "Stats":
                {
                    message = string.Format("Level: {0}\n Str: {1}\n Con: {2}\n Dex: {3}\n Qui: {4}\n Int: {5}\n Pie: {6}\n Emp: {7}\n Cha: {8}\n" +
                                            " HP: {9}\n AF: {10}\n, End: {11}\n Power: {12}\n",
                                            Level,
                                            Strength,
                                            Constitution,
                                            Dexterity,
                                            Quickness,
                                            Intelligence,
                                            Piety,
                                            Empathy,
                                            Charisma,
                                            Health,
                                            EffectiveOverallAF,
                                            Endurance,
                                            Mana);

                    break;
                }

                case "Spec":
                {
                    var specs = GetSpecList();

                    foreach (Specialization spec in specs)
                        message += spec.Name + ": " + spec.Level + " \n";

                    break;
                }

                case "Hood": IsCloakHoodUp = !IsCloakHoodUp; break;

                case "Inventory":
                {
                    ICollection<DbInventoryItem> items = Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

                    if (items != null && items.Any())
                    {
                        int index = 1;
                        foreach (DbInventoryItem item in items)
                            message += "[" + index + "]" + ". " + item.Name;
                    }
                    else
                        message = "Inventory is empty";

                    break;
                }

                case "Helm": RemoveItem(eInventorySlot.HeadArmor, player); break;
                case "Torso": RemoveItem(eInventorySlot.TorsoArmor, player); break;
                case "Legs": RemoveItem(eInventorySlot.LegsArmor, player); break;
                case "Arms": RemoveItem(eInventorySlot.ArmsArmor, player); break;
                case "Hands": RemoveItem(eInventorySlot.HandsArmor, player); break;
                case "Boots": RemoveItem(eInventorySlot.FeetArmor, player); break;
                case "RightHand": RemoveItem(eInventorySlot.RightHandWeapon, player); break;
                case "LeftHand": RemoveItem(eInventorySlot.LeftHandWeapon, player); break;
                case "TwoHand": RemoveItem(eInventorySlot.TwoHandWeapon, player); break;
                case "Ranged": RemoveItem(eInventorySlot.DistanceWeapon, player); break;

                case "Jewelry":
                {
                    for (int i = Slot.JEWELRY; i <= Slot.RIGHTRING; i++)
                    {
                        if (i is Slot.TORSO or Slot.LEGS or Slot.ARMS or Slot.FOREARMS or Slot.SHIELD)
                            continue;

                        RemoveItem((eInventorySlot)i, player);
                    }

                    break;
                }

                case "Delete":
                {
                    if (ControlledBrain != null)
                    {
                        if (ControlledBrain.Body != null)
                            ControlledBrain.Body.Delete();
                    }

                    Delete();

                    break;
                }

                default: break;
            }

            if (message != string.Empty)
                player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_PopupWindow);

            return true;
        }

        public override bool SayReceive(GameLiving source, string str)
        {
            if (source == null || str == null)
                return false;

            if (Group != null && Group.IsInTheGroup(source))
            {
                str = str.ToLower();

                switch (str)
                {
                    case "stay": Brain.FSM.SetCurrentState(eFSMStateType.IDLE); break;
                    case "follow": Brain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER); break;
                    case "camp": Brain.FSM.SetCurrentState(eFSMStateType.CAMP); break;
                    case "reset": Brain.FSM.SetCurrentState(eFSMStateType.WAKING_UP); break;
                }
            }

            return base.SayReceive(source, str);
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source == null || item == null)
                return false;

            GamePlayer player = source as GamePlayer;

            // TODO: Add group checks when done testing
            //if ((Group == null || Group != null && !Group.IsInTheGroup(source)) && player.Client.Account.PrivLevel == 1)
            //    return false;

            bool equipItem = false;

            foreach (eEquipmentItems equipmentItem in Enum.GetValues(typeof(eEquipmentItems)))
            {
                if (item.Item_Type == (int)equipmentItem)
                {
                    equipItem = true;
                    break;
                }
            }

            if (equipItem)
                return EquipRecievedItem(item, player);


            return base.ReceiveItem(source, item);
        }

        private bool EquipRecievedItem(DbInventoryItem itemToEquip, GamePlayer player)
        {
            if (!HasAbilityToUseItem(itemToEquip.Template))
                return false;

            eInventorySlot slotToEquip = (eInventorySlot)itemToEquip.Item_Type;

            if (slotToEquip == eInventorySlot.LeftHandWeapon && itemToEquip.Object_Type != (int)eObjectType.Shield)
            {
                if (Inventory.GetItem(eInventorySlot.RightHandWeapon) == null)
                    slotToEquip = eInventorySlot.RightHandWeapon;
                else if (!CharacterClass.CanUseLefthandedWeapon)
                    return false;
            }

            DbInventoryItem equippedItem = Inventory.GetItem(slotToEquip);

            if (equippedItem != null && !RemoveAndAddItemToEmptyPlayerInventorySlot(equippedItem, player))
                return false;

            player.Inventory.RemoveItem(itemToEquip);

            if (Inventory.AddItem(slotToEquip, itemToEquip))
            {
                RefreshItemBonuses();
                UpdateNPCEquipmentAppearance();

                return true;
            }
            else
                player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), itemToEquip);

            return false;
        }

        private void RemoveItem(eInventorySlot inventorySlot, GamePlayer player)
        {
            DbInventoryItem itemToRemove = Inventory.GetItem(inventorySlot);

            if (itemToRemove != null)
                RemoveAndAddItemToEmptyPlayerInventorySlot(itemToRemove, player);
            else
                player.Out.SendMessage("There is nothing equipped in that slot.", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
        }

        private bool RemoveAndAddItemToEmptyPlayerInventorySlot(DbInventoryItem itemToAdd, GamePlayer player)
        {
            eInventorySlot validSlot = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

            if (validSlot != eInventorySlot.Invalid)
            {
                Inventory.RemoveItem(itemToAdd);
                player.Inventory.AddItem(validSlot, itemToAdd);
            }
            else
            {
                player.Out.SendMessage("No room in your backpack.", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
                return false;
            }

            UpdateNPCEquipmentAppearance();

            return true;
        }

        public void SpendSpecPoints(byte level, byte previousLevel)
        {
            MimicSpec.SpecLines = MimicSpec.SpecLines.OrderByDescending(ratio => ratio.levelRatio).ToList();

            int leftOverSpecPoints = m_leftOverSpecPoints;
            bool spentPoints = true;
            bool spendLeftOverPoints = false;
            bool mimicCreation = level - previousLevel > 1;

            byte index = (byte)(previousLevel + 1);

            // 40+ half-level
            if (level == previousLevel)
                index = level;

            // For each level, get the points available. In the while loop, spend points according to the ratio in SpecLines until spentPoints is false.
            // Then set spendLeftOverPoints true and spend any left over points until spentPoints is false again. Exit the while loop, increase level, and repeat.
            for (byte i = index; i <= Level; i++)
            {
                spentPoints = true;
                spendLeftOverPoints = false;

                int totalSpecPointsThisLevel = GetSpecPointsForLevel(i, mimicCreation) + leftOverSpecPoints;

                while (spentPoints)
                {
                    spentPoints = false;

                    foreach (SpecLine specLine in MimicSpec.SpecLines)
                    {
                        // Indicates a dump stat for lvl 50
                        if (specLine.levelRatio <= 0 && Level < 50)
                            continue;

                        Specialization spec = GetSpecializationByName(specLine.Spec);

                        if (spec != null)
                        {
                            if (spec.Level < specLine.SpecCap && spec.Level < i)
                            {
                                int specRatio = (int)(i * specLine.levelRatio);

                                if (!spendLeftOverPoints && spec.Level >= specRatio)
                                    continue;

                                int totalCost = spec.Level + 1;

                                if (totalSpecPointsThisLevel >= totalCost)
                                {
                                    totalSpecPointsThisLevel -= totalCost;
                                    spec.Level++;
                                    spentPoints = true;
                                }
                            }
                        }
                    }

                    if (!spentPoints && !spendLeftOverPoints)
                    {
                        spendLeftOverPoints = true;
                        spentPoints = true;
                    }
                }

                m_leftOverSpecPoints = leftOverSpecPoints = totalSpecPointsThisLevel;
            }
        }

        private int GetSpecPointsForLevel(int level, bool mimicCreation)
        {
            //    // calc spec points player have (autotrain is not anymore processed here - 1.87 livelike)
            //    int usedpoints = 0;
            //        foreach (Specialization spec in GetSpecList().Where(e => e.Trainable))
            //        {
            //            usedpoints += (spec.Level* (spec.Level + 1) - 2) / 2;
            //            usedpoints -= GetAutoTrainPoints(spec, 0);
            //}

            if (IsLevelSecondStage)
                return CharacterClass.SpecPointsMultiplier * level / 20;

            int specpoints = 0;

            if (mimicCreation)
                if (level > 40)
                    specpoints += CharacterClass.SpecPointsMultiplier * (level - 1) / 20;

            if (level > 5)
                specpoints += CharacterClass.SpecPointsMultiplier * level / 10;
            else if (level >= 2)
                specpoints = level;

            return specpoints;
        }

        #region Spells

        public override void OnCastSpellLosCheckFail(GameObject target)
        {
            // Try to move into LOS so we don't get stuck chain casting in place
            if (target is GameLiving)
                WalkTo(new(target.X, target.Y, target.Z), MaxSpeed);

            base.OnCastSpellLosCheckFail(target);
        }

        public void SetSpells()
        {
            List<Spell> spells = new List<Spell>();

            List<Tuple<Skill, Skill>> usableSkills = GetAllUsableSkills();

            for (int i = 0; i < usableSkills.Count; i++)
            {
                Skill skill = usableSkills[i].Item1;

                if (skill is Spell)
                    spells.Add((Spell)skill);
            }

            if (spells.Count > 0)
                Spells = spells;
        }

        public void SetCasterSpells()
        {
            IList<Specialization> specs = GetSpecList();
            List<Spell> spells = new List<Spell>();

            foreach (Specialization spec in specs)
            {
                var dict = GetAllUsableListSpells();

                if (dict != null && dict.Count > 0)
                {
                    foreach (Tuple<SpellLine, List<Skill>> tuple in dict)
                    {
                        if (tuple.Item2.Count > 0)
                        {
                            foreach (Skill skill in tuple.Item2)
                            {
                                if (skill is Spell)
                                {
                                    Spell spell = skill as Spell;

                                    if (!spells.Contains(spell))
                                        spells.Add(spell);
                                }
                            }
                        }
                    }
                }
            }

            List<Spell> highestSpellLevels = GetHighestLevelSpells(spells);

            if (highestSpellLevels.Count > 0)
                Spells = highestSpellLevels;
        }

        public List<Spell> GetHighestLevelSpells(List<Spell> spells)
        {
            spells = spells.OrderByDescending(spell => spell.Level).ToList();

            List<Spell> highestLevelSpells = new List<Spell>();

            foreach (Spell currentSpell in spells)
            {
                Spell existingSpell = highestLevelSpells.FirstOrDefault(x => AreSpellsEqual(x, currentSpell));

                if (existingSpell != null)
                {
                    if (existingSpell.Level < currentSpell.Level)
                        highestLevelSpells.Remove(existingSpell);
                    else
                        continue;
                }

                highestLevelSpells.Add(currentSpell);
            }

            return highestLevelSpells;
        }

        private bool AreSpellsEqual(Spell spellOne, Spell spellTwo)
        {
            bool equalSpells = spellOne.DamageType == spellTwo.DamageType &&
                               spellOne.SpellType == spellTwo.SpellType &&
                               spellOne.Frequency == spellTwo.Frequency &&
                               spellOne.CastTime == spellTwo.CastTime &&
                               spellOne.Target == spellTwo.Target &&
                               spellOne.Group == spellTwo.Group &&
                               spellOne.IsPBAoE == spellTwo.IsPBAoE &&
                            ((!spellOne.IsAoE && !spellTwo.IsAoE) || (spellOne.IsAoE && spellTwo.IsAoE));

            return equalSpells ? true : HandleSpecificSpells(spellOne, spellTwo);
        }

        private bool HandleSpecificSpells(Spell spellOne, Spell spellTwo)
        {
            // Valewalker proc
            if (spellOne.ID == 11236 && (spellTwo.ID >= 11232 && spellTwo.ID <= 11235) ||
                spellTwo.ID == 11236 && (spellOne.ID >= 11232 && spellOne.ID <= 11235))
                return true;

            return false;
        }

        private List<Spell> m_spells = [];

        /// <summary>
		/// property of spell array of NPC
		/// </summary>
		public override List<Spell> Spells
        {
            get => m_spells;
            set
            {
                if (value == null || value.Count < 1)
                {
                    m_spells.Clear();
                    InstantHarmfulSpells = null;
                    HarmfulSpells = null;
                    InstantHealSpells = null;
                    HealSpells = null;
                    BoltSpells = null;
                    InstantCrowdControlSpells = null;
                    CrowdControlSpells = null;
                    InstantMiscSpells = null;
                    MiscSpells = null;
                }
                else
                {
                    // Voluntary copy. This isn't ideal and needs to be changed eventually.
                    m_spells = value.ToList();
                    SortSpells();
                }
            }
        }

        /// <summary>Calculate actual spell power cost, while minimizing calls to MaxMana property calculator</summary>
        /// <param name="spell"></param>
        /// <returns>Actual power cost</returns>
        public int PowerCost(Spell spell)
        {
            // Copied from SpellHandler, leaving code for future classes commented out

            int powerCost = spell.Power;

            // Warlock.
            /* GameSpellEffect effect = SpellHandler.FindEffectOnTarget(m_caster, "Powerless");
			if (effect != null && !m_spell.IsPrimary)
				return 0;*/

            /* Valkyrie
            // 1.108 - Valhalla's Blessing now has a 75% chance to not use power.
            if (m_caster.EffectList.GetOfType<ValhallasBlessingEffect>() != null && Util.Chance(75))
                return 0;
            */

            /* Animist
            // Patch 1.108 increases the chance to not use power to 50%.
            if (m_caster.EffectList.GetOfType<FungalUnionEffect>() != null && Util.Chance(50))
                return 0;
            */

            /* ToA
            // Arcane Syphon.
            int syphon = Caster.GetModified(eProperty.ArcaneSyphon);
            if (syphon > 0 && Util.Chance(syphon))
                return 0;
            */

            // Percent of max power if less than zero.
            if (powerCost < 0)
            {
                if (CharacterClass.ManaStat is not eStat.UNDEFINED)
                    powerCost = (int)(CalculateMaxMana(Level, GetBaseStat(CharacterClass.ManaStat)) * powerCost * -0.01);
                else
                    powerCost = (int)(MaxMana * powerCost * -0.01);
            }

            /* Mimics currently use the mob spell line, which SpecToFocus() doesn't recognize
            if (playerCaster != null && playerCaster.CharacterClass.FocusCaster)
            {
                eProperty focusProp = SkillBase.SpecToFocus(SpellLine.Spec);

                if (focusProp is not eProperty.Undefined)
                {
                    double focusBonus = Caster.GetModified(focusProp) * 0.4;

                    if (Spell.Level > 0)
                        focusBonus /= Spell.Level;

                    if (focusBonus > 0.4)
                        focusBonus = 0.4;
                    else if (focusBonus < 0)
                        focusBonus = 0;

                    focusBonus *= Math.Min(1, playerCaster.GetModifiedSpecLevel(SpellLine.Spec) / (double)Spell.Level);
                    powerCost *= 1.2 - focusBonus; // Between 120% and 80% of base power cost.
                }
            } */

            /* Healing doesn't use quickcast, and it's not really worth it just for mentalists
            // Doubled power usage if using QuickCast.
            if (EffectListService.GetAbilityEffectOnTarget(this, eEffect.QuickCast) != null && spell.CastTime > 0)
                powerCost *= 2;
            */

            return powerCost;
        }

        ///<summary>Calculate heal amount, accounting for percentage based heals</summary>
        public static double HealAmount(Spell spell, GameLiving target)
        {
            return spell.Value >= 0
                ? spell.Value
                : target.MaxHealth * spell.Value * -0.01d;
        }

        public Spell HealBig { get; protected set; } = null;
        public Spell HealEfficient { get; protected set; } = null;
        public Spell HealGroup { get; protected set; } = null;
        public Spell HealInstant { get; protected set; } = null;
        public Spell HealInstantGroup { get; protected set; } = null;
        public Spell HealOverTime { get; protected set; } = null;
        public Spell HealOverTimeGroup { get; protected set; } = null;
        public Spell HealOverTimeInstant { get; protected set; } = null;
        public Spell HealOverTimeInstantGroup { get; protected set; } = null;
        public Spell CureMezz { get; protected set; } = null;
        public Spell CureDisease { get; protected set; } = null;
        public Spell CureDiseaseGroup { get; protected set; } = null;
        public Spell CurePoison { get; protected set; } = null;
        public Spell CurePoisonGroup { get; protected set; } = null;

        public override void SortSpells()
        {
            if (Spells.Count < 1)
                return;

            // Clear the lists
            InstantHarmfulSpells?.Clear();
            HarmfulSpells?.Clear();
            InstantHealSpells?.Clear();
            HealSpells?.Clear();
            InstantCrowdControlSpells?.Clear();
            CrowdControlSpells?.Clear();
            BoltSpells?.Clear();
            InstantMiscSpells?.Clear();
            MiscSpells?.Clear();

            HealBig = null;
            HealEfficient = null;
            HealGroup = null;
            HealInstant = null;
            HealInstantGroup = null;
            HealOverTime = null;
            HealOverTimeGroup = null;
            HealOverTimeInstant = null;
            HealOverTimeInstantGroup = null;
            CureMezz = null;
            CureDisease = null;
            CureDiseaseGroup = null;
            CurePoison = null;
            CurePoisonGroup = null;

            // Sort spells into lists
            foreach (Spell spell in m_spells)
            {
                if (spell == null)
                    continue;

                if (spell.SpellType == eSpellType.Bolt)
                {
                    if (BoltSpells == null)
                        BoltSpells = new List<Spell>(1);

                    BoltSpells.Add(spell);
                }
                else if (spell.SpellType == eSpellType.Mesmerize ||
                        (spell.SpellType == eSpellType.SpeedDecrease && spell.Value >= 99))
                {
                    //if (spell.IsInstantCast)
                    //{
                    //    if (InstantCrowdControlSpells == null)
                    //        InstantCrowdControlSpells = new List<Spell>(1);
                    //    InstantCrowdControlSpells.Add(spell);
                    //}
                    //else
                    //{
                    if (CrowdControlSpells == null)
                        CrowdControlSpells = new List<Spell>(1);

                    CrowdControlSpells.Add(spell);
                    //}
                }
                else if (spell.IsHarmful)
                {
                    if (spell.IsInstantCast)
                    {
                        if (InstantHarmfulSpells == null)
                            InstantHarmfulSpells = new List<Spell>(1);

                        InstantHarmfulSpells.Add(spell);
                    }
                    else
                    {
                        if (HarmfulSpells == null)
                            HarmfulSpells = new List<Spell>(1);

                        HarmfulSpells.Add(spell);
                    }
                }
                else if (spell.IsHealing && !spell.IsPulsing && !spell.IsConcentration)
                {
                    // TODO: Move pet heals somewhere else
                    if (spell.Target == eSpellTarget.PET)
                        continue;

                    double valueNew = HealAmount(spell, this);

                    if (spell.SpellType == eSpellType.CureMezz)
                        CureMezz = spell;
                    else if (spell.SpellType == eSpellType.CureDisease)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CureDiseaseGroup == null || spell.Duration > CureDiseaseGroup.Duration))
                            CureDiseaseGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CureDisease == null || spell.Duration > CureDisease.Duration))
                            CureDisease = spell;
                    }
                    else if (spell.SpellType == eSpellType.CurePoison)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CurePoisonGroup == null || spell.Duration > CurePoisonGroup.Duration))
                            CurePoisonGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CurePoison == null || spell.Duration > CurePoison.Duration))
                            CurePoison = spell;
                    }
                    else if (spell.IsInstantCast)
                    {
                        if (InstantHealSpells == null)
                            InstantHealSpells = new List<Spell>(1);

                        InstantHealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            // Valks have both regens and hots, so we need to compare actual healing over time in combat
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeInstantGroup == null)
                                    HealOverTimeInstantGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstantGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstantGroup, this) / HealOverTimeInstantGroup.Frequency
                                        : HealAmount(HealOverTimeInstantGroup, this) / HealthRegenerationPeriod / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HealthRegenerationPeriod / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstantGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTimeInstant == null)
                                    HealOverTimeInstant = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstant.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstant, this) / HealOverTimeInstant.Frequency
                                        : HealAmount(HealOverTimeInstant, this) / HealthRegenerationPeriod / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HealthRegenerationPeriod / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstant = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealInstantGroup == null || valueNew > HealAmount(HealInstantGroup, this))
                                    HealInstantGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealInstant == null || valueNew > HealAmount(HealInstant, this))
                                    HealInstant = spell;
                            }
                        }
                    }
                    else
                    {
                        if (HealSpells == null)
                            HealSpells = new List<Spell>(1);

                        HealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            // Valks have both regens and hots, so we need to compare actual healing over time in combat
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeGroup == null)
                                    HealOverTimeGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeGroup, this) / HealOverTimeGroup.Frequency
                                        : HealAmount(HealOverTimeGroup, this) / HealthRegenerationPeriod / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HealthRegenerationPeriod / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTime == null)
                                    HealOverTime = spell;
                                else
                                {
                                    double perSecondOld = HealOverTime.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTime, this) / HealOverTime.Frequency
                                        : HealAmount(HealOverTime, this) / HealthRegenerationPeriod / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HealthRegenerationPeriod / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTime = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealGroup == null || valueNew / spell.CastTime > HealAmount(HealGroup, this) / HealGroup.CastTime)
                                    HealGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealEfficient == null)
                                    HealEfficient = spell;
                                else
                                {
                                    double perSecondNew = valueNew / spell.CastTime;
                                    double perSecondEff = HealAmount(HealEfficient, this) / HealEfficient.CastTime;
                                    double perSecondBig = HealBig == null ? 0.0 : HealAmount(HealBig, this) / HealBig.CastTime;

                                    // Add a small margin for new spell efficiency, so we use bigger spec heals when they're almost as efficient
                                    double effNew = valueNew / PowerCost(spell) + 0.25;
                                    double effOld = HealAmount(HealEfficient, this) / PowerCost(HealEfficient);

                                    if (effNew > effOld)
                                    {
                                        if (perSecondEff > perSecondNew && perSecondEff > perSecondBig)
                                            HealBig = HealEfficient;

                                        HealEfficient = spell;
                                    }
                                    else if (perSecondNew > perSecondEff && perSecondNew > perSecondBig)
                                        HealBig = spell;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Skald speed is instant, but instant misc isn't checked outside combat. Add an exception.
                    if (spell.IsInstantCast && spell.SpellType != eSpellType.SpeedEnhancement)
                    {
                        if (InstantMiscSpells == null)
                            InstantMiscSpells = new List<Spell>(1);

                        InstantMiscSpells.Add(spell);
                    }
                    else
                    {
                        if (MiscSpells == null)
                            MiscSpells = new List<Spell>(1);

                        MiscSpells.Add(spell);
                    }
                }
            }
        }

        #endregion

        public override void SortStyles()
        {
            StylesChain?.Clear();
            StylesDefensive?.Clear();
            StylesBack?.Clear();
            StylesSide?.Clear();
            StylesFront?.Clear();
            StylesAnytime?.Clear();
            StylesTaunt?.Clear();
            StylesDetaunt?.Clear();
            StylesShield?.Clear();

            if (Styles == null)
                return;

            foreach (Style s in Styles)
            {
                if (s == null)
                    continue;

                if (s.WeaponTypeRequirement != (int)eObjectType.Shield ||
                    s.WeaponTypeRequirement == (int)eObjectType.Shield && s.OpeningRequirementType == Style.eOpening.Defensive)
                {
                    switch (s.OpeningRequirementType)
                    {
                        case Style.eOpening.Defensive:
                        if (StylesDefensive == null)
                            StylesDefensive = new List<Style>(1);

                        StylesDefensive.Add(s);
                        break;

                        case Style.eOpening.Positional:
                        switch ((Style.eOpeningPosition)s.OpeningRequirementValue)
                        {
                            case Style.eOpeningPosition.Back:
                            if (StylesBack == null)
                                StylesBack = new List<Style>(1);

                            StylesBack.Add(s);
                            break;

                            case Style.eOpeningPosition.Side:
                            if (StylesSide == null)
                                StylesSide = new List<Style>(1);

                            StylesSide.Add(s);
                            break;

                            case Style.eOpeningPosition.Front:
                            if (StylesFront == null)
                                StylesFront = new List<Style>(1);

                            StylesFront.Add(s);
                            break;

                            default:
                            log.Warn($"MimicNPC.SortStyles(): Invalid OpeningRequirementValue for positional style {s.Name}, ID {s.ID}, ClassId {s.ClassID}");
                            break;
                        }
                        break;

                        default:
                        if (s.OpeningRequirementValue > 0)
                        {
                            if (StylesChain == null)
                                StylesChain = new List<Style>(1);

                            StylesChain.Add(s);
                        }
                        else
                        {
                            bool added = false;

                            if (s.Procs.Count > 0)
                            {
                                foreach ((Spell, int, int) proc in s.Procs)
                                {
                                    if (proc.Item1.SpellType == eSpellType.StyleTaunt)
                                    {
                                        if (proc.Item1.ID == 20000)
                                        {
                                            if (StylesTaunt == null)
                                                StylesTaunt = new List<Style>(1);

                                            StylesTaunt.Add(s);
                                            added = true;
                                        }
                                        else if (proc.Item1.ID == 20001)
                                        {
                                            if (StylesDetaunt == null)
                                                StylesDetaunt = new List<Style>(1);

                                            StylesDetaunt.Add(s);
                                            added = true;
                                        }
                                    }
                                }
                            }

                            if (!added)
                            {
                                if (StylesAnytime == null)
                                    StylesAnytime = new List<Style>(1);

                                StylesAnytime.Add(s);
                            }
                        }
                        break;
                    }
                }
                else
                {
                    if (StylesShield == null)
                        StylesShield = new List<Style>(1);

                    StylesShield.Add(s);
                }
            }
        }

        public override void DisableSkill(Skill skill, int duration)
        {
            base.DisableSkill(skill, duration);
        }

        public void SetLevel(byte level)
        {
            Level = level;
            //Experience = ExperienceForNextLevel - 1;
            Experience = ExperienceForCurrentLevel;

            if (level >= 20 && level < 25)
                RealmLevel = Util.Random(1, 10);
            else if (level > 24 && level < 30)
                RealmLevel = Util.Random(1, 15);
            else if (level > 29 && level < 35)
                RealmLevel = Util.Random(1, 25);
            else if (level > 39 && level < 50)
                RealmLevel = Util.Random(1, 45);
            else if (level == 50)
                RealmLevel = Util.Random(1, 90);

            RealmPoints = REALMPOINTS_FOR_LEVEL[RealmLevel];
        }

        public void SetRaceAndName()
        {
            PlayerRace playerRace = CharacterClass.EligibleRaces[Util.Random(CharacterClass.EligibleRaces.Count - 1)];

            Gender = Util.RandomBool() ? eGender.Female : eGender.Male;
            Race = (short)playerRace.ID;
            Model = (ushort)playerRace.GetModel(Gender);
            Size = (byte)Util.Random(45, 60);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Dictionary<eStat, int> statDict;
            GlobalConstants.STARTING_STATS_DICT.TryGetValue((eRace)Race, out statDict);

            ChangeBaseStat(eStat.STR, (short)statDict[eStat.STR]);
            ChangeBaseStat(eStat.CON, (short)statDict[eStat.CON]);
            ChangeBaseStat(eStat.DEX, (short)statDict[eStat.DEX]);
            ChangeBaseStat(eStat.QUI, (short)statDict[eStat.QUI]);
            ChangeBaseStat(eStat.INT, (short)statDict[eStat.INT]);
            ChangeBaseStat(eStat.PIE, (short)statDict[eStat.PIE]);
            ChangeBaseStat(eStat.EMP, (short)statDict[eStat.EMP]);
            ChangeBaseStat(eStat.CHR, (short)statDict[eStat.CHR]);
            ChangeBaseStat(CharacterClass.PrimaryStat, 10);
            ChangeBaseStat(CharacterClass.SecondaryStat, 10);
            ChangeBaseStat(CharacterClass.TertiaryStat, 10);

            foreach (KeyValuePair<eRealm, List<eCharacterClass>> keyValuePair in GlobalConstants.STARTING_CLASSES_DICT)
            {
                if (keyValuePair.Value.Contains((eCharacterClass)CharacterClass.ID))
                {
                    Realm = keyValuePair.Key;
                    break;
                }
            }

            Name = MimicNames.GetName(Gender, Realm);
        }

        public override void StartAttack(GameObject target)
        {
            if (IsSitting)
                Sit(false);

            base.StartAttack(target);
        }

        public override bool AddToWorld()
        {
            if (!(base.AddToWorld()))
                return false;

            RandomNumberDeck = new PlayerDeck();

            return true;
        }

        public override void Delete()
        {
            Group?.RemoveMember(this);
            base.Delete();
        }

        public override bool RemoveFromWorld()
        {
            if (!base.RemoveFromWorld())
                return false;

            Duel?.Stop();

            if (ControlledBrain != null)
                CommandNpcRelease();

            return true;
        }

        public double SpecLock { get; set; }
        public long LastWorldUpdate { get; set; }

        private PlayerDeck _randomNumberDeck;

        #region Client/Character/VariousFlags

        /// <summary>
        /// This is our gameclient!
        /// </summary>
        protected readonly GameClient m_client;

        /// <summary>
        /// This holds the character this player is
        /// based on!
        /// (renamed and private, cause if derive is needed overwrite PlayerCharacter)
        /// </summary>
        protected DbCoreCharacter m_dbCharacter;

        /// <summary>
        /// The guild id this character belong to
        /// </summary>
        protected string m_guildId;

        /// <summary>
        /// Char spec points checked on load
        /// </summary>
        protected bool SpecPointsOk = true;

        /// <summary>
        /// Has this player entered the game, will be
        /// true after the first time the char enters
        /// the world
        /// </summary>
        protected bool m_enteredGame;

        /// <summary>
        /// Is this player being 'jumped' to a new location?
        /// </summary>
        public bool IsJumping { get; set; }

        /// <summary>
        /// true if the targetObject is visible
        /// </summary>
        protected bool m_targetInView;

        /// <summary>
        /// Property for the optional away from keyboard message.
        /// </summary>
        public static readonly string AFK_MESSAGE = "afk_message";

        /// <summary>
        /// Property for the optional away from keyboard message.
        /// </summary>
        public static readonly string QUICK_CAST_CHANGE_TICK = "quick_cast_change_tick";

        /// <summary>
        /// Last spell cast from a used item
        /// </summary>
        public static readonly string LAST_USED_ITEM_SPELL = "last_used_item_spell";

        public static readonly string REALM_LOYALTY_KEY = "realm_loyalty";
        public static readonly string CURRENT_LOYALTY_KEY = "current_loyalty_days";

        /// <summary>
        /// Effectiveness of the rez sick that should be applied. This is set by rez spells just before rezzing.
        /// </summary>
        public static readonly string RESURRECT_REZ_SICK_EFFECTIVENESS = "RES_SICK_EFFECTIVENESS";

        /// <summary>
        /// Array that stores ML step completition
        /// </summary>
        private ArrayList m_mlSteps = new ArrayList();

        /// <summary>
        /// Gets or sets the targetObject's visibility
        /// </summary>
        public override bool TargetInView
        {
            get
            {
                if (GetDistanceTo(TargetObject) <= TargetInViewAlwaysTrueMinRange)
                    return true;

                return m_targetInView;
            }
            set => m_targetInView = value;
        }

        public override int TargetInViewAlwaysTrueMinRange => (TargetObject is GamePlayer targetPlayer && targetPlayer.IsMoving) ? 100 : 64;

        public PlayerDeck RandomNumberDeck
        {
            get
            {
                if (_randomNumberDeck == null)
                    _randomNumberDeck = new PlayerDeck();

                return _randomNumberDeck;
            }
            set { _randomNumberDeck = value; }
        }

        /// <summary>
        /// Holds the ground target visibility flag
        /// </summary>
        protected bool _groundTargetInView;

        /// <summary>
        /// Gets or sets the GroundTargetObject's visibility
        /// </summary>
        public override bool GroundTargetInView
        {
            get { return _groundTargetInView; }
            set { _groundTargetInView = value; }
        }

        protected int m_OutOfClassROGPercent = 0;

        public int OutOfClassROGPercent
        {
            get { return m_OutOfClassROGPercent; }
            set { m_OutOfClassROGPercent = value; }
        }

        /// <summary>
        /// Player is in BG ?
        /// </summary>
        protected bool _isInBG;

        public bool isInBG
        {
            get { return _isInBG; }
            set { _isInBG = value; }
        }

        /// <summary>
        /// The character the player is based on
        /// </summary>
        internal DbCoreCharacter DBCharacter
        {
            get { return m_dbCharacter; }
        }

        /// <summary>
        /// Can this player use cross realm items
        /// </summary>
        public virtual bool CanUseCrossRealmItems { get { return Properties.ALLOW_CROSS_REALM_ITEMS; } }

        protected bool _lastDeathPvP;

        public bool LastDeathPvP
        {
            get { return _lastDeathPvP; }
            set { _lastDeathPvP = value; }
        }

        protected bool _wasMovedByCorpseSummoner;

        public bool WasMovedByCorpseSummoner
        {
            get { return _wasMovedByCorpseSummoner; }
            set { _wasMovedByCorpseSummoner = value; }
        }

        #region Database Accessor

        /// <summary>
        /// Gets or sets the Database ObjectId for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public string ObjectId
        {
            get { return DBCharacter != null ? DBCharacter.ObjectId : InternalID; }
            set { if (DBCharacter != null) DBCharacter.ObjectId = value; }
        }

        /// <summary>
        /// Gets or sets the gain XP flag for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public bool GainXP
        {
            get { return true; }
            set { if (DBCharacter != null) DBCharacter.GainXP = value; }
        }

        /// <summary>
        /// Gets or sets the gain RP flag for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public bool GainRP
        {
            get { return true; }
            set { if (DBCharacter != null) DBCharacter.GainRP = value; }
        }

        /// <summary>
        /// Gets or sets the boosted flag for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public bool Boosted
        {
            get { return (DBCharacter != null ? DBCharacter.isBoosted : true); }
            set { if (DBCharacter != null) DBCharacter.isBoosted = value; }
        }

        /// <summary>
        /// gets or sets the guildnote for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public string GuildNote
        {
            get { return DBCharacter != null ? DBCharacter.GuildNote : String.Empty; }
            set { if (DBCharacter != null) DBCharacter.GuildNote = value; }
        }

        /// <summary>
        /// Gets or sets the BindHouseRegion for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHouseRegion
        {
            get { return DBCharacter != null ? DBCharacter.BindHouseRegion : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHouseRegion = value; }
        }

        /// <summary>
        /// Gets or sets the BindHouseXpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHouseXpos
        {
            get { return DBCharacter != null ? DBCharacter.BindHouseXpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHouseXpos = value; }
        }

        /// <summary>
        /// Gets or sets the BindHouseYpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHouseYpos
        {
            get { return DBCharacter != null ? DBCharacter.BindHouseYpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHouseYpos = value; }
        }

        /// <summary>
        /// Gets or sets BindHouseZpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHouseZpos
        {
            get { return DBCharacter != null ? DBCharacter.BindHouseZpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHouseZpos = value; }
        }

        /// <summary>
        /// Gets or sets the BindHouseHeading for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHouseHeading
        {
            get { return DBCharacter != null ? DBCharacter.BindHouseHeading : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHouseHeading = value; }
        }

        /// <summary>
        /// Gets or sets the DeathTime for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public long DeathTime
        {
            get { return DBCharacter != null ? DBCharacter.DeathTime : 0; }
            set { if (DBCharacter != null) DBCharacter.DeathTime = value; }
        }

        /// <summary>
        /// Gets or sets the BindRegion for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindRegion
        {
            get { return DBCharacter != null ? DBCharacter.BindRegion : 0; }
            set { if (DBCharacter != null) DBCharacter.BindRegion = value; }
        }

        /// <summary>
        /// Gets or sets the BindXpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindXpos
        {
            get { return DBCharacter != null ? DBCharacter.BindXpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindXpos = value; }
        }

        /// <summary>
        /// Gets or sets the BindYpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindYpos
        {
            get { return DBCharacter != null ? DBCharacter.BindYpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindYpos = value; }
        }

        /// <summary>
        /// Gets or sets the BindZpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindZpos
        {
            get { return DBCharacter != null ? DBCharacter.BindZpos : 0; }
            set { if (DBCharacter != null) DBCharacter.BindZpos = value; }
        }

        /// <summary>
        /// Gets or sets the BindHeading for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int BindHeading
        {
            get { return DBCharacter != null ? DBCharacter.BindHeading : 0; }
            set { if (DBCharacter != null) DBCharacter.BindHeading = value; }
        }

        /// <summary>
        /// Gets or sets the Database MaxEndurance for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public int DBMaxEndurance
        {
            get { return DBCharacter != null ? DBCharacter.MaxEndurance : 100; }
            set { if (DBCharacter != null) DBCharacter.MaxEndurance = value; }
        }

        /// <summary>
        /// Gets AccountName for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public string AccountName
        {
            get { return DBCharacter != null ? DBCharacter.AccountName : string.Empty; }
        }

        /// <summary>
        /// Gets CreationDate for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public DateTime CreationDate
        {
            get { return DBCharacter != null ? DBCharacter.CreationDate : DateTime.MinValue; }
        }

        /// <summary>
        /// Gets LastPlayed for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public DateTime LastPlayed
        {
            get { return DBCharacter != null ? DBCharacter.LastPlayed : DateTime.MinValue; }
        }

        /// <summary>
        /// Gets or sets the BindYpos for this player
        /// (delegate to property in DBCharacter)
        /// </summary>
        public byte DeathCount
        {
            get { return DBCharacter != null ? DBCharacter.DeathCount : (byte)0; }
            set { if (DBCharacter != null) DBCharacter.DeathCount = value; }
        }

        private int _killStreak = 0;

        public int KillStreak
        {
            get { return _killStreak; }
            set { _killStreak = value; }
        }

        /// <summary>
        /// Gets the last time this player leveled up
        /// </summary>
        public DateTime LastLevelUp
        {
            get { return DBCharacter != null ? DBCharacter.LastLevelUp : DateTime.MinValue; }
            set { if (DBCharacter != null) DBCharacter.LastLevelUp = value; }
        }

        #endregion Database Accessor

        #endregion Client/Character/VariousFlags

        #region Combat timer

        /// <summary>
        /// gets the DamageRvR Memory of this player
        /// </summary>
        public override long DamageRvRMemory
        {
            get => m_damageRvRMemory;
            set => m_damageRvRMemory = value;
        }

        public override long LastAttackTickPvE
        {
            set
            {
                bool wasInCombat = InCombat;
                base.LastAttackTickPvE = value;

                //if (!wasInCombat && InCombat)
                //    Out.SendUpdateMaxSpeed();

                ResetInCombatTimer();
            }
        }

        public override long LastAttackTickPvP
        {
            set
            {
                bool wasInCombat = InCombat;
                base.LastAttackTickPvP = value;

                //if (!wasInCombat && InCombat)
                //    Out.SendUpdateMaxSpeed();

                ResetInCombatTimer();
            }
        }

        public override long LastAttackedByEnemyTickPvE
        {
            set
            {
                bool wasInCombat = InCombat;
                base.LastAttackedByEnemyTickPvE = value;

                //if (!wasInCombat && InCombat)
                //    Out.SendUpdateMaxSpeed();

                ResetInCombatTimer();
            }
        }

        public override long LastAttackedByEnemyTickPvP
        {
            set
            {
                bool wasInCombat = InCombat;
                base.LastAttackTickPvP = value;
                //if (!wasInCombat && InCombat)
                //    Out.SendUpdateMaxSpeed();

                ResetInCombatTimer();
            }
        }

        /// <summary>
        /// Expire Combat Timer Interval
        /// </summary>
        private static int COMBAT_TIMER_INTERVAL => 11000;

        /// <summary>
        /// Combat Timer
        /// </summary>
        private ECSGameTimer m_combatTimer;

        /// <summary>
        /// Reset and Restart Combat Timer
        /// </summary>
        protected virtual void ResetInCombatTimer()
        {
            m_combatTimer.Start(COMBAT_TIMER_INTERVAL);
        }

        #endregion Combat timer

        #region release/bind/pray

        #region Binding

        /// <summary>
        /// Property that holds tick when the player bind last time
        /// </summary>
        public const string LAST_BIND_TICK = "LastBindTick";

        /// <summary>
        /// Min Allowed Interval Between Player Bind
        /// </summary>
        public virtual int BindAllowInterval { get { return 60000; } }

        /// <summary>
        /// Binds this player to the current location
        /// </summary>
        /// <param name="forced">if true, can bind anywhere</param>
        public virtual void Bind(bool forced)
        {
            if (CurrentRegion.IsInstance)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Bind.CantHere"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (forced)
            {
                BindRegion = CurrentRegionID;
                BindHeading = Heading;
                BindXpos = X;
                BindYpos = Y;
                BindZpos = Z;
                if (DBCharacter != null)
                    GameServer.Database.SaveObject(DBCharacter);
                return;
            }

            if (!IsAlive)
                return;

            //60 second rebind timer
            long lastBindTick = TempProperties.GetProperty<long>(LAST_BIND_TICK, 0);
            long changeTime = CurrentRegion.Time - lastBindTick;

            if (changeTime < BindAllowInterval)
                return;

            bool bound = false;

            //var bindarea = CurrentAreas.OfType<Area.BindArea>().FirstOrDefault(ar => GameServer.ServerRules.IsAllowedToBind(this, ar.BindPoint));
            //if (bindarea != null)
            //{
            //    bound = true;
            //    BindRegion = CurrentRegionID;
            //    BindHeading = Heading;
            //    BindXpos = X;
            //    BindYpos = Y;
            //    BindZpos = Z;
            //    if (DBCharacter != null)
            //        GameServer.Database.SaveObject(DBCharacter);
            //}

            //if we are not bound yet lets check if we are in a house where we can bind
            if (!bound && InHouse && CurrentHouse != null)
            {
                var house = CurrentHouse;
                bool canbindhere;
                try
                {
                    canbindhere = house.HousepointItems.Any(kv => ((GameObject)kv.Value.GameObject).GetName(0, false).EndsWith("bindstone", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    canbindhere = false;
                }

                if (canbindhere)
                {
                    // make sure we can actually use the bindstone
                    //if (!house.CanBindInHouse(this))
                    //{
                    //    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Bind.CantHere"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    //    return;
                    //}
                    //else
                    //{
                    //    bound = true;
                    //    double angle = house.Heading * ((Math.PI * 2) / 360); // angle*2pi/360;
                    //    int outsideX = (int)(house.X + (0 * Math.Cos(angle) + 500 * Math.Sin(angle)));
                    //    int outsideY = (int)(house.Y - (500 * Math.Cos(angle) - 0 * Math.Sin(angle)));
                    //    ushort outsideHeading = (ushort)((house.Heading < 180 ? house.Heading + 180 : house.Heading - 180) / 0.08789);
                    //    BindHouseRegion = CurrentRegionID;
                    //    BindHouseHeading = outsideHeading;
                    //    BindHouseXpos = outsideX;
                    //    BindHouseYpos = outsideY;
                    //    BindHouseZpos = house.Z;
                    //    if (DBCharacter != null)
                    //        GameServer.Database.SaveObject(DBCharacter);
                    //}
                }
            }

            if (bound)
            {
                if (!IsMoving)
                {
                    eEmote bindEmote = eEmote.Bind;
                    switch (Realm)
                    {
                        case eRealm.Albion: bindEmote = eEmote.BindAlb; break;
                        case eRealm.Midgard: bindEmote = eEmote.BindMid; break;
                        case eRealm.Hibernia: bindEmote = eEmote.BindHib; break;
                    }

                    foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    {
                        if (player == null)
                            return;

                        if ((int)player.Client.Version < (int)GameClient.eClientVersion.Version187)
                            player.Out.SendEmoteAnimation(this, eEmote.Bind);
                        else
                            player.Out.SendEmoteAnimation(this, bindEmote);
                    }
                }

                TempProperties.SetProperty(LAST_BIND_TICK, CurrentRegion.Time);
            }
        }

        #endregion Binding

        #region Releasing

        /// <summary>
        /// tick when player is died
        /// </summary>
        protected long m_deathTick;

        /// <summary>
        /// choosed the player to release as soon as possible?
        /// </summary>
        protected bool m_automaticRelease = false;

        /// <summary>
        /// The release timer for this player
        /// </summary>
        protected ECSGameTimer m_releaseTimer;

        /// <summary>
        /// Stops release timer and closes timer window
        /// </summary>
        public void StopReleaseTimer()
        {
            //Out.SendCloseTimerWindow();
            if (m_releaseTimer != null)
            {
                m_releaseTimer.Stop();
                m_releaseTimer = null;
            }
        }

        /// <summary>
        /// minimum time to wait before release is possible in seconds
        /// </summary>
        protected const int RELEASE_MINIMUM_WAIT = 10;

        /// <summary>
        /// max time before auto release in seconds
        /// </summary>
        protected const int RELEASE_TIME = 900;

        /// <summary>
        /// The property name that is set when relea
        /// sing to another region
        /// </summary>
        public const string RELEASING_PROPERTY = "releasing";

        /// <summary>
        /// The current release type
        /// </summary>
        protected eReleaseType m_releaseType = eReleaseType.Normal;

        /// <summary>
        /// Gets the player's current release type.
        /// </summary>
        public eReleaseType ReleaseType
        {
            get { return m_releaseType; }
        }

        /// <summary>
        /// Releases this player after death ... subtracts xp etc etc...
        /// </summary>
        /// <param name="releaseCommand">The type of release used for this player</param>
        /// <param name="forced">if true, will release even if not dead</param>
        public virtual void Release(eReleaseType releaseCommand, bool forced)
        {
            //DbCoreCharacter character = DBCharacter;
            //if (character == null)
            //    return;

            // check if valid housebind
            //if (releaseCommand == eReleaseType.House && character.BindHouseRegion < 1)
            //{
            //    //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.NoValidBindpoint"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            //    releaseCommand = eReleaseType.Bind;
            //}

            //battlegrounds caps
            DbBattleground bg = GameServer.KeepManager.GetBattleground(CurrentRegionID);
            if (bg != null && releaseCommand == eReleaseType.RvR)
            {
                if (Level > bg.MaxLevel)
                    releaseCommand = eReleaseType.Normal;
            }

            if (IsAlive)
                return;

            if (!forced)
            {
                if (m_releaseType == eReleaseType.Duel)
                    return;

                m_releaseType = releaseCommand;
                // we use realtime, because timer window is realtime
                long diff = m_deathTick - GameLoop.GameLoopTime + RELEASE_MINIMUM_WAIT * 1000;
                if (diff >= 1000)
                {
                    if (m_automaticRelease)
                    {
                        m_automaticRelease = false;
                        m_releaseType = eReleaseType.Normal;

                        return;
                    }

                    m_automaticRelease = true;
                    switch (releaseCommand)
                    {
                        default:
                        {
                            // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.WillReleaseAuto", diff / 1000), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                            return;
                        }
                        case eReleaseType.City:
                        {
                            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.WillReleaseAutoCity", diff / 1000), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                            return;
                        }
                        case eReleaseType.RvR:
                        {
                            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.ReleaseToPortalKeep", diff / 1000), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                            return;
                        }
                        case eReleaseType.House:
                        {
                            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.ReleaseToHouse", diff / 1000), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                            return;
                        }
                    }
                }
            }
            else
            {
                m_releaseType = releaseCommand;
            }

            int relX = 0, relY = 0, relZ = 0;
            ushort relRegion = 0, relHeading = 0;
            switch (m_releaseType)
            {
                case eReleaseType.Duel:
                {
                    relRegion = CurrentRegion.ID;
                    relX = X;
                    relY = Y;
                    relZ = Z;
                    relHeading = 2048;
                    break;
                }
                case eReleaseType.House:
                {
                    relRegion = (ushort)BindHouseRegion;
                    relX = BindHouseXpos;
                    relY = BindHouseYpos;
                    relZ = BindHouseZpos;
                    relHeading = (ushort)BindHouseHeading;
                    break;
                }

                case eReleaseType.City:
                {
                    if (Realm == eRealm.Hibernia)
                    {
                        relRegion = 201; // Tir Na Nog
                        relX = 34149;
                        relY = 32063;
                        relZ = 8047;
                        relHeading = 1025;
                    }
                    else if (Realm == eRealm.Midgard)
                    {
                        relRegion = 101; // Jordheim
                        relX = 30094;
                        relY = 27589;
                        relZ = 8763;
                        relHeading = 3468;
                    }
                    else
                    {
                        relRegion = 10; // City of Camelot
                        relX = 36240;
                        relY = 29695;
                        relZ = 7985;
                        relHeading = 4095;
                    }
                    relHeading = 2048;
                    break;
                }
                case eReleaseType.RvR:
                {
                    GamePlayer player = Client.Player as GamePlayer;

                    if (player.CurrentRegionID == 27)
                    {
                        relRegion = 27;
                        relX = 342521;
                        relY = 385230;
                        relZ = 5410;
                        relHeading = 1756;
                        break;
                    }

                    foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(CurrentRegionID))
                    {
                        if (keep.IsPortalKeep && keep.OriginalRealm == Realm)
                        {
                            relRegion = keep.CurrentRegion.ID;
                            relX = keep.X;
                            relY = keep.Y;
                            relZ = keep.Z;
                        }
                    }

                    //if we aren't releasing anywhere, release to the border keeps
                    if (relX == 0)
                    {
                        relRegion = CurrentRegion.ID;
                        GameServer.KeepManager.GetBorderKeepLocation(((byte)Realm * 2) / 1, out relX, out relY, out relZ, out relHeading);
                    }
                    break;
                }
                default:
                {
                    if (!ServerProperties.Properties.DISABLE_TUTORIAL)
                    {
                        //Tutorial
                        if (BindRegion == 27)
                        {
                            switch (Realm)
                            {
                                case eRealm.Albion:
                                {
                                    relRegion = 1; // Cotswold
                                    relX = 8192 + 553251;
                                    relY = 8192 + 502936;
                                    relZ = 2280;
                                    break;
                                }
                                case eRealm.Midgard:
                                {
                                    relRegion = 100; // Mularn
                                    relX = 8192 + 795621;
                                    relY = 8192 + 719590;
                                    relZ = 4680;
                                    break;
                                }
                                case eRealm.Hibernia:
                                {
                                    relRegion = 200; // MagMell
                                    relX = 8192 + 338652;
                                    relY = 8192 + 482335;
                                    relZ = 5200;
                                    break;
                                }
                            }
                            break;
                        }
                    }

                    switch (CurrentRegionID)
                    {
                        //battlegrounds
                        case 234:
                        case 235:
                        case 236:
                        case 237:
                        case 238:
                        case 239:
                        case 240:
                        case 241:
                        case 242:
                        {
                            //get the bg cap
                            byte cap = 50;
                            foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(CurrentRegionID))
                            {
                                if (keep.DBKeep.BaseLevel < cap)
                                {
                                    cap = keep.DBKeep.BaseLevel;
                                    break;
                                }
                            }
                            //get the portal location
                            foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(CurrentRegionID))
                            {
                                if (keep.DBKeep.BaseLevel > 50 && keep.Realm == Realm)
                                {
                                    relRegion = (ushort)keep.Region;
                                    relX = keep.X;
                                    relY = keep.Y;
                                    relZ = keep.Z;
                                    break;
                                }
                            }
                            break;
                        }
                        //nf
                        case 163:
                        {
                            if (BindRegion != 163)
                            {
                                relRegion = 163;
                                switch (Realm)
                                {
                                    case eRealm.Albion:
                                    {
                                        GameServer.KeepManager.GetBorderKeepLocation(1, out relX, out relY, out relZ, out relHeading);
                                        break;
                                    }
                                    case eRealm.Midgard:
                                    {
                                        GameServer.KeepManager.GetBorderKeepLocation(3, out relX, out relY, out relZ, out relHeading);
                                        break;
                                    }
                                    case eRealm.Hibernia:
                                    {
                                        GameServer.KeepManager.GetBorderKeepLocation(5, out relX, out relY, out relZ, out relHeading);
                                        break;
                                    }
                                }
                                break;
                            }
                            else
                            {
                                relRegion = (ushort)BindRegion;
                                relX = BindXpos;
                                relY = BindYpos;
                                relZ = BindZpos;
                                relHeading = (ushort)BindHeading;
                            }
                            break;
                        }/*
                        //bg45-49
                        case 165:
                        {
                            break;
                        }*/
                        default:
                        {
                            relRegion = (ushort)BindRegion;
                            relX = BindXpos;
                            relY = BindYpos;
                            relZ = BindZpos;
                            relHeading = (ushort)BindHeading;
                            break;
                        }
                    }
                    break;
                }
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.YouRelease"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            //Out.SendCloseTimerWindow();
            if (m_releaseTimer != null)
            {
                m_releaseTimer.Stop();
                m_releaseTimer = null;
            }

            if (Realm != eRealm.None)
            {
                //if (Level >= Properties.PVE_EXP_LOSS_LEVEL && !HCFlag)
                //{
                //    // actual lost exp, needed for 2nd stage deaths
                //    long lostExp = Experience;
                //    long lastDeathExpLoss = TempProperties.GetProperty<long>(DEATH_EXP_LOSS_PROPERTY);
                //    TempProperties.RemoveProperty(DEATH_EXP_LOSS_PROPERTY);

                //    GainExperience(eXPSource.Other, -lastDeathExpLoss);
                //    lostExp -= Experience;

                //    // raise only the gravestone if xp has to be stored in it
                //    if (lostExp > 0)
                //    {
                //        // find old gravestone of player and remove it
                //        if (character.HasGravestone)
                //        {
                //            Region reg = WorldMgr.GetRegion((ushort)character.GravestoneRegion);
                //            if (reg != null)
                //            {
                //                //GameGravestone oldgrave = reg.FindGraveStone(this);
                //                //if (oldgrave != null)
                //                //{
                //                //    oldgrave.Delete();
                //                //}
                //            }
                //            character.HasGravestone = false;
                //        }

                //        //GameGravestone gravestone = new GameGravestone(this, lostExp);
                //        //gravestone.AddToWorld();
                //        //character.GravestoneRegion = gravestone.CurrentRegionID;
                //        //character.HasGravestone = true;
                //        //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.GraveErected"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
                //        //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.ReturnToPray"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
                //    }
                //}
            }

            if (Level >= Properties.PVE_CON_LOSS_LEVEL)
            {
                int deathConLoss = TempProperties.GetProperty<int>(DEATH_CONSTITUTION_LOSS_PROPERTY); // get back constitution lost at death
                if (deathConLoss > 0)
                {
                    TotalConstitutionLostAtDeath += deathConLoss;
                    // Out.SendCharStatsUpdate();
                    // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.LostConstitution"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
                }
            }

            //Update health&sit state first!
            m_isDead = false;
            Health = MaxHealth;
            Endurance = MaxEndurance;
            Mana = MaxMana;
            StartPowerRegeneration();
            StartEnduranceRegeneration();
            LastDeathPvP = false;

            var maxChargeItems = Properties.MAX_CHARGE_ITEMS;
            /*
            foreach (var item in Inventory.EquippedItems)
            {
                //max 2 charges
                if (item.SpellID > 0 && SelfBuffChargeIDs.Contains(item.SpellID) && LoyaltyManager.GetPlayerRealmLoyalty(this).Days > 30)
                {
                    if(ActiveBuffCharges < maxChargeItems)
                        UseItemCharge(item, (int)eUseType.use1);
                    else
                    {
                        Out.SendMessage("You may only use two buff charge effects. This item fails to affect you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    }
                }

                //max 2 charges
                if (item.SpellID1 > 0 && SelfBuffChargeIDs.Contains(item.SpellID1) && LoyaltyManager.GetPlayerRealmLoyalty(this).Days > 30)
                {
                    if(ActiveBuffCharges < maxChargeItems)
                        UseItemCharge(item, (int)eUseType.use2);
                    else
                    {
                        Out.SendMessage("You may only use two buff charge effects. This item fails to affect you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    }
                }
            }*/

            //UpdatePlayerStatus();

            Region region = null;
            if ((region = WorldMgr.GetRegion((ushort)BindRegion)) != null && region.GetZone(BindXpos, BindYpos) != null)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.SurroundingChange"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            }
            else
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.NoValidBindpoint"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                Bind(true);
            }

            int oldRegion = CurrentRegionID;

            //Call MoveTo after new GameGravestone(..
            //or the GraveStone will be located at the player's bindpoint

            MoveTo(relRegion, relX, relY, relZ, relHeading);
            //It is enough if we revive the player on this client only here
            //because for other players the player will be removed in the MoveTo
            //method and added back again (if in view) with full health ... so no
            //revive needed for others...
            //Out.SendPlayerRevive(this);
            //			Out.SendUpdatePlayer();
            //Out.SendUpdatePoints();

            //Set property indicating that we are releasing to another region; used for Released event
            if (oldRegion != CurrentRegionID)
                TempProperties.SetProperty(RELEASING_PROPERTY, true);
            else
            {
                // fire the player revive event
                Notify(GamePlayerEvent.Revive, this);
                Notify(GamePlayerEvent.Released, this);
            }

            TempProperties.RemoveProperty(DEATH_CONSTITUTION_LOSS_PROPERTY);

            //Reset last valide position array to prevent /stuck avec /release
            lock (m_lastUniqueLocations)
            {
                for (int i = 0; i < m_lastUniqueLocations.Length; i++)
                {
                    GameLocation loc = m_lastUniqueLocations[i];
                    loc.X = X;
                    loc.Y = Y;
                    loc.Z = Z;
                    loc.Heading = Heading;
                    loc.RegionID = CurrentRegionID;
                }
            }
        }

        /// <summary>
        /// helper state var for different release phases
        /// </summary>
        private byte m_releasePhase = 0;

        /// <summary>
        /// callback every second to control realtime release
        /// </summary>
        /// <param name="callingTimer"></param>
        /// <returns></returns>
        protected virtual int ReleaseTimerCallback(ECSGameTimer callingTimer)
        {
            if (IsAlive)
                return 0;

            long diffToRelease = GameLoop.GameLoopTime - m_deathTick;
            if (m_automaticRelease && diffToRelease > RELEASE_MINIMUM_WAIT * 1000)
            {
                Release(m_releaseType, true);
                return 0;
            }
            diffToRelease = (RELEASE_TIME * 1000 - diffToRelease) / 1000;
            if (diffToRelease <= 0)
            {
                Release(m_releaseType, true);
                return 0;
            }
            if (m_releasePhase <= 1 && diffToRelease <= 10 && diffToRelease >= 8)
            {
                // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.WillReleaseIn", 10), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                m_releasePhase = 2;
            }
            if (m_releasePhase == 0 && diffToRelease <= 30 && diffToRelease >= 28)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Release.WillReleaseIn", 30), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                m_releasePhase = 1;
            }
            return 1000;
        }

        /// <summary>
        /// The current death type
        /// </summary>
        protected eDeathType m_deathtype;

        /// <summary>
        /// Gets the player's current death type.
        /// </summary>
        public eDeathType DeathType
        {
            get { return m_deathtype; }
            set { m_deathtype = value; }
        }

        /// <summary>
        /// Called when player revive
        /// </summary>
        public virtual void OnRevive(DOLEvent e, object sender, EventArgs args)
        {
            GamePlayer player = (GamePlayer)sender;
            //effectListComponent.CancelAll();
            m_isDead = false;

            bool applyRezSick = true;

            // Used by spells like Perfect Recovery
            if (TempProperties.GetAllProperties().Contains(RESURRECT_REZ_SICK_EFFECTIVENESS) && TempProperties.GetProperty<double>(RESURRECT_REZ_SICK_EFFECTIVENESS) == 0)
            {
                applyRezSick = false;
                TempProperties.RemoveProperty(RESURRECT_REZ_SICK_EFFECTIVENESS);
            }
            else if (player.Level < ServerProperties.Properties.RESS_SICKNESS_LEVEL)
            {
                applyRezSick = false;
            }

            if (player.IsUnderwater && player.CanBreathUnderWater == false)
                player.UpdateWaterBreathState(eWaterBreath.Holding);

            //We need two different sickness spells because RvR sickness is not curable by Healer NPC -Unty
            if (applyRezSick)
            {
                switch (DeathType)
                {
                    case eDeathType.RvR:
                    {
                        Spell rvrIllness = SkillBase.GetSpellByID(8181);
                        CastSpell(rvrIllness, SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells));
                        break;
                    }
                    case eDeathType.PvP: //PvP sickness is the same as PvE sickness - Curable
                    case eDeathType.PvE:
                    {
                        Spell pveIllness = SkillBase.GetSpellByID(2435);
                        CastSpell(pveIllness, SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells));
                        break;
                    }
                }
            }

            GameEventMgr.RemoveHandler(this, GamePlayerEvent.Revive, new DOLEventHandler(OnRevive));
            m_deathtype = eDeathType.None;
            LastDeathPvP = false;
            //UpdatePlayerStatus();
            //Out.SendPlayerRevive(this);
        }

        /// <summary>
        /// Property that saves experience lost on last death
        /// </summary>
        public const string DEATH_EXP_LOSS_PROPERTY = "death_exp_loss";

        /// <summary>
        /// Property that saves condition lost on last death
        /// </summary>
        public const string DEATH_CONSTITUTION_LOSS_PROPERTY = "death_con_loss";

        #endregion Releasing

        #region Praying

        /// <summary>
        /// The timer that will be started when the player wants to pray
        /// </summary>
        private ECSGameTimer m_prayAction;

        /// <summary>
        /// Gets the praying-state of this living
        /// </summary>
        public virtual bool IsPraying => m_prayAction?.IsAlive == true;

        /// <summary>
        /// Prays on a gravestone for XP!
        /// </summary>
        public virtual void Pray()
        {
            string cantPrayMessage = null;
            GameGravestone gravestone = TargetObject as GameGravestone;

            if (!IsAlive)
                cantPrayMessage = "GamePlayer.Pray.CantPrayNow";
            //else if (IsRiding)
            //    cantPrayMessage = "GamePlayer.Pray.CantPrayRiding";
            else if (gravestone == null)
                cantPrayMessage = "GamePlayer.Pray.NeedTarget";
            else if (!gravestone.InternalID.Equals(InternalID))
                cantPrayMessage = "GamePlayer.Pray.SelectGrave";
            else if (!IsWithinRadius(gravestone, 2000))
                cantPrayMessage = "GamePlayer.Pray.MustGetCloser";
            else if (IsMoving)
                cantPrayMessage = "GamePlayer.Pray.MustStandingStill";
            else if (IsPraying)
                cantPrayMessage = "GamePlayer.Pray.AlreadyPraying";

            if (cantPrayMessage != null)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, cantPrayMessage), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            m_prayAction = new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(_ =>
            {
                if (gravestone.XPValue > 0)
                {
                    // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Pray.GainBack"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    GainExperience(eXPSource.Praying, gravestone.XPValue);
                }

                gravestone.XPValue = 0;
                gravestone.Delete();
                m_prayAction = null;
                return 0;
            }), 5000);

            Sit(true);
            // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Pray.Begin"), eChatType.CT_System, eChatLoc.CL_SystemWindow);

            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (player == null)
                    continue;

                player.Out.SendEmoteAnimation(this, eEmote.Pray);
            }
        }

        /// <summary>
        /// Stop praying; used when player changes target
        /// </summary>
        public void PrayTimerStop()
        {
            if (!IsPraying)
                return;
            m_prayAction.Stop();
            m_prayAction = null;
        }

        #endregion Praying

        #endregion release/bind/pray

        #region Stats

        private int _totalConstitutionLostAtDeath = 0;

        /// <summary>
        /// Gets/sets the player efficacy percent
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int TotalConstitutionLostAtDeath
        {
            get { return _totalConstitutionLostAtDeath; }
            set { _totalConstitutionLostAtDeath = value; }
        }

        /// <summary>
        /// Change a stat value
        /// (delegate to PlayerCharacter)
        /// </summary>
        /// <param name="stat">The stat to change</param>
        /// <param name="val">The new value</param>
        public override void ChangeBaseStat(eStat stat, short val)
        {
            base.ChangeBaseStat(stat, val);
        }

        /// <summary>
        /// Gets player's strength
        /// </summary>
        public new int Strength
        {
            get { return (short)GetModified(eProperty.Strength); }
        }

        /// <summary>
        /// Gets player's dexterity
        /// </summary>
        public new int Dexterity
        {
            get { return (short)GetModified(eProperty.Dexterity); }
        }

        /// <summary>
        /// Gets player's constitution
        /// </summary>
        public new int Constitution
        {
            get { return (short)GetModified(eProperty.Constitution); }
        }

        /// <summary>
        /// Gets mimic's quickness
        /// </summary>
        public new int Quickness
        {
            get { return (short)GetModified(eProperty.Quickness); }
        }

        /// <summary>
        /// Gets player's intelligence
        /// </summary>
        public new int Intelligence
        {
            get { return (short)GetModified(eProperty.Intelligence); }
        }

        /// <summary>
        /// Gets player's piety
        /// </summary>
        public new int Piety
        {
            get { return (short)GetModified(eProperty.Piety); }
        }

        /// <summary>
        /// Gets player's empathy
        /// </summary>
        public new int Empathy
        {
            get { return (short)GetModified(eProperty.Empathy); }
        }

        /// <summary>
        /// Gets player's charisma
        /// </summary>
        public new int Charisma
        {
            get { return GetModified(eProperty.Charisma); }
        }

        protected IPlayerStatistics m_statistics = null;

        /// <summary>
        /// Get the statistics for this player
        /// </summary>
        public virtual IPlayerStatistics Statistics
        {
            get { return m_statistics; }
        }

        #endregion Stats

        #region Health/Mana/Endurance/Regeneration

        public override void StartPowerRegeneration()
        {
            if (!IsAlive || ObjectState is not eObjectState.Active)
                return;

            if (m_powerRegenerationTimer == null)
                m_powerRegenerationTimer = new(this, new ECSGameTimer.ECSTimerCallback(PowerRegenerationTimerCallback));
            else if (m_powerRegenerationTimer.IsAlive)
                return;

            m_powerRegenerationTimer.Start(m_powerRegenerationPeriod);
        }

        public override void StartEnduranceRegeneration()
        {
            if (!IsAlive || ObjectState is not eObjectState.Active)
                return;

            if (m_enduRegenerationTimer == null)
                m_enduRegenerationTimer = new(this, new ECSGameTimer.ECSTimerCallback(EnduranceRegenerationTimerCallback));
            else if (m_enduRegenerationTimer.IsAlive)
                return;

            m_enduRegenerationTimer.Start(m_enduranceRegenerationPeriod);
        }

        /// <summary>
        /// Stop the health regeneration.
        /// Overriden. No lazy timers for GamePlayers.
        /// </summary>
        public override void StopHealthRegeneration()
        {
            if (m_healthRegenerationTimer == null)
                return;

            m_healthRegenerationTimer.Stop();
        }

        /// <summary>
        /// Stop the power regeneration.
        /// Overriden. No lazy timers for GamePlayers.
        /// </summary>
        public override void StopPowerRegeneration()
        {
            PowerRegenStackingBonus = 0;
            if (m_powerRegenerationTimer == null)
                return;

            m_powerRegenerationTimer.Stop();
        }

        /// <summary>
        /// Stop the endurance regeneration.
        /// Overriden. No lazy timers for GamePlayers.
        /// </summary>
        public override void StopEnduranceRegeneration()
        {
            if (m_enduRegenerationTimer == null)
                return;

            m_enduRegenerationTimer.Stop();
        }

        protected override int HealthRegenerationTimerCallback(ECSGameTimer callingTimer)
        {
            if (Health < MaxHealth)
                ChangeHealth(this, eHealthChangeType.Regenerate, GetModified(eProperty.HealthRegenerationAmount));

            bool atMaxHealth = Health >= MaxHealth;

            if (DamageRvRMemory > 0)
            {
                if (atMaxHealth)
                    DamageRvRMemory = 0;
                else
                    DamageRvRMemory -= Math.Max(GetModified(eProperty.HealthRegenerationAmount), 0);
            }

            if (atMaxHealth)
            {
                lock (m_xpGainers.SyncRoot)
                {
                    m_xpGainers.Clear();
                }

                return 0;
            }

            if (InCombat)
                return HealthRegenerationPeriod * 2;

            if (IsSitting)
                return HealthRegenerationPeriod / 2;

            return HealthRegenerationPeriod;
        }

        public int PowerRegenStackingBonus { get; set; } = 0;

        /// <summary>
        /// Override PowerRegenTimer because if we are not connected anymore
        /// we DON'T regenerate mana, even if we are not garbage collected yet!
        /// </summary>
        /// <param name="selfRegenerationTimer">the timer</param>
        /// <returns>the new time</returns>
        protected override int PowerRegenerationTimerCallback(ECSGameTimer selfRegenerationTimer)
        {
            if (IsSitting)
            {
                if (PowerRegenStackingBonus < 3)
                    PowerRegenStackingBonus++;
            }
            else
                PowerRegenStackingBonus = 0;

            return base.PowerRegenerationTimerCallback(selfRegenerationTimer);
        }

        /// <summary>
        /// Override EnduranceRegenTimer because if we are not connected anymore
        /// we DON'T regenerate endurance, even if we are not garbage collected yet!
        /// </summary>
        /// <param name="selfRegenerationTimer">the timer</param>
        /// <returns>the new time</returns>
        protected override int EnduranceRegenerationTimerCallback(ECSGameTimer selfRegenerationTimer)
        {
            bool sprinting = IsSprinting;

            if (Endurance < MaxEndurance || sprinting)
            {
                int regen = GetModified(eProperty.EnduranceRegenerationAmount);
                int endChant = GetModified(eProperty.FatigueConsumption);
                ECSGameEffect charge = EffectListService.GetEffectOnTarget(this, eEffect.Charge);
                int longWind = 5;

                if (sprinting && IsMoving)
                {
                    if (charge is null)
                    {
                        AtlasOF_LongWindAbility raLongWind = GetAbility<AtlasOF_LongWindAbility>();

                        if (raLongWind != null)
                            longWind -= raLongWind.GetAmountForLevel(CalculateSkillLevel(raLongWind)) * 5 / 100;

                        regen -= longWind;

                        if (endChant > 1)
                            regen = (int)Math.Ceiling(regen * endChant * 0.01);

                        if (Endurance + regen > MaxEndurance - longWind)
                            regen -= Endurance + regen - (MaxEndurance - longWind);
                    }
                }

                if (regen != 0)
                    ChangeEndurance(this, eEnduranceChangeType.Regenerate, regen);
            }

            if (sprinting)
            {
                if (Endurance - 5 <= 0)
                    Sprint(false);
            }
            else if (Endurance >= MaxEndurance)
                return 0;

            ushort rate = EnduranceRegenerationPeriod;

            if (IsSitting)
                rate /= 2;

            return rate;
        }

        protected bool m_isDead = false;

        /// <summary>
        /// returns if this living is alive
        /// </summary>
        public override bool IsAlive
        {
            //get { return Health > 0; }
            get { return !m_isDead; }
        }

        /// <summary>
        /// Gets/sets the object health
        /// </summary>
        public override int Health
        {
            get { return base.Health; }
            set
            {
                value = Math.Clamp(value, 0, MaxHealth);
                //If it is already set, don't do anything
                if (Health == value)
                {
                    base.Health = value; //needed to start regeneration
                    return;
                }

                int oldPercent = HealthPercent;
                base.Health = value;

                if (m_health == 0)
                    m_isDead = true;

                if (oldPercent != HealthPercent)
                {
                    if (Group != null)
                        Group.UpdateMember(this, false, false);
                    //UpdatePlayerStatus();
                }
            }
        }

        /// <summary>
        /// Calculates the maximum health for a specific playerlevel and constitution
        /// </summary>
        /// <param name="level">The level of the player</param>
        /// <param name="constitution">The constitution of the player</param>
        /// <returns></returns>
        public virtual int CalculateMaxHealth(int level, int constitution)
        {
            constitution -= 50;

            if (constitution < 0)
                constitution *= 2;

            // hp1 : from level
            // hp2 : from constitution
            // hp3 : from champions level
            // hp4 : from artifacts such Spear of Kings charge
            int hp1 = CharacterClass.BaseHP * level;
            int hp2 = hp1 * constitution / 10000;
            int hp3 = 0;
            //if (ChampionLevel >= 1)
            //hp3 = ServerProperties.Properties.HPS_PER_CHAMPIONLEVEL * ChampionLevel;
            double hp4 = 20 + hp1 / 50 + hp2 + hp3;
            if (GetModified(eProperty.ExtraHP) > 0)
                hp4 += Math.Round(hp4 * (double)GetModified(eProperty.ExtraHP) / 100);

            return Math.Max(1, (int)hp4);
        }

        public override byte HealthPercentGroupWindow
        {
            get
            {
                return CharacterClass.HealthPercentGroupWindow;
            }
        }

        /// <summary>
        /// Calculate max mana for this player based on level and mana stat level
        /// </summary>
        public virtual int CalculateMaxMana(int level, int manaStat)
        {
            int maxPower = 0;

            // Special handling for Vampiirs:
            /* There is no stat that affects the Vampiir's power pool or the damage done by its power based spells.
             * The Vampiir is not a focus based class like, say, an Enchanter.
             * The Vampiir is a lot more cut and dried than the typical casting class.
             * EDIT, 12/13/04 - I was told today that this answer is not entirely accurate.
             * While there is no stat that affects the damage dealt (in the way that intelligence or piety affects how much damage a more traditional caster can do),
             * the Vampiir's power pool capacity is intended to be increased as the Vampiir's strength increases.
             *
             * This means that strength ONLY affects a Vampiir's mana pool
             *
             * http://www.camelotherald.com/more/1913.shtml
             * Strength affects the amount of damage done by spells in all of the Vampiir's spell lines.
             * The amount of said affecting was recently increased slightly (fixing a bug), and that minor increase will go live in 1.74 next week.
             *
             * Strength ALSO affects the size of the power pool for a Vampiir sort of.
             * Your INNATE strength (the number of attribute points your character has for strength) has no effect at all.
             * Extra points added through ITEMS, however, does increase the size of your power pool.
             */

            if (CharacterClass.ManaStat is not eStat.UNDEFINED || (eCharacterClass)CharacterClass.ID is eCharacterClass.Vampiir)
                maxPower = Math.Max(5, level * 5 + (manaStat - 50));
            else if (Champion && ChampionLevel > 0)
                maxPower = 100; // This is a guess, need feedback.

            return Math.Max(0, maxPower);
        }

        /// <summary>
        /// Gets/sets the object mana
        /// </summary>
        public override int Mana
        {
            get { return m_mana; }
            set
            {
                int maxMana = MaxMana;
                int mana;
                mana = Math.Min(value, maxMana);
                mana = Math.Max(mana, 0);

                if (IsAlive && (mana < maxMana))
                    StartPowerRegeneration();

                int oldPercent = ManaPercent;
                m_mana = mana;

                if (oldPercent != ManaPercent)
                {
                    if (Group != null)
                        Group.UpdateMember(this, false, false);
                }
            }
        }

        /// <summary>
        /// Gets/sets the object max mana
        /// </summary>
        public override int MaxMana
        {
            get { return GetModified(eProperty.MaxMana); }
        }

        /// <summary>
        /// Gets/sets the object endurance
        /// </summary>
        public override int Endurance
        {
            get { return m_endurance; }
            set
            {
                int endurance;
                endurance = Math.Min(value, MaxEndurance);
                endurance = Math.Max(endurance, 0);

                if (IsAlive && endurance < MaxEndurance)
                    StartEnduranceRegeneration();

                int oldPercent = EndurancePercent;
                m_endurance = endurance;

                if (oldPercent != EndurancePercent)
                {
                    if (Group != null)
                        Group.UpdateMember(this, false, false);
                }
            }
        }

        public override int MaxEndurance
        {
            get { return GetModified(eProperty.Fatigue); }
            set { base.MaxEndurance = value; }
        }

        /// <summary>
        /// Gets the concentration left
        /// </summary>
        public override int Concentration
        {
            get { return MaxConcentration - effectListComponent.UsedConcentration; }
        }

        /// <summary>
        /// Gets the maximum concentration for this player
        /// </summary>
        public override int MaxConcentration
        {
            get { return GetModified(eProperty.MaxConcentration); }
        }

        #region Calculate Fall Damage

        /// <summary>
        /// Calculates fall damage taking fall damage reduction bonuses into account
        /// </summary>
        /// <returns></returns>
        public virtual double CalcFallDamage(int fallDamagePercent)
        {
            if (fallDamagePercent <= 0)
                return 0;

            int safeFallLevel = GetAbilityLevel("Safe Fall");
            int mythSafeFall = GetModified(eProperty.MythicalSafeFall);

            if (mythSafeFall > 0 & mythSafeFall < fallDamagePercent)
            {
                Client.Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "PlayerPositionUpdateHandler.MythSafeFall"), eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);
                fallDamagePercent = mythSafeFall;
            }
            // if (safeFallLevel > 0 & mythSafeFall == 0)
            //  Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "PlayerPositionUpdateHandler.SafeFall"), eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);

            Endurance -= MaxEndurance * fallDamagePercent / 100;
            double damage = (0.01 * fallDamagePercent * (MaxHealth - 1));

            // [Freya] Nidel: CloudSong falling damage reduction
            GameSpellEffect cloudSongFall = SpellHandler.FindEffectOnTarget(this, "CloudsongFall");
            if (cloudSongFall != null)
                damage -= (damage * cloudSongFall.Spell.Value) * 0.01;

            //Mattress: SafeFall property for Mythirians, the value of the MythicalSafeFall property represents the percent damage taken in a fall.
            if (mythSafeFall != 0 && damage > mythSafeFall)
                damage = ((MaxHealth - 1) * (mythSafeFall * 0.01));

            // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "PlayerPositionUpdateHandler.FallingDamage"), eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);
            // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "PlayerPositionUpdateHandler.FallPercent", fallDamagePercent), eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);
            // Out.SendMessage("You lose endurance.", eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);
            TakeDamage(null, eDamageType.Falling, (int)damage, 0);

            //Update the player's health to all other players around
            //foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            //Out.SendCombatAnimation(null, Client.Player, 0, 0, 0, 0, 0, HealthPercent);

            return damage;
        }

        #endregion Calculate Fall Damage

        #endregion Health/Mana/Endurance/Regeneration

        #region Class/Race

        /// <summary>
        /// Gets/sets the player's race name
        /// </summary>
        public virtual string RaceName
        {
            //get { return RaceToTranslatedName(Race, Gender); }
            //get { return string.Format("!{0} - {1}!", ((eRace)Race).ToString("F"), Gender.ToString("F")); }
            get { return ((eRace)Race).ToString("F"); }
        }

        ///// <summary>
        ///// Gets or sets this player's race id
        ///// (delegate to DBCharacter)
        ///// </summary>
        //public override short Race
        //{
        //    get { return m_race; }
        //    set { m_race = value; }
        //}

        //private short m_race;

        /// <summary>
        /// Players class
        /// </summary>
        protected ICharacterClass m_characterClass;

        /// <summary>
        /// Gets the player's character class
        /// </summary>
        public virtual ICharacterClass CharacterClass
        {
            get { return m_characterClass; }
        }

        /// <summary>
        /// Set the character class to a specific one
        /// </summary>
        /// <param name="id">id of the character class</param>
        /// <returns>success</returns>
        public virtual bool SetCharacterClass(int id)
        {
            ICharacterClass cl = ScriptMgr.FindCharacterClass(id);

            if (cl == null)
            {
                if (log.IsErrorEnabled)
                    log.ErrorFormat("No CharacterClass with ID {0} found", id);
                return false;
            }

            m_characterClass = cl;
            m_characterClass.Init(this);

            //DBCharacter.Class = m_characterClass.ID;

            if (Group != null)
            {
                Group.UpdateMember(this, false, true);
            }

            return true;
        }

        /// <summary>
        /// Hold all player face custom attibutes
        /// </summary>
        protected byte[] m_customFaceAttributes = new byte[(int)eCharFacePart._Last + 1];

        /// <summary>
        /// Get the character face attribute you want
        /// </summary>
        /// <param name="part">face part</param>
        /// <returns>attribute</returns>
        public byte GetFaceAttribute(eCharFacePart part)
        {
            return m_customFaceAttributes[(int)part];
        }

        #endregion Class/Race

        #region Spells/Skills/Abilities/Effects

        /// <summary>
        /// Holds the player choosen list of Realm Abilities.
        /// </summary>
        protected readonly ReaderWriterList<RealmAbility> m_realmAbilities = new ReaderWriterList<RealmAbility>();

        /// <summary>
        /// Holds the player specializable skills and style lines
        /// (KeyName -> Specialization)
        /// </summary>
        protected readonly Dictionary<string, Specialization> m_specialization = new Dictionary<string, Specialization>();

        /// <summary>
        /// Holds the Spell lines the player can use
        /// </summary>
        protected readonly List<SpellLine> m_spellLines = new List<SpellLine>();

        /// <summary>
        /// Object to use when locking the SpellLines list
        /// </summary>
        protected readonly Object lockSpellLinesList = new Object();

        ///// <summary>
        ///// Holds all styles of the player
        ///// </summary>
        //protected readonly Dictionary<int, Style> m_styles = new Dictionary<int, Style>();

        ///// <summary>
        ///// Used to lock the style list
        ///// </summary>
        //protected readonly Object lockStyleList = new Object();

        /// <summary>
        /// Temporary Stats Boni
        /// </summary>
        protected readonly int[] m_statBonus = new int[8];

        /// <summary>
        /// Temporary Stats Boni in percent
        /// </summary>
        protected readonly int[] m_statBonusPercent = new int[8];

        /// <summary>
        /// Gets/Sets amount of full skill respecs
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int RespecAmountAllSkill
        {
            get { return DBCharacter != null ? DBCharacter.RespecAmountAllSkill : 0; }
            set { if (DBCharacter != null) DBCharacter.RespecAmountAllSkill = value; }
        }

        /// <summary>
        /// Gets/Sets amount of single-line respecs
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int RespecAmountSingleSkill
        {
            get { return DBCharacter != null ? DBCharacter.RespecAmountSingleSkill : 0; }
            set { if (DBCharacter != null) DBCharacter.RespecAmountSingleSkill = value; }
        }

        /// <summary>
        /// Gets/Sets amount of realm skill respecs
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int RespecAmountRealmSkill
        {
            get { return DBCharacter != null ? DBCharacter.RespecAmountRealmSkill : 0; }
            set { if (DBCharacter != null) DBCharacter.RespecAmountRealmSkill = value; }
        }

        /// <summary>
        /// Gets/Sets amount of DOL respecs
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int RespecAmountDOL
        {
            get { return DBCharacter != null ? DBCharacter.RespecAmountDOL : 0; }
            set { if (DBCharacter != null) DBCharacter.RespecAmountDOL = value; }
        }

        /// <summary>
        /// Gets/Sets level respec usage flag
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual bool IsLevelRespecUsed
        {
            get { return DBCharacter != null ? DBCharacter.IsLevelRespecUsed : true; }
            set { if (DBCharacter != null) DBCharacter.IsLevelRespecUsed = value; }
        }

        protected static readonly int[] m_numRespecsCanBuyOnLevel =
        {
            1,1,1,1,1, //1-5
            2,2,2,2,2,2,2, //6-12
            3,3,3,3, //13-16
            4,4,4,4,4,4, //17-22
            5,5,5,5,5, //23-27
            6,6,6,6,6,6, //28-33
            7,7,7,7,7, //34-38
            8,8,8,8,8,8, //39-44
            9,9,9,9,9, //45-49
            10 //50
        };

        /// <summary>
        /// Can this player buy a respec?
        /// </summary>
        public virtual bool CanBuyRespec
        {
            get
            {
                return (RespecBought < m_numRespecsCanBuyOnLevel[Level - 1]);
            }
        }

        /// <summary>
        /// Gets/Sets amount of bought respecs
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual int RespecBought
        {
            get { return DBCharacter != null ? DBCharacter.RespecBought : 0; }
            set { if (DBCharacter != null) DBCharacter.RespecBought = value; }
        }

        protected static readonly int[] m_respecCost =
        {
            1,2,3, //13
            2,5,9, //14
            3,9,17, //15
            6,16,30, //16
            10,26,48,75, //17
            16,40,72,112, //18
            22,56,102,159, //19
            31,78,140,218, //20
            41,103,187,291, //21
            54,135,243,378, //22
            68,171,308,480,652, //23
            85,214,385,600,814, //24
            105,263,474,738,1001, //25
            128,320,576,896,1216, //26
            153,383,690,1074,1458, //27
            182,455,820,1275,1731,2278, //28
            214,535,964,1500,2036,2679, //29
            250,625,1125,1750,2375,3125, //30
            289,723,1302,2025,2749,3617, //31
            332,831,1497,2329,3161,4159, //32
            380,950,1710,2661,3612,4752, //33
            432,1080,1944,3024,4104,5400,6696, //34
            488,1220,2197,3417,4638,6103,7568, //35
            549,1373,2471,3844,5217,6865,8513, //36
            615,1537,2767,4305,5843,7688,9533, //37
            686,1715,3087,4802,6517,8575,10633, //38
            762,1905,3429,5335,7240,9526,11813,14099, //39
            843,2109,3796,5906,8015,10546,13078,15609, //40
            930,2327,4189,6516,8844,11637,14430,17222, //41
            1024,2560,4608,7168,9728,1280,15872,18944, //42
            1123,2807,5053,7861,10668,14037,17406,20776, //43
            1228,3070,5527,8597,11668,15353,19037,22722, //44
            1339,3349,6029,9378,12725,16748,20767,24787,28806, //45
            1458,3645,6561,10206,13851,18225,22599,26973,31347, //46
            1582,3957,7123,11080,15037,19786,24535,29283,34032, //47
            1714,4286,7716,12003,16290,21434,26578,31722,36867, //48
            1853,4634,8341,12976,17610,23171,28732,34293,39854, //49
            2000,5000,9000,14000,19000,25000,31000,37000,43000,50000 //50
        };

        /// <summary>
        /// How much does this player have to pay for a respec?
        /// </summary>
        public virtual long RespecCost
        {
            get
            {
                if (Level <= 12) //1-12
                    return m_respecCost[0];

                if (CanBuyRespec)
                {
                    int t = 0;
                    for (int i = 13; i < Level; i++)
                    {
                        t += m_numRespecsCanBuyOnLevel[i - 1];
                    }

                    return m_respecCost[t + RespecBought];
                }

                return -1;
            }
        }

        /// <summary>
        /// give player a new Specialization or improve existing one
        /// </summary>
        /// <param name="skill"></param>
        public void AddSpecialization(Specialization skill)
        {
            AddSpecialization(skill, true);
        }

        /// <summary>
        /// give player a new Specialization or improve existing one
        /// </summary>
        /// <param name="skill"></param>
        protected virtual void AddSpecialization(Specialization skill, bool notify)
        {
            if (skill == null)
                return;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(skill.KeyName, out Specialization specialization))
                {
                    specialization.Level = skill.Level;
                    return;
                }

                m_specialization.Add(skill.KeyName, skill);

                if (notify)
                    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.AddSpecialisation.YouLearn", skill.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }

        /// <summary>
        /// Removes the existing specialization from the player
        /// </summary>
        /// <param name="specKeyName">The spec keyname to remove</param>
        /// <returns>true if removed</returns>
        public virtual bool RemoveSpecialization(string specKeyName)
        {
            Specialization playerSpec = null;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (!m_specialization.TryGetValue(specKeyName, out playerSpec))
                    return false;

                m_specialization.Remove(specKeyName);
            }

            return true;
        }

        /// <summary>
        /// Removes the existing spellline from the player, the line instance should be called with GamePlayer.GetSpellLine ONLY and NEVER SkillBase.GetSpellLine!!!!!
        /// </summary>
        /// <param name="line">The spell line to remove</param>
        /// <returns>true if removed</returns>
        protected virtual bool RemoveSpellLine(SpellLine line)
        {
            lock (lockSpellLinesList)
            {
                if (!m_spellLines.Contains(line))
                {
                    return false;
                }

                m_spellLines.Remove(line);
            }

            return true;
        }

        /// <summary>
        /// Removes the existing specialization from the player
        /// </summary>
        /// <param name="lineKeyName">The spell line keyname to remove</param>
        /// <returns>true if removed</returns>
        public virtual bool RemoveSpellLine(string lineKeyName)
        {
            SpellLine line = GetSpellLine(lineKeyName);
            if (line == null)
                return false;

            return RemoveSpellLine(line);
        }

        /// <summary>
        /// Reset this player to level 1, respec all skills, remove all spec points, and reset stats
        /// </summary>
        public virtual void Reset()
        {
            byte originalLevel = Level;
            Level = 1;
            Experience = 0;
            RespecAllLines();

            if (Level < originalLevel && originalLevel > 5)
            {
                for (int i = 6; i <= originalLevel; i++)
                {
                    if (CharacterClass.PrimaryStat != eStat.UNDEFINED)
                    {
                        ChangeBaseStat(CharacterClass.PrimaryStat, -1);
                    }
                    if (CharacterClass.SecondaryStat != eStat.UNDEFINED && ((i - 6) % 2 == 0))
                    {
                        ChangeBaseStat(CharacterClass.SecondaryStat, -1);
                    }
                    if (CharacterClass.TertiaryStat != eStat.UNDEFINED && ((i - 6) % 3 == 0))
                    {
                        ChangeBaseStat(CharacterClass.TertiaryStat, -1);
                    }
                }
            }

            //CharacterClass.OnLevelUp(this, originalLevel);
        }

        /// <summary>
        /// Load this player Classes Specialization.
        /// </summary>
        public virtual void LoadClassSpecializations(bool sendMessage)
        {
            // Get this Attached Class Specialization from SkillBase.
            IDictionary<Specialization, int> careers = SkillBase.GetSpecializationCareer(CharacterClass.ID);

            // Remove All Trainable Specialization or "Career Spec" that aren't managed by This Data Career anymore
            var speclist = GetSpecList();
            var careerslist = careers.Keys.Select(k => k.KeyName.ToLower());

            foreach (var spec in speclist.Where(sp => sp.Trainable || !sp.AllowSave))
            {
                if (!careerslist.Contains(spec.KeyName.ToLower()))
                    RemoveSpecialization(spec.KeyName);
            }

            foreach (KeyValuePair<Specialization, int> constraint in careers)
            {
                if (constraint.Key is IMasterLevelsSpecialization)
                    continue;

                // load if the spec doesn't exists
                if (Level >= constraint.Value)
                {
                    if (!HasSpecialization(constraint.Key.KeyName))
                        AddSpecialization(constraint.Key, sendMessage);
                }
                else
                {
                    if (HasSpecialization(constraint.Key.KeyName))
                        RemoveSpecialization(constraint.Key.KeyName);
                }
            }
        }

        /// <summary>
        /// Verify this player has the correct number of spec points for the players level
        /// </summary>
        public virtual int VerifySpecPoints()
        {
            // calc normal spec points for the level & classe
            int allpoints = -1;
            for (int i = 1; i <= Level; i++)
            {
                if (i <= 5) allpoints += i; //start levels
                if (i > 5) allpoints += CharacterClass.SpecPointsMultiplier * i / 10; //normal levels
                if (i > 40) allpoints += CharacterClass.SpecPointsMultiplier * (i - 1) / 20; //half levels
            }
            if (IsLevelSecondStage && Level != MaxLevel)
                allpoints += CharacterClass.SpecPointsMultiplier * Level / 20; // add current half level

            // calc spec points player have (autotrain is not anymore processed here - 1.87 livelike)
            int usedpoints = 0;
            foreach (Specialization spec in GetSpecList().Where(e => e.Trainable))
            {
                usedpoints += (spec.Level * (spec.Level + 1) - 2) / 2;
                usedpoints -= GetAutoTrainPoints(spec, 0);
            }

            allpoints -= usedpoints;

            // check if correct, if not respec. Not applicable to GMs
            if (allpoints < 0)
            {
                if (Client.Account.PrivLevel == 1)
                {
                    log.WarnFormat("Spec points total for player {0} incorrect: {1} instead of {2}.", Name, usedpoints, allpoints + usedpoints);
                    RespecAllLines();
                    return allpoints + usedpoints;
                }
            }

            return allpoints;
        }

        public virtual bool RespecAll()
        {
            if (RespecAllLines())
            {
                // Wipe skills and styles.
                RespecAmountAllSkill--; // Decriment players respecs available.
                if (Level == 5)
                    IsLevelRespecUsed = true;

                return true;
            }

            return false;
        }

        public virtual bool RespecDOL()
        {
            if (RespecAllLines()) // Wipe skills and styles.
            {
                RespecAmountDOL--; // Decriment players respecs available.
                return true;
            }

            return false;
        }

        public virtual int RespecSingle(Specialization specLine)
        {
            int specPoints = RespecSingleLine(specLine); // Wipe skills and styles.
            if (!ServerProperties.Properties.FREE_RESPEC)
                RespecAmountSingleSkill--; // Decriment players respecs available.
            if (Level == 20 || Level == 40)
            {
                IsLevelRespecUsed = true;
            }
            return specPoints;
        }

        public virtual bool RespecRealm(bool useRespecPoint = true)
        {
            bool any = m_realmAbilities.Count > 0;

            foreach (Ability ab in m_realmAbilities)
                RemoveAbility(ab.KeyName);

            m_realmAbilities.Clear();
            if (!ServerProperties.Properties.FREE_RESPEC && useRespecPoint)
                RespecAmountRealmSkill--;
            return any;
        }

        protected virtual bool RespecAllLines()
        {
            bool ok = false;
            IList<Specialization> specList = GetSpecList().Where(e => e.Trainable).ToList();
            foreach (Specialization cspec in specList)
            {
                if (cspec.Level < 2)
                    continue;
                RespecSingleLine(cspec);
                ok = true;
            }
            return ok;
        }

        /// <summary>
        /// Respec single line
        /// </summary>
        /// <param name="specLine">spec line being respec'd</param>
        /// <returns>Amount of points spent in that line</returns>
        protected virtual int RespecSingleLine(Specialization specLine)
        {
            int specPoints = (specLine.Level * (specLine.Level + 1) - 2) / 2;
            // Graveen - autotrain 1.87
            specPoints -= GetAutoTrainPoints(specLine, 0);

            //setting directly the autotrain points in the spec
            if (GetAutoTrainPoints(specLine, 4) == 1 && Level >= 8)
            {
                specLine.Level = (int)Math.Floor((double)Level / 4);
            }
            else specLine.Level = 1;

            // If BD subpet spells scaled and capped by BD spec, respecing a spell line
            //	requires re-scaling the spells for all subpets from that line.
            if (CharacterClass is CharacterClassBoneDancer
                && ServerProperties.Properties.PET_SCALE_SPELL_MAX_LEVEL > 0
                && ServerProperties.Properties.PET_CAP_BD_MINION_SPELL_SCALING_BY_SPEC
                && ControlledBrain is IControlledBrain brain && brain.Body is GameSummonedPet pet
                && pet.ControlledNpcList != null)
                foreach (ABrain subBrain in pet.ControlledNpcList)
                    if (subBrain != null && subBrain.Body is BdSubPet subPet && subPet.PetSpecLine == specLine.KeyName)
                        subPet.SortSpells();

            return specPoints;
        }

        /// <summary>
        /// Send this players trainer window
        /// </summary>
        public virtual void SendTrainerWindow()
        {
            //Out.SendTrainerWindow();
        }

        /// <summary>
        /// returns a list with all specializations
        /// in the order they were added
        /// </summary>
        /// <returns>list of Spec's</returns>
        public virtual IList<Specialization> GetSpecList()
        {
            List<Specialization> list;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                // sort by Level and ID to simulate "addition" order... (try to sort your DB if you want to change this !)
                list = m_specialization.Select(item => item.Value).OrderBy(it => it.LevelRequired).ThenBy(it => it.ID).ToList();
            }

            return list;
        }

        /// <summary>
        /// returns a list with all non trainable skills without styles
        /// This is a copy of Ability until any unhandled Skill subclass needs to go in there...
        /// </summary>
        /// <returns>list of Skill's</returns>
        public virtual IList GetNonTrainableSkillList()
        {
            return GetAllAbilities();
        }

        /// <summary>
        /// Retrives a specific specialization by name
        /// </summary>
        /// <param name="name">the name of the specialization line</param>
        /// <param name="caseSensitive">false for case-insensitive compare</param>
        /// <returns>found specialization or null</returns>
        public virtual Specialization GetSpecializationByName(string name)
        {
            Specialization spec = null;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (KeyValuePair<string, Specialization> entry in m_specialization)
                {
                    if (entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        spec = entry.Value;
                        break;
                    }
                }
            }

            return spec;
        }

        /// <summary>
        /// The best armor level this player can use.
        /// </summary>
        public virtual int BestArmorLevel
        {
            get
            {
                int bestLevel = -1;
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("AlbArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("HibArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("MidArmor"));
                return bestLevel;
            }
        }

        public virtual int BestShieldLevel { get { return GetAbilityLevel("Shield"); } }

        #region Abilities

        /// <summary>
        /// Adds a new Ability to the player
        /// </summary>
        /// <param name="ability"></param>
        /// <param name="sendUpdates"></param>
        public override void AddAbility(Ability ability, bool sendUpdates)
        {
            if (ability == null)
                return;

            base.AddAbility(ability, sendUpdates);
        }

        /// <summary>
        /// Adds a Realm Ability to the player
        /// </summary>
        /// <param name="ability"></param>
        /// <param name="sendUpdates"></param>
        public virtual void AddRealmAbility(RealmAbility ability, bool sendUpdates)
        {
            if (ability == null)
                return;

            m_realmAbilities.FreezeWhile(list =>
            {
                int index = list.FindIndex(ab => ab.ID == ability.ID);
                if (index > -1)
                {
                    list[index].Level = ability.Level;
                }
                else
                {
                    list.Add(ability);
                }
            });

            RefreshSpecDependantSkills(false);
        }

        #endregion Abilities

        public virtual void RemoveAllAbilities()
        {
            lock (m_lockAbilities)
            {
                m_abilities.Clear();
            }
        }

        public virtual void RemoveAllSpecs()
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                m_specialization.Clear();
            }
        }

        public virtual void RemoveAllSpellLines()
        {
            lock (lockSpellLinesList)
            {
                m_spellLines.Clear();
            }
        }

        /// <summary>
        /// Retrieve this player Realm Abilities.
        /// </summary>
        /// <returns></returns>
        public virtual List<RealmAbility> GetRealmAbilities()
        {
            return m_realmAbilities.ToList();
        }

        /// <summary>
        /// Asks for existance of specific specialization
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        public virtual bool HasSpecialization(string keyName)
        {
            bool hasit = false;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                hasit = m_specialization.ContainsKey(keyName);
            }

            return hasit;
        }

        /// <summary>
        /// returns the level of a specialization
        /// if 0 is returned, the spec is non existent on player
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        public override int GetBaseSpecLevel(string keyName)
        {
            Specialization spec = null;
            int level = 0;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(keyName, out spec))
                    level = m_specialization[keyName].Level;
            }

            return level;
        }

        /// <summary>
        /// returns the level of a specialization + bonuses from RR and Items
        /// if 0 is returned, the spec is non existent on the player
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        public override int GetModifiedSpecLevel(string keyName)
        {
            if (keyName.StartsWith(GlobalSpellsLines.Champion_Lines_StartWith))
                return 50;

            if (keyName.StartsWith(GlobalSpellsLines.Realm_Spells))
                return Level;

            Specialization spec = null;
            int level = 0;
            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (!m_specialization.TryGetValue(keyName, out spec))
                {
                    if (keyName == GlobalSpellsLines.Combat_Styles_Effect)
                    {
                        if (CharacterClass.ID == (int)eCharacterClass.Reaver || CharacterClass.ID == (int)eCharacterClass.Heretic)
                            level = GetModifiedSpecLevel(Specs.Flexible);
                        if (CharacterClass.ID == (int)eCharacterClass.Valewalker)
                            level = GetModifiedSpecLevel(Specs.Scythe);
                        if (CharacterClass.ID == (int)eCharacterClass.Savage)
                            level = GetModifiedSpecLevel(Specs.Savagery);
                    }

                    level = 0;
                }
            }

            if (spec != null)
            {
                level = spec.Level;
                // TODO: should be all in calculator later, right now
                // needs specKey -> eProperty conversion to find calculator and then
                // needs eProperty -> specKey conversion to find how much points player has spent
                eProperty skillProp = SkillBase.SpecToSkill(keyName);
                if (skillProp != eProperty.Undefined)
                    level += GetModified(skillProp);
            }

            return level;
        }

        /// <summary>
        /// Adds a spell line to the player
        /// </summary>
        /// <param name="line"></param>
        public virtual void AddSpellLine(SpellLine line)
        {
            AddSpellLine(line, true);
        }

        /// <summary>
        /// Adds a spell line to the player
        /// </summary>
        /// <param name="line"></param>
        public virtual void AddSpellLine(SpellLine line, bool notify)
        {
            if (line == null)
                return;

            SpellLine oldline = GetSpellLine(line.KeyName);
            if (oldline == null)
            {
                lock (lockSpellLinesList)
                {
                    m_spellLines.Add(line);
                }
            }
            else
            {
                oldline.Level = line.Level;
            }
        }

        /// <summary>
        /// return a list of spell lines in the order they were added
        /// this is a copy only.
        /// </summary>
        /// <returns></returns>
        public virtual List<SpellLine> GetSpellLines()
        {
            List<SpellLine> list = new List<SpellLine>();
            lock (lockSpellLinesList)
            {
                list = new List<SpellLine>(m_spellLines);
            }

            return list;
        }

        /// <summary>
        /// find a spell line on player and return them
        /// </summary>
        /// <param name="keyname"></param>
        /// <returns></returns>
        public virtual SpellLine GetSpellLine(string keyname)
        {
            lock (lockSpellLinesList)
            {
                foreach (SpellLine line in m_spellLines)
                {
                    if (line.KeyName == keyname)
                        return line;
                }
            }
            return null;
        }

        /// <summary>
        /// Skill cache, maintained for network order on "skill use" request...
        /// Second item is for "Parent" Skill if applicable
        /// </summary>
        protected ReaderWriterList<Tuple<Skill, Skill>> m_usableSkills = new ReaderWriterList<Tuple<Skill, Skill>>();

        /// <summary>
        /// List Cast cache, maintained for network order on "spell use" request...
        /// Second item is for "Parent" SpellLine if applicable
        /// </summary>
        protected ReaderWriterList<Tuple<SpellLine, List<Skill>>> m_usableListSpells = new ReaderWriterList<Tuple<SpellLine, List<Skill>>>();

        /// <summary>
        /// Get All Usable Spell for a list Caster.
        /// </summary>
        /// <param name="update"></param>
        /// <returns></returns>
        public virtual List<Tuple<SpellLine, List<Skill>>> GetAllUsableListSpells(bool update = false)
        {
            List<Tuple<SpellLine, List<Skill>>> results = new List<Tuple<SpellLine, List<Skill>>>();

            if (!update)
            {
                if (m_usableListSpells.Count > 0)
                    results = new List<Tuple<SpellLine, List<Skill>>>(m_usableListSpells);

                // return results if cache is valid.
                if (results.Count > 0)
                    return results;
            }

            // lock during all update, even if replace only take place at end...
            m_usableListSpells.FreezeWhile(innerList =>
            {
                List<Tuple<SpellLine, List<Skill>>> finalbase = new List<Tuple<SpellLine, List<Skill>>>();
                List<Tuple<SpellLine, List<Skill>>> finalspec = new List<Tuple<SpellLine, List<Skill>>>();

                // Add Lists spells ordered.
                foreach (Specialization spec in GetSpecList().Where(item => !item.HybridSpellList))
                {
                    var spells = spec.GetLinesSpellsForLiving(this);

                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        List<Tuple<SpellLine, List<Skill>>> working;
                        if (sl.IsBaseLine)
                        {
                            working = finalbase;
                        }
                        else
                        {
                            working = finalspec;
                        }

                        List<Skill> sps = new List<Skill>();
                        SpellLine key = spells.Keys.FirstOrDefault(el => el.ID == sl.ID);

                        if (key != null && spells.TryGetValue(key, out List<Skill> spellsInLine))
                        {
                            foreach (Skill sp in spellsInLine)
                                sps.Add(sp);
                        }

                        working.Add(new Tuple<SpellLine, List<Skill>>(sl, sps));
                    }
                }

                // Linq isn't used, we need to keep order ! (SelectMany, GroupBy, ToDictionary can't be used !)
                innerList.Clear();
                foreach (var tp in finalbase)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }

                foreach (var tp in finalspec)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }
            });

            return results;
        }

        /// <summary>
        /// Get All Player Usable Skill Ordered in Network Order (usefull to check for useskill)
        /// This doesn't get player's List Cast Specs...
        /// </summary>
        /// <param name="update"></param>
        /// <returns></returns>
        public virtual List<Tuple<Skill, Skill>> GetAllUsableSkills(bool update = false)
        {
            List<Tuple<Skill, Skill>> results = new List<Tuple<Skill, Skill>>();

            if (!update)
            {
                if (m_usableSkills.Count > 0)
                    results = new List<Tuple<Skill, Skill>>(m_usableSkills);

                // return results if cache is valid.
                if (results.Count > 0)
                    return results;
            }

            // need to lock for all update.
            m_usableSkills.FreezeWhile(innerList =>
            {
                IList<Specialization> specs = GetSpecList();
                List<Tuple<Skill, Skill>> copylist = new List<Tuple<Skill, Skill>>(innerList);

                // Add Spec
                foreach (Specialization spec in specs.Where(item => item.Trainable))
                {
                    int index = innerList.FindIndex(e => (e.Item1 is Specialization specialization) && specialization.ID == spec.ID);

                    if (index < 0)
                    {
                        // Specs must be appended to spec list
                        innerList.Insert(innerList.Count(e => e.Item1 is Specialization), new Tuple<Skill, Skill>(spec, spec));
                    }
                    else
                    {
                        copylist.Remove(innerList[index]);
                        // Replace...
                        innerList[index] = new Tuple<Skill, Skill>(spec, spec);
                    }
                }

                // Add Abilities (Realm ability should be a custom spec)
                // Abilities order should be saved to db and loaded each time
                foreach (Specialization spec in specs)
                {
                    foreach (Ability abv in spec.GetAbilitiesForLiving(this))
                    {
                        // We need the Instantiated Ability Object for Displaying Correctly According to Player "Activation" Method (if Available)
                        Ability ab = GetAbility(abv.KeyName);

                        if (ab == null)
                            ab = abv;

                        int index = innerList.FindIndex(k => (k.Item1 is Ability ability) && ability.ID == ab.ID);

                        if (index < 0)
                        {
                            // add
                            innerList.Add(new Tuple<Skill, Skill>(ab, spec));
                        }
                        else
                        {
                            copylist.Remove(innerList[index]);
                            // replace
                            innerList[index] = new Tuple<Skill, Skill>(ab, spec);
                        }
                    }
                }

                // Add Hybrid spells
                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (KeyValuePair<SpellLine, List<Skill>> sl in spec.GetLinesSpellsForLiving(this))
                    {
                        int index = -1;

                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && !((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (index < innerList.Count)
                                index = innerList.FindIndex(index + 1, e => (e.Item2 is SpellLine spellLine) && spellLine.ID == sl.Key.ID && (e.Item1 is Spell spell) && !spell.NeedInstrument);

                            if (index < 0 || index >= innerList.Count)
                            {
                                // add
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                // disable replace
                                index = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[index]);
                                // replace
                                innerList[index] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                // Add Songs
                int songIndex = -1;
                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (KeyValuePair<SpellLine, List<Skill>> sl in spec.GetLinesSpellsForLiving(this))
                    {
                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && ((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (songIndex < innerList.Count)
                                songIndex = innerList.FindIndex(songIndex + 1, e => (e.Item1 is Spell) && ((Spell)e.Item1).NeedInstrument);

                            if (songIndex < 0 || songIndex >= innerList.Count)
                            {
                                // add
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                // disable replace
                                songIndex = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[songIndex]);
                                // replace
                                innerList[songIndex] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                // Add Styles
                foreach (Specialization spec in specs)
                {
                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        int index = innerList.FindIndex(e => (e.Item1 is Style) && e.Item1.ID == st.ID);
                        if (index < 0)
                        {
                            // add
                            innerList.Add(new Tuple<Skill, Skill>(st, spec));
                        }
                        else
                        {
                            copylist.Remove(innerList[index]);
                            // replace
                            innerList[index] = new Tuple<Skill, Skill>(st, spec);
                        }
                    }
                }

                // clean all not re-enabled skills
                foreach (Tuple<Skill, Skill> item in copylist)
                {
                    innerList.Remove(item);
                }

                foreach (Tuple<Skill, Skill> el in innerList)
                    results.Add(el);
            });

            return results;
        }

        /// <summary>
        /// updates the list of available skills (dependent on caracter specs)
        /// </summary>
        /// <param name="sendMessages">sends "you learn" messages if true</param>
        public virtual void RefreshSpecDependantSkills(bool sendMessages)
        {
            // refresh specs
            LoadClassSpecializations(sendMessages);

            // lock specialization while refreshing...
            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (Specialization spec in m_specialization.Values)
                {
                    // check for new Abilities
                    foreach (Ability ab in spec.GetAbilitiesForLiving(this))
                    {
                        if (!HasAbility(ab.KeyName) || GetAbility(ab.KeyName).Level < ab.Level)
                            AddAbility(ab, false);
                    }

                    // check for new Styles
                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        if (st.SpecLevelRequirement == 2 && Level > 5)
                        {
                            if (Styles.Contains(st))
                            {
                                log.Info("Removed base style.");
                                Styles.Remove(st);
                            }

                            continue;
                        }

                        AddStyle(st, false);
                    }

                    // check for new SpellLine
                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        AddSpellLine(sl, false);
                    }
                }
            }

            MimicBrain?.OnRefreshSpecDependantSkills();
        }

        public virtual void AddStyle(Style st, bool notify)
        {
            lock (Styles)
            {
                if (!Styles.Contains(st))
                {
                    Styles.Add(st);
                }
            }

            Styles = Styles;
        }

        /// <summary>
        /// Called by trainer when specialization points were added to a skill
        /// </summary>
        /// <param name="skill"></param>
        public void OnSkillTrained(Specialization skill)
        {
            //CharacterClass.OnSkillTrained(this, skill);
            RefreshSpecDependantSkills(false);
        }

        /// <summary>
        /// effectiveness of the player (resurrection illness)
        /// Effectiveness is used in physical/magic damage (exept dot), in weapon skill and max concentration formula
        /// </summary>
        protected double m_playereffectiveness = 1.0;

        /// <summary>
        /// get / set the player's effectiveness.
        /// Effectiveness is used in physical/magic damage (exept dot), in weapon skill and max concentration
        /// </summary>
        public override double Effectiveness
        {
            get { return m_playereffectiveness; }
            set { m_playereffectiveness = value; }
        }

        /// <summary>
        /// Creates new effects list for this living.
        /// </summary>
        /// <returns>New effects list instance</returns>
        //protected override GameEffectList CreateEffectsList()
        //{
        //    return new GameEffectPlayerList(this);
        //}

        #endregion Spells/Skills/Abilities/Effects

        #region Realm-/Region-/Bount-/Skillpoints...

        private long m_bountyPoints;

        /// <summary>
        /// Gets/sets player bounty points
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual long BountyPoints
        {
            get { return m_bountyPoints; }
            set { m_bountyPoints = value; }
        }

        private long m_realmPoints;

        /// <summary>
        /// Gets/sets player realm points
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual long RealmPoints
        {
            get { return m_realmPoints; }
            set { m_realmPoints = value; }
        }

        /// <summary>
        /// Gets/sets player skill specialty points
        /// </summary>
        public virtual int SkillSpecialtyPoints
        {
            get { return VerifySpecPoints(); }
        }

        /// <summary>
        /// Gets/sets player realm specialty points
        /// </summary>
        //public virtual int RealmSpecialtyPoints
        //{
        //    get
        //    {
        //        return GameServer.ServerRules.GetPlayerRealmPointsTotal(this)
        //                 - GetRealmAbilities().Where(ab => !(ab is RR5RealmAbility))
        //                     .Sum(ab => Enumerable.Range(0, ab.Level).Sum(i => ab.CostForUpgrade(i)));
        //    }
        //}

        private int _realmLevel;

        /// <summary>
        /// Gets/sets player realm rank
        /// </summary>
        public virtual int RealmLevel
        {
            get { return _realmLevel; }
            set { _realmLevel = value; }
        }

        /// <summary>
        /// Returns the translated realm rank title of the player.
        /// </summary>
        /// <param name="language"></param>
        /// <returns></returns>
        public virtual string RealmRankTitle(string language)
        {
            string translationId = string.Empty;

            if (Realm != eRealm.None && Realm != eRealm.Door)
            {
                int RR = 0;

                if (RealmLevel > 1)
                    RR = RealmLevel / 10 + 1;

                string realm = string.Empty;
                if (Realm == eRealm.Albion)
                    realm = "Albion";
                else if (Realm == eRealm.Midgard)
                    realm = "Midgard";
                else
                    realm = "Hibernia";

                string gender = Gender == eGender.Female ? "Female" : "Male";

                translationId = string.Format("{0}.RR{1}.{2}", realm, RR, gender);
            }
            else
            {
                translationId = "UnknownRealm";
            }

            string translation;
            if (!LanguageMgr.TryGetTranslation(out translation, language, string.Format("GamePlayer.RealmTitle.{0}", translationId)))
                translation = RealmTitle;

            return translation;
        }

        /// <summary>
        /// Gets player realm rank name
        /// sirru mod 20.11.06
        /// </summary>
        public virtual string RealmTitle
        {
            get
            {
                if (Realm == eRealm.None)
                    return "Unknown Realm";

                try
                {
                    return GlobalConstants.REALM_RANK_NAMES[(int)Realm - 1, (int)Gender - 1, (RealmLevel / 10)];
                }
                catch
                {
                    return "Unknown Rank"; // why aren't all the realm ranks defined above?
                }
            }
        }

        /// <summary>
        /// Called when this player gains realm points
        /// </summary>
        /// <param name="amount">The amount of realm points gained</param>
        public override void GainRealmPoints(long amount)
        {
            GainRealmPoints(amount, true, true);
        }

        /// <summary>
        /// Called when this living gains realm points
        /// </summary>
        /// <param name="amount">The amount of realm points gained</param>
        public void GainRealmPoints(long amount, bool modify)
        {
            GainRealmPoints(amount, modify, true);
        }

        /// <summary>
        /// Called when this player gains realm points
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="modify"></param>
        /// <param name="sendMessage"></param>
        public void GainRealmPoints(long amount, bool modify, bool sendMessage)
        {
            GainRealmPoints(amount, modify, true, true);
        }

        /// <summary>
        /// Called when this player gains realm points
        /// </summary>
        /// <param name="amount">The amount of realm points gained</param>
        /// <param name="modify">Should we apply the rp modifer</param>
        /// <param name="sendMessage">Wether to send a message like "You have gained N realmpoints"</param>
        /// <param name="notify"></param>
        public virtual void GainRealmPoints(long amount, bool modify, bool sendMessage, bool notify)
        {
            if (!GainRP)
                return;

            if (modify)
            {
                //rp rate modifier
                double modifier = ServerProperties.Properties.RP_RATE;
                if (modifier != -1)
                    amount = (long)(amount * modifier);

                //[StephenxPimente]: Zone Bonus Support
                if (ServerProperties.Properties.ENABLE_ZONE_BONUSES)
                {
                    //int zoneBonus = (((int)amount * ZoneBonus.GetRPBonus(this)) / 100);
                    //if (zoneBonus > 0)
                    //{
                    //   /Out.SendMessage(ZoneBonus.GetBonusMessage(this, (int)(zoneBonus * ServerProperties.Properties.RP_RATE), ZoneBonus.eZoneBonusType.RP),
                    //        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    //    GainRealmPoints((long)(zoneBonus * ServerProperties.Properties.RP_RATE), false, false, false);
                    //}
                }

                //[Freya] Nidel: ToA Rp Bonus
                long rpBonus = GetModified(eProperty.RealmPoints);
                if (rpBonus > 0)
                {
                    amount += (amount * rpBonus) / 100;
                }

                #region Kill Streak

                int killBonus = (int)((0.05 * (KillStreak > 10 ? 10 : KillStreak)) * amount);
                //Out.SendMessage($"{KillStreak} kill streak! ({killBonus} RPs)", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                amount += killBonus;

                #endregion Kill Streak
            }

            if (notify)
                base.GainRealmPoints(amount);

            RealmPoints += amount;

            //if (m_guild != null && Client.Account.PrivLevel == 1)
            //    m_guild.RealmPoints += amount;

            if (sendMessage == true && amount > 0)
                while (RealmPoints >= CalculateRPsFromRealmLevel(RealmLevel + 1) && RealmLevel < (REALMPOINTS_FOR_LEVEL.Length - 1))
                {
                    RealmLevel++;

                    if (RealmLevel % 10 == 0)
                    {
                        foreach (GamePlayer plr in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                            plr.Out.SendLivingDataUpdate(this, true);

                        Notify(GamePlayerEvent.RRLevelUp, this);
                    }
                    else
                        Notify(GamePlayerEvent.RLLevelUp, this);
                    //if (CanGenerateNews && ((RealmLevel >= 40 && RealmLevel % 10 == 0) || RealmLevel >= 60))
                    //{
                    //    string message = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainRealmPoints.ReachedRankNews", Name, RealmLevel + 10, LastPositionUpdateZone.Description);
                    //    string newsmessage = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainRealmPoints.ReachedRankNews", Name, RealmLevel + 10, LastPositionUpdateZone.Description);
                    //    NewsMgr.CreateNews(newsmessage, Realm, eNewsType.RvRLocal, true);
                    //}
                    //if (CanGenerateNews && RealmPoints >= 1000000 && RealmPoints - amount < 1000000)
                    //{
                    //    string message = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainRealmPoints.Earned", Name, LastPositionUpdateZone.Description);
                    //    string newsmessage = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainRealmPoints.Earned", Name, LastPositionUpdateZone.Description);
                    //    NewsMgr.CreateNews(newsmessage, Realm, eNewsType.RvRLocal, true);
                    //}
                }

            //if (GetAchievementProgress(AchievementUtils.AchievementNames.Realm_Rank) <= (int)Math.Floor((double)(RealmLevel + 10.0) / 10.0))
            //{
            //    SetAchievementTo(AchievementUtils.AchievementNames.Realm_Rank, (int)Math.Floor((double)(RealmLevel + 10.0) / 10.0));
            //}

            //Out.SendUpdatePoints();
        }

        /// <summary>
        /// Called when this living buy something with realm points
        /// </summary>
        /// <param name="amount">The amount of realm points loosed</param>
        public bool RemoveBountyPoints(long amount)
        {
            return RemoveBountyPoints(amount, null);
        }

        /// <summary>
        /// Called when this living buy something with realm points
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="str"></param>
        /// <returns></returns>
        public bool RemoveBountyPoints(long amount, string str)
        {
            return RemoveBountyPoints(amount, str, eChatType.CT_Say, eChatLoc.CL_SystemWindow);
        }

        /// <summary>
        /// Called when this living buy something with realm points
        /// </summary>
        /// <param name="amount">The amount of realm points loosed</param>
        /// <param name="loc">The chat location</param>
        /// <param name="str">The message</param>
        /// <param name="type">The chat type</param>
        public virtual bool RemoveBountyPoints(long amount, string str, eChatType type, eChatLoc loc)
        {
            if (BountyPoints < amount)
                return false;
            BountyPoints -= amount;
            //Out.SendUpdatePoints();
            // if (str != null && amount != 0)
            //     Out.SendMessage(str, type, loc);
            return true;
        }

        /// <summary>
        /// Player gains bounty points
        /// </summary>
        /// <param name="amount">The amount of bounty points</param>
        public override void GainBountyPoints(long amount)
        {
            GainBountyPoints(amount, true, true);
        }

        /// <summary>
        /// Player gains bounty points
        /// </summary>
        /// <param name="amount">The amount of bounty points</param>
        public void GainBountyPoints(long amount, bool modify)
        {
            GainBountyPoints(amount, modify, true);
        }

        /// <summary>
        /// Called when player gains bounty points
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="modify"></param>
        /// <param name="sendMessage"></param>
        public void GainBountyPoints(long amount, bool modify, bool sendMessage)
        {
            GainBountyPoints(amount, modify, true, true);
        }

        /// <summary>
        /// Called when player gains bounty points
        /// </summary>
        /// <param name="amount">The amount of bounty points gained</param>
        /// <param name="multiply">Should this amount be multiplied by the BP Rate</param>
        /// <param name="sendMessage">Wether to send a message like "You have gained N bountypoints"</param>
        public virtual void GainBountyPoints(long amount, bool modify, bool sendMessage, bool notify)
        {
            if (modify)
            {
                //bp rate modifier
                double modifier = ServerProperties.Properties.BP_RATE;
                if (modifier != -1)
                    amount = (long)(amount * modifier);

                //[StephenxPimente]: Zone Bonus Support
                if (ServerProperties.Properties.ENABLE_ZONE_BONUSES)
                {
                    //int zoneBonus = (((int)amount * ZoneBonus.GetBPBonus(this)) / 100);
                    //if (zoneBonus > 0)
                    //{
                    //    //Out.SendMessage(ZoneBonus.GetBonusMessage(this, (int)(zoneBonus * ServerProperties.Properties.BP_RATE), ZoneBonus.eZoneBonusType.BP),
                    //        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    //    GainBountyPoints((long)(zoneBonus * ServerProperties.Properties.BP_RATE), false, false, false);
                    //}
                }

                //[Freya] Nidel: ToA Bp Bonus
                long bpBonus = GetModified(eProperty.BountyPoints);

                if (bpBonus > 0)
                {
                    amount += (amount * bpBonus) / 100;
                }
            }

            if (notify)
                base.GainBountyPoints(amount);

            BountyPoints += amount;

            //if (m_guild != null && Client.Account.PrivLevel == 1)
            //    m_guild.BountyPoints += amount;

            ///if (sendMessage == true)
               // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainBountyPoints.YouGet", amount.ToString()), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            //Out.SendUpdatePoints();
        }

        /// <summary>
        /// Holds realm points needed for special realm level
        /// </summary>
        public static readonly long[] REALMPOINTS_FOR_LEVEL =
        {
            0,	// for level 0
            0,	// for level 1
            25,	// for level 2
            125,	// for level 3
            350,	// for level 4
            750,	// for level 5
            1375,	// for level 6
            2275,	// for level 7
            3500,	// for level 8
            5100,	// for level 9
            7125,	// for level 10
            9625,	// for level 11
            12650,	// for level 12
            16250,	// for level 13
            20475,	// for level 14
            25375,	// for level 15
            31000,	// for level 16
            37400,	// for level 17
            44625,	// for level 18
            52725,	// for level 19
            61750,	// for level 20
            71750,	// for level 21
            82775,	// for level 22
            94875,	// for level 23
            108100,	// for level 24
            122500,	// for level 25
            138125,	// for level 26
            155025,	// for level 27
            173250,	// for level 28
            192850,	// for level 29
            213875,	// for level 30
            236375,	// for level 31
            260400,	// for level 32
            286000,	// for level 33
            313225,	// for level 34
            342125,	// for level 35
            372750,	// for level 36
            405150,	// for level 37
            439375,	// for level 38
            475475,	// for level 39
            513500,	// for level 40
            553500,	// for level 41
            595525,	// for level 42
            639625,	// for level 43
            685850,	// for level 44
            734250,	// for level 45
            784875,	// for level 46
            837775,	// for level 47
            893000,	// for level 48
            950600,	// for level 49
            1010625,	// for level 50
            1073125,	// for level 51
            1138150,	// for level 52
            1205750,	// for level 53
            1275975,	// for level 54
            1348875,	// for level 55
            1424500,	// for level 56
            1502900,	// for level 57
            1584125,	// for level 58
            1668225,	// for level 59
            1755250,	// for level 60
            1845250,	// for level 61
            1938275,	// for level 62
            2034375,	// for level 63
            2133600,	// for level 64
            2236000,	// for level 65
            2341625,	// for level 66
            2450525,	// for level 67
            2562750,	// for level 68
            2678350,	// for level 69
            2797375,	// for level 70
            2919875,	// for level 71
            3045900,	// for level 72
            3175500,	// for level 73
            3308725,	// for level 74
            3445625,	// for level 75
            3586250,	// for level 76
            3730650,	// for level 77
            3878875,	// for level 78
            4030975,	// for level 79
            4187000,	// for level 80
            4347000,	// for level 81
            4511025,	// for level 82
            4679125,	// for level 83
            4851350,	// for level 84
            5027750,	// for level 85
            5208375,	// for level 86
            5393275,	// for level 87
            5582500,	// for level 88
            5776100,	// for level 89
            5974125,	// for level 90
            6176625,	// for level 91
            6383650,	// for level 92
            6595250,	// for level 93
            6811475,	// for level 94
            7032375,	// for level 95
            7258000,	// for level 96
            7488400,	// for level 97
            7723625,	// for level 98
            7963725,	// for level 99
            8208750,	// for level 100
            9111713,	// for level 101
            10114001,	// for level 102
            11226541,	// for level 103
            12461460,	// for level 104
            13832221,	// for level 105
            15353765,	// for level 106
            17042680,	// for level 107
            18917374,	// for level 108
            20998286,	// for level 109
            23308097,	// for level 110
            25871988,	// for level 111
            28717906,	// for level 112
            31876876,	// for level 113
            35383333,	// for level 114
            39275499,	// for level 115
            43595804,	// for level 116
            48391343,	// for level 117
            53714390,	// for level 118
            59622973,	// for level 119
            66181501,	// for level 120
            73461466,	// for level 121
            81542227,	// for level 122
            90511872,	// for level 123
            100468178,	// for level 124
            111519678,	// for level 125
            123786843,	// for level 126
            137403395,	// for level 127
            152517769,	// for level 128
            169294723,	// for level 129
            187917143,	// for level 130
        };

        /// <summary>
        /// Calculates amount of RealmPoints needed for special realm level
        /// </summary>
        /// <param name="realmLevel">realm level</param>
        /// <returns>amount of realm points</returns>
        protected virtual long CalculateRPsFromRealmLevel(int realmLevel)
        {
            if (realmLevel < REALMPOINTS_FOR_LEVEL.Length)
                return REALMPOINTS_FOR_LEVEL[realmLevel];

            // thanks to Linulo from http://daoc.foren.4players.de/viewtopic.php?t=40839&postdays=0&postorder=asc&start=0
            return (long)(25.0 / 3.0 * (realmLevel * realmLevel * realmLevel) - 25.0 / 2.0 * (realmLevel * realmLevel) + 25.0 / 6.0 * realmLevel);
        }

        /// <summary>
        /// Calculates realm level from realm points. SLOW.
        /// </summary>
        /// <param name="realmPoints">amount of realm points</param>
        /// <returns>realm level: RR5L3 = 43, RR1L2 = 2</returns>
        protected virtual int CalculateRealmLevelFromRPs(long realmPoints)
        {
            if (realmPoints == 0)
                return 0;

            int i;

            for (i = REALMPOINTS_FOR_LEVEL.Length - 1; i > 0; i--)
            {
                if (REALMPOINTS_FOR_LEVEL[i] <= realmPoints)
                    break;
            }

            return i;
        }

        /// <summary>
        /// Realm point value of this player
        /// </summary>
        public override int RealmPointsValue
        {
            get
            {
                // http://www.camelotherald.com/more/2275.shtml
                // new 1.81D formula
                // Realm point value = (level - 20)squared + (realm rank level x 5) + (champion level x 10) + (master level (squared)x 5)
                //we use realm level 1L0 = 0, mythic uses 1L0 = 10, so we + 10 the realm level
                int level = Math.Max(0, Level - 20);
                if (level == 0)
                    return Math.Max(1, (RealmLevel + 10) * 5);

                return Math.Max(1, level * level + (RealmLevel + 10) * 5);
            }
        }

        /// <summary>
        /// Bounty point value of this player
        /// </summary>
        public override int BountyPointsValue
        {
            // TODO: correct formula!
            get { return (int)(1 + Level * 0.6); }
        }

        /// <summary>
        /// Returns the amount of experience this player is worth
        /// </summary>
        public override long ExperienceValue
        {
            get
            {
                return base.ExperienceValue * 4;
            }
        }

        public static readonly int[] prcRestore =
        {
            // http://www.silicondragon.com/Gaming/DAoC/Misc/XPs.htm
            1,//0
            3,//1
            6,//2
            10,//3
            15,//4
            21,//5
            33,//6
            53,//7
            82,//8
            125,//9
            188,//10
            278,//11
            352,//12
            443,//13
            553,//14
            688,//15
            851,//16
            1048,//17
            1288,//18
            1578,//19
            1926,//20
            2347,//21
            2721,//22
            3146,//23
            3633,//24
            4187,//25
            4820,//26
            5537,//27
            6356,//28
            7281,//29
            8337,//30
            9532,//31 - from logs
            10886,//32 - from logs
            12421,//33 - from logs
            14161,//34
            16131,//35
            18360,//36 - recheck
            19965,//37 - guessed
            21857,//38
            23821,//39
            25928,//40 - guessed
            28244,//41
            30731,//42
            33411,//43
            36308,//44
            39438,//45
            42812,//46
            46454,//47
            50385,//48
            54625,//49
            59195,//50
        };

        /// <summary>
        /// Money value of this player
        /// </summary>
        public override long MoneyValue
        {
            get
            {
                return 5 * prcRestore[Level < GamePlayer.prcRestore.Length ? Level : GamePlayer.prcRestore.Length - 1];
            }
        }

        #endregion Realm-/Region-/Bount-/Skillpoints...

        #region Level/Experience/Champ

        private bool _champion;
        /// <summary>
        /// Is Champion level activated
        /// </summary>
        public virtual bool Champion
        {
            get { return false; }
            set { _champion = value; }
        }

        private int _championLevel;
        /// <summary>
        /// Champion level
        /// </summary>
        public virtual int ChampionLevel
        {
            get { return 0; }
            set { _championLevel = value; }
        }

        /// <summary>
        /// What is the maximum level a player can achieve?
        /// To alter this in a custom GamePlayer class you must override this method and
        /// provide your own XPForLevel array with MaxLevel + 1 entries
        /// </summary>
        public virtual byte MaxLevel
        {
            get { return 50; }
        }

        /// <summary>
        /// How much experience is needed for a given level?
        /// </summary>
        public virtual long GetExperienceNeededForLevel(int level)
        {
            if (level > MaxLevel)
                return GetScaledExperienceAmountForLevel(MaxLevel);

            if (level <= 0)
                return GetScaledExperienceAmountForLevel(0);

            return GetScaledExperienceAmountForLevel(level - 1);
        }

        /// <summary>
        /// How Much Experience Needed For Level
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public static long GetExperienceAmountForLevel(int level)
        {
            try
            {
                return XPForLevel[level];
            }
            catch
            {
                return 0;
            }
        }

        public static long GetScaledExperienceAmountForLevel(int level)
        {
            try
            {
                return ScaledXPForLevel[level];
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// A table that holds the required XP/Level
        /// This must include a final entry for MaxLevel + 1
        /// </summary>
        private static readonly long[] XPForLevel =
        {
            0, // xp to level 1
            50, // xp to level 2
            250, // xp to level 3
            850, // xp to level 4
            2300, // xp to level 5
            6350, // xp to level 6
            15950, // xp to level 7
            37950, // xp to level 8
            88950, // xp to level 9
            203950, // xp to level 10
            459950, // xp to level 11
            839950, // xp to level 12
            1399950, // xp to level 13
            2199950, // xp to level 14
            3399950, // xp to level 15
            5199950, // xp to level 16
            7899950, // xp to level 17
            11799950, // xp to level 18
            17499950, // xp to level 19
            25899950, // xp to level 20
            38199950, // xp to level 21
            54699950, // xp to level 22
            76999950, // xp to level 23
            106999950, // xp to level 24
            146999950, // xp to level 25
            199999950, // xp to level 26
            269999950, // xp to level 27
            359999950, // xp to level 28
            479999950, // xp to level 29
            639999950, // xp to level 30
            849999950, // xp to level 31
            1119999950, // xp to level 32
            1469999950, // xp to level 33
            1929999950, // xp to level 34
            2529999950, // xp to level 35
            3319999950, // xp to level 36
            4299999950, // xp to level 37
            5499999950, // xp to level 38
            6899999950, // xp to level 39
            8599999950, // xp to level 40
            12899999950, // xp to level 41
            20699999950, // xp to level 42
            29999999950, // xp to level 43
            40799999950, // xp to level 44
            53999999950, // xp to level 45
            69599999950, // xp to level 46
            88499999950, // xp to level 47
            110999999950, // xp to level 48
            137999999950, // xp to level 49
            169999999950, // xp to level 50
            999999999950, // xp to level 51
        };

        /// <summary>
        /// A table that holds the required XP/Level
        /// This must include a final entry for MaxLevel + 1
        /// </summary>
        private static readonly long[] ScaledXPForLevel =
        {
            0, // xp to level 1
            50, // xp to level 2
            250, // xp to level 3
            850, // xp to level 4
            2300, // xp to level 5
            6350, // xp to level 6
            15950, // xp to level 7
            37950, // xp to level 8
            88950, // xp to level 9
            203950, // xp to level 10
            459950, // xp to level 11
            839950, // xp to level 12
            1399950, // xp to level 13
            2199950, // xp to level 14
            3399950, // xp to level 15
            6499938, // xp to level 16
            9953937, // xp to level 17
            14985937, // xp to level 18
            22399936, // xp to level 19
            33410936, // xp to level 20
            49659935, // xp to level 21
            71656935, // xp to level 22
            101639934, // xp to level 23
            142309934, // xp to level 24
            196979933, // xp to level 25
            269999933, // xp to level 26
            367199932, // xp to level 27
            493199932, // xp to level 28
            662399931, // xp to level 29
            889599931, // xp to level 30
            1189999930, // xp to level 31
            1579199930, // xp to level 32
            2087399929, // xp to level 33
            2759899929, // xp to level 34
            3643199928, // xp to level 35
            4813999928, // xp to level 36
            6277999927, // xp to level 37
            8084999927, // xp to level 38
            10211999926, // xp to level 39
            12813999926, // xp to level 40
            16382999937, // xp to level 41
            20699999950, // xp to level 42
            29999999950, // xp to level 43
            40799999950, // xp to level 44
            53999999950, // xp to level 45
            69599999950, // xp to level 46
            88499999950, // xp to level 47
            110999999950, // xp to level 48
            137999999950, // xp to level 49
            169999999950, // xp to level 50
            999999999950, // xp to level 51
        };

        private long m_experience;

        /// <summary>
        /// Gets or sets the current xp of this player
        /// </summary>
        public virtual long Experience
        {
            get { return m_experience; }
            set
            {
                m_experience = value;
            }
        }

        /// <summary>
        /// Returns the xp that are needed for the next level
        /// </summary>
        public virtual long ExperienceForNextLevel
        {
            get
            {
                return GetExperienceNeededForLevel(Level + 1);
            }
        }

        /// <summary>
        /// Returns the xp that were needed for the current level
        /// </summary>
        public virtual long ExperienceForCurrentLevel
        {
            get
            {
                return GetExperienceNeededForLevel(Level);
            }
        }

        /// <summary>
        /// Returns the xp that is needed for the second stage of current level
        /// </summary>
        public virtual long ExperienceForCurrentLevelSecondStage
        {
            get { return 1 + ExperienceForCurrentLevel + (ExperienceForNextLevel - ExperienceForCurrentLevel) / 2; }
        }

        /// <summary>
        /// Returns how far into the level we have progressed
        /// A value between 0 and 1000 (1 bubble = 100)
        /// </summary>
        public virtual ushort LevelPermill
        {
            get
            {
                //No progress if we haven't even reached current level!
                if (Experience < ExperienceForCurrentLevel)
                    return 0;
                //No progess after maximum level
                if (Level > MaxLevel)
                    return 0;

                return
                    (ushort)(1000 * (Experience - ExperienceForCurrentLevel) / (ExperienceForNextLevel - ExperienceForCurrentLevel));
            }
        }

        public void ForceGainExperience(long expTotal)
        {
            if (IsLevelSecondStage)
            {
                if (Experience + expTotal < ExperienceForCurrentLevelSecondStage)
                {
                    expTotal = ExperienceForCurrentLevelSecondStage - Experience;
                }
            }
            else if (Experience + expTotal < ExperienceForCurrentLevel)
            {
                expTotal = ExperienceForCurrentLevel - Experience;
            }

            Experience += expTotal;

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.YouGet", expTotal.ToString("N0", System.Globalization.NumberFormatInfo.InvariantInfo)), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            if (expTotal >= 0)
            {
                //Level up
                if (Level >= 5 && !CharacterClass.HasAdvancedFromBaseClass())
                {
                    if (expTotal > 0)
                    {
                        //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.CannotRaise"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                        //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.TalkToTrainer"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    }
                }
                else if (Level >= 40 && Level < MaxLevel && !IsLevelSecondStage && Experience >= ExperienceForCurrentLevelSecondStage)
                {
                    OnLevelSecondStage();
                    Notify(GamePlayerEvent.LevelSecondStage, this);
                }
                else if (Level < MaxLevel && Experience >= ExperienceForNextLevel)
                {
                    Level++;
                }
            }
            //Out.SendUpdatePoints();
        }

        /// <summary>
        /// Called whenever this player gains experience
        /// </summary>
        public override void GainExperience(GainedExperienceEventArgs arguments, bool notify = true)
        {
            long expTotal = arguments.ExpTotal;

            if (!GainXP && expTotal > 0)
                return;

            if (this.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) != null)
            {
                Out.SendMessage($"You may not gain experience while in a battlegroup.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            int numCurrentLoyalDays = this.TempProperties.GetProperty<int>(CURRENT_LOYALTY_KEY);
            //check for cached loyalty days, and grab value if needed
            if (numCurrentLoyalDays == 0)
            {
                DbAccountXRealmLoyalty realmLoyalty = DOLDB<DbAccountXRealmLoyalty>.SelectObject(DB.Column("AccountID").IsEqualTo(this.Client.Account.ObjectId).And(DB.Column("Realm").IsEqualTo(this.Realm)));
                if (realmLoyalty == null)
                {
                    DbAccountXRealmLoyalty newLoyalty = new DbAccountXRealmLoyalty();
                    newLoyalty.AccountId = this.Client.Account.ObjectId;
                    newLoyalty.Realm = (int)this.Realm;
                    newLoyalty.LoyalDays = 1;
                    numCurrentLoyalDays = 1;
                    newLoyalty.LastLoyaltyUpdate = DateTime.Now;
                    GameServer.Database.AddObject(newLoyalty);
                }
                else
                {
                    numCurrentLoyalDays = realmLoyalty.LoyalDays;
                }

                this.TempProperties.SetProperty(CURRENT_LOYALTY_KEY, numCurrentLoyalDays);
            }

            //check for realm loyalty
            var loyaltyCheck = this.TempProperties.GetProperty<DateTime>(REALM_LOYALTY_KEY);

            if (loyaltyCheck < DateTime.Now.AddDays(-1))
            {
                List<DbAccountXRealmLoyalty> rloyal = new List<DbAccountXRealmLoyalty>(DOLDB<DbAccountXRealmLoyalty>.SelectObjects(DB.Column("AccountID").IsEqualTo(this.Client.Account.ObjectId)));
                bool realmFound = false;
                foreach (var rl in rloyal)
                {
                    if (rl.Realm == (int)this.Realm)
                    {
                        if (rl.LastLoyaltyUpdate < DateTime.Now.AddDays(-1))
                            rl.LoyalDays++;
                        numCurrentLoyalDays = rl.LoyalDays;
                        realmFound = true;
                    }
                    else
                    {
                        //reduce loyalty
                        if (rl.LoyalDays < 2 || arguments.XPSource is eXPSource.Player)
                        {
                            rl.LoyalDays = 0;
                        }
                        else
                        {
                            rl.LoyalDays--;
                            rl.LoyalDays--;
                        }

                        if (rl.LoyalDays < rl.MinimumLoyalDays)
                            rl.LoyalDays = rl.MinimumLoyalDays;
                    }

                    rl.LastLoyaltyUpdate = DateTime.Now;
                    GameServer.Database.SaveObject(rl);
                }

                if (realmFound == false)
                {
                    DbAccountXRealmLoyalty newLoyalty = new DbAccountXRealmLoyalty();
                    newLoyalty.AccountId = this.Client.Account.ObjectId;
                    newLoyalty.Realm = (int)this.Realm;
                    newLoyalty.LoyalDays = 1;
                    numCurrentLoyalDays = 1;
                    newLoyalty.LastLoyaltyUpdate = DateTime.Now;
                    GameServer.Database.AddObject(newLoyalty);
                }

                this.TempProperties.SetProperty(REALM_LOYALTY_KEY, DateTime.Now);
                this.TempProperties.SetProperty(CURRENT_LOYALTY_KEY, numCurrentLoyalDays);
            }

            //if (arguments.XPSource is eXPSource.Player && !this.CurrentZone.IsBG)
            //{
            //    LoyaltyManager.HandlePVPKill(this);
            //}

            long baseXp = arguments.ExpBase;

            if (arguments.AllowMultiply)
            {
                // Recalculate base experience.
                expTotal -= baseXp;

                if (Properties.ENABLE_ZONE_BONUSES)
                {
                    long zoneBonus = baseXp * ZoneBonus.GetXPBonus(this) / 100;

                    if (zoneBonus > 0)
                    {
                        zoneBonus = (long)(zoneBonus * Properties.XP_RATE);
                        //Out.SendMessage(ZoneBonus.GetBonusMessage(this, (int)zoneBonus, ZoneBonus.eZoneBonusType.XP), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                        GainExperience(new(zoneBonus, 0, 0, 0, 0, 0, false, false, eXPSource.Other), false);
                    }
                }

                if (CurrentRegion.IsRvR || CurrentZone.IsRvR)
                    baseXp = (long)(baseXp * Properties.RvR_XP_RATE);
                else
                    baseXp = (long)(baseXp * Properties.XP_RATE);

                long xpBonus = GetModified(eProperty.XpPoints);

                if (xpBonus != 0)
                    baseXp += baseXp * xpBonus / 100;

                expTotal += baseXp;
            }

            // Get Champion Experience too
            // GainChampionExperience(expTotal);

            if (IsLevelSecondStage)
            {
                if (Experience + expTotal < ExperienceForCurrentLevelSecondStage)
                {
                    expTotal = ExperienceForCurrentLevelSecondStage - Experience;
                }
            }
            else if (Experience + expTotal < ExperienceForCurrentLevel)
            {
                expTotal = ExperienceForCurrentLevel - Experience;
            }

            if (arguments.SendMessage && expTotal > 0)
            {
                System.Globalization.NumberFormatInfo format = System.Globalization.NumberFormatInfo.InvariantInfo;
                string totalExpStr = expTotal.ToString("N0", format);
                string expCampBonusStr = string.Empty;
                string expGroupBonusStr = string.Empty;
                string expGuildBonusStr = string.Empty;
                string expBafBonusStr = string.Empty;
                string expOutpostBonusStr = string.Empty;

                if (arguments.ExpCampBonus > 0)
                    expCampBonusStr = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.CampBonus", arguments.ExpCampBonus.ToString("N0", format)) + " ";

                if (arguments.ExpGroupBonus > 0)
                    expGroupBonusStr = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.GroupBonus", arguments.ExpGroupBonus.ToString("N0", format)) + " ";

                if (arguments.ExpGuildBonus > 0)
                    expGuildBonusStr = "(" + arguments.ExpGuildBonus.ToString("N0", format) + " guild bonus) ";

                if (arguments.ExpBafBonus > 0)
                    expBafBonusStr = "(" + arguments.ExpBafBonus.ToString("N0", format) + " baf bonus) ";

                if (arguments.ExpOutpostBonus > 0)
                    expOutpostBonusStr = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.OutpostBonus", arguments.ExpOutpostBonus.ToString("N0", format)) + " ";

                string message = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.YouGet", totalExpStr) + " " + expCampBonusStr + expGroupBonusStr + expGuildBonusStr + expBafBonusStr + expOutpostBonusStr;
                Out.SendMessage(message, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            Experience += expTotal;

            if (expTotal >= 0)
            {
                //Level up
                if (Level >= 5 && !CharacterClass.HasAdvancedFromBaseClass())
                {
                    if (expTotal > 0)
                    {
                        Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.CannotRaise"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                        Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.GainExperience.TalkToTrainer"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    }
                }
                else if (Level >= 40 && Level < MaxLevel && !IsLevelSecondStage && Experience >= ExperienceForCurrentLevelSecondStage)
                {
                    OnLevelSecondStage();
                    Notify(GamePlayerEvent.LevelSecondStage, this);
                }
                else if (Level < MaxLevel && Experience >= ExperienceForNextLevel)
                {
                    Level++;
                }
            }

            Out.SendUpdatePoints();
        }

        /// <summary>
        /// Gets or sets the level of the player
        /// (delegate to PlayerCharacter)
        /// </summary>
        public override byte Level
        {
            get { return base.Level; }
            set
            {
                byte oldLevel = Level;
                base.Level = value;

                if (oldLevel > 0)
                    if (value > oldLevel)
                        OnLevelUp(oldLevel);
            }
        }

        /// <summary>
        /// What is the base, unmodified level of this character.
        /// </summary>
        public override byte BaseLevel
        {
            get { return DBCharacter != null ? (byte)DBCharacter.Level : base.BaseLevel; }
        }

        private int _mlLevel;

        /// <summary>
        /// Gets and sets the last ML the player has completed.
        /// MLLevel is advanced once all steps are completed.
        /// </summary>
        public virtual int MLLevel
        {
            get { return 0; }
            set { _mlLevel = value; }
        }

        private bool _mlGranted;

        /// <summary>
        /// True if player has started Master Levels
        /// </summary>
        public virtual bool MLGranted
        {
            get { return false; }
            set { _mlGranted = value; }
        }

        private byte _ml;

        /// <summary>
        /// What ML line has this character chosen
        /// </summary>
        public virtual byte MLLine
        {
            get { return 0; }
            set { _ml = value; }
        }

        /// <summary>
        /// What level is displayed to another player
        /// </summary>
        public override byte GetDisplayLevel(GamePlayer player)
        {
            return Math.Min((byte)50, Level);
        }

        private bool m_isLevelSecondStage = false;

        /// <summary>
        /// Is this player in second stage of current level
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual bool IsLevelSecondStage
        {
            get { return m_isLevelSecondStage; }
            set { m_isLevelSecondStage = value; }
        }

        /// <summary>
        /// Called when this player levels
        /// </summary>
        /// <param name="previouslevel"></param>
        public virtual void OnLevelUp(byte previouslevel)
        {
            IsLevelSecondStage = false;

            LoadClassSpecializations(false);
            SpendSpecPoints(Level, previouslevel);

            //level 20 changes realm title and gives 1 realm skill point
            if (Level == 20)
                GainRealmPoints(0);

            // Adjust stats
            // stat increases start at level 6
            if (Level > 5)
            {
                for (int i = Level; i > Math.Max(previouslevel, (byte)5); i--)
                {
                    if (CharacterClass.PrimaryStat != eStat.UNDEFINED)
                    {
                        ChangeBaseStat(CharacterClass.PrimaryStat, 1);
                    }
                    if (CharacterClass.SecondaryStat != eStat.UNDEFINED && ((i - 6) % 2 == 0))
                    { // base level to start adding stats is 6
                        ChangeBaseStat(CharacterClass.SecondaryStat, 1);
                    }
                    if (CharacterClass.TertiaryStat != eStat.UNDEFINED && ((i - 6) % 3 == 0))
                    { // base level to start adding stats is 6
                        ChangeBaseStat(CharacterClass.TertiaryStat, 1);
                    }
                }
            }

            //CharacterClass.OnLevelUp(this, previouslevel);
            RefreshSpecDependantSkills(false);

            if (CharacterClass.ClassType == eClassType.ListCaster)
                SetCasterSpells();
            else
                SetSpells();

            // Echostorm - Code for display of new title on level up
            // Get old and current rank titles
            //string currenttitle = CharacterClass.GetTitle(this, Level);

            // check for difference
            //if (CharacterClass.GetTitle(this, previouslevel) != currenttitle)
            //{
            //    // Inform player of new title.
            //    //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnLevelUp.AttainedRank", currenttitle), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            //}

            if (IsAlive)
            {
                // workaround for starting regeneration
                StartHealthRegeneration();
                StartPowerRegeneration();
            }

            DeathCount = 0;

            if (Group != null)
            {
                Group.UpdateGroupWindow();
            }

            // update color on levelup
            if (ObjectState == eObjectState.Active)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                {
                    if (player == null)
                        continue;

                    player.Out.SendEmoteAnimation(this, eEmote.LvlUp);
                }
            }

            Emote(eEmote.LvlUp);

            // Level up pets and subpets
            if (ServerProperties.Properties.PET_LEVELS_WITH_OWNER &&
                ControlledBrain is ControlledMobBrain brain && brain.Body is GameSummonedPet pet)
            {
                if (pet.SetPetLevel())
                {
                    if (ServerProperties.Properties.PET_SCALE_SPELL_MAX_LEVEL > 0 && pet.Spells.Count > 0)
                        pet.SortSpells();

                    brain.UpdatePetWindow();
                }

                // subpets
                if (pet.ControlledNpcList != null)
                    foreach (ABrain subBrain in pet.ControlledNpcList)
                        if (subBrain != null && subBrain.Body is GameSummonedPet subPet)
                            if (subPet.SetPetLevel()) // Levels up subpet
                                if (ServerProperties.Properties.PET_SCALE_SPELL_MAX_LEVEL > 0)
                                    subPet.SortSpells();
            }

            // save player to database
            //SaveIntoDatabase();
        }

        /// <summary>
        /// Called when this player reaches second stage of the current level
        /// </summary>
        public virtual void OnLevelSecondStage()
        {
            IsLevelSecondStage = true;

            //death penalty reset on mini-ding
            DeathCount = 0;

            if (Group != null)
                Group.UpdateGroupWindow();

            Emote(eEmote.LvlUp);

            if (ObjectState == eObjectState.Active)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                {
                    if (player == null)
                        continue;

                    player.Out.SendEmoteAnimation(this, eEmote.LvlUp);
                }
            }

            SpendSpecPoints(Level, Level);
            RefreshSpecDependantSkills(false);
        }

        /// <summary>
        /// Calculate the Autotrain points.
        /// </summary>
        /// <param name="spec">Specialization</param>
        /// <param name="mode">various AT related calculations (amount of points, level of AT...)</param>
        public virtual int GetAutoTrainPoints(Specialization spec, int Mode)
        {
            int max_autotrain = Level / 4;

            if (max_autotrain == 0) 
                max_autotrain = 1;

            foreach (string autotrainKey in CharacterClass.GetAutotrainableSkills())
            {
                if (autotrainKey == spec.KeyName)
                {
                    switch (Mode)
                    {
                        case 0:// return sum of all AT points in the spec
                        {
                            int pts_to_refund = Math.Min(max_autotrain, spec.Level);
                            return ((pts_to_refund * (pts_to_refund + 1) - 2) / 2);
                        }
                        case 1: // return max AT level + message
                        {
                            if (Level % 4 == 0)
                                if (spec.Level >= max_autotrain)
                                    return max_autotrain;
                            //else
                            //    Out.SendDialogBox(eDialogCode.SimpleWarning, 0, 0, 0, 0, eDialogType.Ok, true, LanguageMgr.GetTranslation(Client.Account.Language, "PlayerClass.OnLevelUp.Autotrain", spec.Name, max_autotrain));
                            return 0;
                        }
                        case 2: // return next free points due to AT change on levelup
                        {
                            if (spec.Level < max_autotrain)
                                return (spec.Level + 1);
                            else
                                return 0;
                        }
                        case 3: // return sum of all free AT points
                        {
                            if (spec.Level < max_autotrain)
                                return (((max_autotrain * (max_autotrain + 1) - 2) / 2) - ((spec.Level * (spec.Level + 1) - 2) / 2));
                            else
                                return ((max_autotrain * (max_autotrain + 1) - 2) / 2);
                        }
                        case 4: // spec is autotrainable
                        {
                            return 1;
                        }
                    }
                }
            }
            return 0;
        }

        #endregion Level/Experience

        #region Combat

        public override int CalculateCastingTime(SpellLine line, Spell spell)
        {
            int castTime;

            if (!CharacterClass.CanChangeCastingSpeed(line, spell))
                castTime = spell.CastTime;
            else
                castTime = base.CalculateCastingTime(line, spell);

            return castTime;
        }

        /// <summary>
        /// Gets/Sets safety flag
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual bool SafetyFlag
        {
            get { return DBCharacter != null ? DBCharacter.SafetyFlag : false; }
            set { if (DBCharacter != null) DBCharacter.SafetyFlag = value; }
        }

        /// <summary>
        /// Sets/gets the living's cloak hood state
        /// (delegate to PlayerCharacter)
        /// </summary>
        public override bool IsCloakHoodUp
        {
            get { return base.IsCloakHoodUp; }
            set
            {
                base.IsCloakHoodUp = value;

                BroadcastLivingEquipmentUpdate();
            }
        }

        /// <summary>
        /// Sets/gets the living's cloak visible state
        /// (delegate to PlayerCharacter)
        /// </summary>
        public override bool IsCloakInvisible
        {
            get
            {
                return DBCharacter != null ? DBCharacter.IsCloakInvisible : base.IsCloakInvisible;
            }
            set
            {
                //DBCharacter.IsCloakInvisible = value;

                //Out.SendInventoryItemsUpdate(null);
                UpdateEquipmentAppearance();

                //if (value)
                //{
                //    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.IsCloakInvisible.Invisible"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //}
                //else
                //{
                //    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.IsCloakInvisible.Visible"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //}
            }
        }

        /// <summary>
        /// Sets/gets the living's helm visible state
        /// (delegate to PlayerCharacter)
        /// </summary>
        public override bool IsHelmInvisible
        {
            get
            {
                return DBCharacter != null ? DBCharacter.IsHelmInvisible : base.IsHelmInvisible;
            }
            set
            {
                //DBCharacter.IsHelmInvisible = value;

                //Out.SendInventoryItemsUpdate(null);
                UpdateEquipmentAppearance();

                //if (value)
                //{
                //    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.IsHelmInvisible.Invisible"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //}
                //else
                //{
                //    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.IsHelmInvisible.Visible"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //}
            }
        }

        /// <summary>
        /// Gets or sets the players SpellQueue option
        /// (delegate to PlayerCharacter)
        /// </summary>
        public virtual bool SpellQueue
        {
            get { return DBCharacter != null ? DBCharacter.SpellQueue : false; }
            set { if (DBCharacter != null) DBCharacter.SpellQueue = value; }
        }

        /// <summary>
        /// Switches the active weapon to another one
        /// </summary>
        /// <param name="slot">the new eActiveWeaponSlot</param>
        public override void SwitchWeapon(eActiveWeaponSlot slot)
        {
            if (attackComponent.AttackState)
                attackComponent.StopAttack();

            if (effectListComponent.ContainsEffectForEffectType(eEffect.Volley))
            {
                AtlasOF_VolleyECSEffect volley = (AtlasOF_VolleyECSEffect)EffectListService.GetEffectOnTarget(this, eEffect.Volley);
                volley?.OnPlayerSwitchedWeapon();
            }

            if (CurrentSpellHandler != null)
            {
                Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.SwitchWeapon.SpellCancelled"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                StopCurrentSpellcast();
            }

            foreach (Spell spell in ActivePulseSpells.Values)
            {
                if (spell.InstrumentRequirement != 0)
                {
                    Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.SwitchWeapon.SpellCancelled"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                    EffectService.RequestImmediateCancelConcEffect(EffectListService.GetPulseEffectOnTarget(this, spell));
                }
            }

            DbInventoryItem[] oldActiveSlots = new DbInventoryItem[4];
            DbInventoryItem[] newActiveSlots = new DbInventoryItem[4];
            DbInventoryItem rightHandSlot = Inventory.GetItem(eInventorySlot.RightHandWeapon);
            DbInventoryItem leftHandSlot = Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            DbInventoryItem twoHandSlot = Inventory.GetItem(eInventorySlot.TwoHandWeapon);
            DbInventoryItem distanceSlot = Inventory.GetItem(eInventorySlot.DistanceWeapon);

            // save old active weapons
            // simple active slot logic:
            // 0=right hand, 1=left hand, 2=two-hand, 3=range, F=none
            switch (VisibleActiveWeaponSlots & 0x0F)
            {
                case 0: oldActiveSlots[0] = rightHandSlot; break;
                case 2: oldActiveSlots[2] = twoHandSlot; break;
                case 3: oldActiveSlots[3] = distanceSlot; break;
            }

            if ((VisibleActiveWeaponSlots & 0xF0) == 0x10)
                oldActiveSlots[1] = leftHandSlot;

            base.SwitchWeapon(slot);

            // save new active slots
            switch (VisibleActiveWeaponSlots & 0x0F)
            {
                case 0: newActiveSlots[0] = rightHandSlot; break;
                case 2: newActiveSlots[2] = twoHandSlot; break;
                case 3: newActiveSlots[3] = distanceSlot; break;
            }

            if ((VisibleActiveWeaponSlots & 0xF0) == 0x10)
                newActiveSlots[1] = leftHandSlot;

            // unequip changed items
            for (int i = 0; i < 4; i++)
            {
                if (oldActiveSlots[i] != null && newActiveSlots[i] == null)
                    OnItemUnequipped(oldActiveSlots[i], (eInventorySlot)oldActiveSlots[i].SlotPosition);
            }

            // equip new active items
            for (int i = 0; i < 4; i++)
            {
                if (newActiveSlots[i] != null && oldActiveSlots[i] == null)
                    OnItemEquipped(newActiveSlots[i], (eInventorySlot)newActiveSlots[i].SlotPosition);
            }

            if (ObjectState == eObjectState.Active)
                UpdateEquipmentAppearance();
        }

        /// <summary>
        /// Switches the active quiver slot to another one
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="forced"></param>
        public virtual void SwitchQuiver(eActiveQuiverSlot slot, bool forced)
        {
            if (slot != eActiveQuiverSlot.None)
            {
                eInventorySlot updatedSlot = eInventorySlot.Invalid;
                if ((slot & eActiveQuiverSlot.Fourth) > 0)
                    updatedSlot = eInventorySlot.FourthQuiver;
                else if ((slot & eActiveQuiverSlot.Third) > 0)
                    updatedSlot = eInventorySlot.ThirdQuiver;
                else if ((slot & eActiveQuiverSlot.Second) > 0)
                    updatedSlot = eInventorySlot.SecondQuiver;
                else if ((slot & eActiveQuiverSlot.First) > 0)
                    updatedSlot = eInventorySlot.FirstQuiver;

                if (Inventory.GetItem(updatedSlot) != null && (rangeAttackComponent.ActiveQuiverSlot != slot || forced))
                    rangeAttackComponent.ActiveQuiverSlot = slot;
                else
                    rangeAttackComponent.ActiveQuiverSlot = eActiveQuiverSlot.None;
            }
            else
            {
                if (Inventory.GetItem(eInventorySlot.FirstQuiver) != null)
                    SwitchQuiver(eActiveQuiverSlot.First, true);
                else if (Inventory.GetItem(eInventorySlot.SecondQuiver) != null)
                    SwitchQuiver(eActiveQuiverSlot.Second, true);
                else if (Inventory.GetItem(eInventorySlot.ThirdQuiver) != null)
                    SwitchQuiver(eActiveQuiverSlot.Third, true);
                else if (Inventory.GetItem(eInventorySlot.FourthQuiver) != null)
                    SwitchQuiver(eActiveQuiverSlot.Fourth, true);
                else
                    rangeAttackComponent.ActiveQuiverSlot = eActiveQuiverSlot.None;
            }
        }

        /// <summary>
        /// This method is called at the end of the attack sequence to
        /// notify objects if they have been attacked/hit by an attack
        /// </summary>
        /// <param name="ad">information about the attack</param>
        public override void OnAttackedByEnemy(AttackData ad)
        {
            base.OnAttackedByEnemy(ad);

            if (ControlledBrain != null && ControlledBrain is ControlledMobBrain)
            {
                var brain = (ControlledMobBrain)ControlledBrain;
                brain.OnOwnerAttacked(ad);
            }

            switch (ad.AttackResult)
            {
                case eAttackResult.HitStyle:
                case eAttackResult.HitUnstyled:
                {
                    // If attacked by a non-damaging spell, we should not show damage numbers.
                    // We need to check the damage on the spell here, not in the AD, since this could in theory be a damaging spell that had its damage modified to 0.
                    if (ad.AttackType == eAttackType.Spell && ad.SpellHandler.Spell?.Damage == 0)
                        break;

                    if (IsStealthed && !effectListComponent.ContainsEffectForEffectType(eEffect.Vanish))
                    {
                        if (ad.AttackType != eAttackType.Spell || ad.SpellHandler.Spell.SpellType != eSpellType.DamageOverTime)
                            Stealth(false);
                    }

                    // decrease condition of hitted armor piece
                    if (ad.ArmorHitLocation != eArmorSlot.NOTSET)
                    {
                        DbInventoryItem item = Inventory.GetItem((eInventorySlot)ad.ArmorHitLocation);

                        if (item != null)
                        {
                            TryReactiveEffect(item, ad.Attacker);

                            if (item is GameInventoryItem)
                            {
                                (item as GameInventoryItem).OnStruckByEnemy(this, ad.Attacker);
                            }
                        }
                    }
                    break;
                }
                case eAttackResult.Blocked:
                {
                    DbInventoryItem reactiveItem = Inventory.GetItem(eInventorySlot.LeftHandWeapon);
                    if (reactiveItem != null && reactiveItem.Object_Type == (int)eObjectType.Shield)
                    {
                        TryReactiveEffect(reactiveItem, ad.Attacker);

                        if (reactiveItem is GameInventoryItem)
                        {
                            (reactiveItem as GameInventoryItem).OnStruckByEnemy(this, ad.Attacker);
                        }
                    }
                    break;
                }
            }
            // vampiir
            if (CharacterClass is ClassVampiir)
            {
                GameSpellEffect removeEffect = SpellHandler.FindEffectOnTarget(this, "VampiirSpeedEnhancement");
                if (removeEffect != null)
                    removeEffect.Cancel(false);
            }
        }

        /// <summary>
        /// Launch any reactive effect on an item
        /// </summary>
        /// <param name="reactiveItem"></param>
        /// <param name="target"></param>
        protected virtual void TryReactiveEffect(DbInventoryItem reactiveItem, GameLiving target)
        {
            if (reactiveItem != null)
            {
                int requiredLevel = reactiveItem.Template.LevelRequirement > 0 ? reactiveItem.Template.LevelRequirement : Math.Min(MaxLevel, reactiveItem.Level);

                if (requiredLevel <= Level)
                {
                    SpellLine reactiveEffectLine = SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);

                    if (reactiveItem.ProcSpellID != 0)
                    {
                        Spell spell = SkillBase.GetSpellByID(reactiveItem.ProcSpellID);

                        if (spell != null)
                        {
                            int chance = reactiveItem.ProcChance > 0 ? reactiveItem.ProcChance : 10;

                            if (Util.Chance(chance))
                            {
                                ISpellHandler spellHandler = ScriptMgr.CreateSpellHandler(this, spell, reactiveEffectLine);
                                if (spellHandler != null)
                                {
                                    spellHandler.StartSpell(target, reactiveItem);
                                }
                            }
                        }
                    }

                    if (reactiveItem.ProcSpellID1 != 0)
                    {
                        Spell spell = SkillBase.GetSpellByID(reactiveItem.ProcSpellID1);

                        if (spell != null)
                        {
                            int chance = reactiveItem.ProcChance > 0 ? reactiveItem.ProcChance : 10;

                            if (Util.Chance(chance))
                            {
                                ISpellHandler spellHandler = ScriptMgr.CreateSpellHandler(this, spell, reactiveEffectLine);
                                if (spellHandler != null)
                                {
                                    spellHandler.StartSpell(target, reactiveItem);
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (Duel != null && !IsDuelPartner(source as GameLiving))
                Duel.Stop();

            if (source is IGamePlayer || (source is GameNPC npc && npc.Brain is IControlledBrain brain && brain.GetLivingOwner() is IGamePlayer) || source is GameSiegeWeapon)
            {
                if (Realm != source.Realm && source.Realm != 0)
                    DamageRvRMemory += damageAmount + criticalAmount;
            }

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (HasAbility(GS.Abilities.DefensiveCombatPowerRegeneration))
                Mana += (int)((damageAmount + criticalAmount) * 0.25);
        }

        public override int MeleeAttackRange
        {
            get
            {
                int range = 128;

                if (TargetObject is GameKeepComponent)
                    range += 150;
                else
                {
                    if (TargetObject is GameLiving target && target.IsMoving)
                        range += 32;

                    if (IsMoving)
                        range += 32;
                }

                return range;
            }
        }

        /// <summary>
        /// Gets the effective AF of this living.  This is used for the overall AF display
        /// on the character but not used in any damage equations.
        /// </summary>
        public override int EffectiveOverallAF
        {
            get
            {
                int eaf = 0;
                int abs = 0;
                foreach (DbInventoryItem item in Inventory.VisibleItems)
                {
                    double factor = 0;
                    switch (item.Item_Type)
                    {
                        case Slot.TORSO:
                        factor = 2.2;
                        break;

                        case Slot.LEGS:
                        factor = 1.3;
                        break;

                        case Slot.ARMS:
                        factor = 0.75;
                        break;

                        case Slot.HELM:
                        factor = 0.5;
                        break;

                        case Slot.HANDS:
                        factor = 0.25;
                        break;

                        case Slot.FEET:
                        factor = 0.25;
                        break;
                    }

                    int itemAFCap = Level << 1;
                    if (RealmLevel > 39)
                        itemAFCap += 2;
                    switch ((eObjectType)item.Object_Type)
                    {
                        case eObjectType.Cloth:
                        abs = 0;
                        itemAFCap >>= 1;
                        break;

                        case eObjectType.Leather:
                        abs = 10;
                        break;

                        case eObjectType.Reinforced:
                        abs = 19;
                        break;

                        case eObjectType.Studded:
                        abs = 19;
                        break;

                        case eObjectType.Scale:
                        abs = 27;
                        break;

                        case eObjectType.Chain:
                        abs = 27;
                        break;

                        case eObjectType.Plate:
                        abs = 34;
                        break;
                    }

                    if (factor > 0)
                    {
                        int af = item.DPS_AF;
                        if (af > itemAFCap)
                            af = itemAFCap;
                        double piece_eaf = af * item.Quality / 100.0 * item.ConditionPercent / 100.0 * (1 + abs / 100.0);
                        eaf += (int)(piece_eaf * factor);
                    }
                }

                // Overall AF CAP = 10 * level * (1 + abs%/100)
                int bestLevel = -1;
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("AlbArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("HibArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("MidArmor"));

                switch (bestLevel)
                {
                    default: abs = 0; break; // cloth etc
                    case ArmorLevel.Leather: abs = 10; break;
                    case ArmorLevel.Studded: abs = 19; break;
                    case ArmorLevel.Chain: abs = 27; break;
                    case ArmorLevel.Plate: abs = 34; break;
                }

                eaf += BaseBuffBonusCategory[(int)eProperty.ArmorFactor]; // base buff before cap
                int eafcap = (int)(10 * Level * (1 + abs * 0.01));
                if (eaf > eafcap)
                    eaf = eafcap;
                eaf += (int)Math.Min(Level * 1.875, SpecBuffBonusCategory[(int)eProperty.ArmorFactor])
                       - DebuffCategory[(int)eProperty.ArmorFactor]
                       + BuffBonusCategory4[(int)eProperty.ArmorFactor]
                       + Math.Min(Level, ItemBonus[(int)eProperty.ArmorFactor]);

                eaf = (int)(eaf * BuffBonusMultCategory1.Get((int)eProperty.ArmorFactor));

                return eaf;
            }
        }

        /// <summary>
        /// Calc Armor hit location when player is hit by enemy
        /// </summary>
        /// <returns>slotnumber where enemy hits</returns>
        /// attackdata(ad) changed
        public virtual eArmorSlot CalculateArmorHitLocation(AttackData ad)
        {
            if (ad.Style != null)
            {
                if (ad.Style.ArmorHitLocation != eArmorSlot.NOTSET)
                    return ad.Style.ArmorHitLocation;
            }

            int chancehit = Util.Random(1, 100);

            if (chancehit <= 40)
            {
                return eArmorSlot.TORSO;
            }
            else if (chancehit <= 65)
            {
                return eArmorSlot.LEGS;
            }
            else if (chancehit <= 80)
            {
                return eArmorSlot.ARMS;
            }
            else if (chancehit <= 90)
            {
                return eArmorSlot.HEAD;
            }
            else if (chancehit <= 95)
            {
                return eArmorSlot.HAND;
            }
            else
            {
                return eArmorSlot.FEET;
            }
        }

        public override int WeaponSpecLevel(eObjectType objectType, int slotPosition)
        {
            // Use axe spec if left hand axe is not in the left hand slot.
            if (objectType is eObjectType.LeftAxe && slotPosition is not Slot.LEFTHAND)
                return GameServer.ServerRules.GetObjectSpecLevel(this, eObjectType.Axe);

            // Use left axe spec if axe is in the left hand slot.
            if (slotPosition is Slot.LEFTHAND && objectType is eObjectType.Axe)
                return GameServer.ServerRules.GetObjectSpecLevel(this, eObjectType.LeftAxe);

            return GameServer.ServerRules.GetObjectSpecLevel(this, objectType);
        }

        /// <summary>
        /// determines current weaponspeclevel
        /// </summary>
        public override int WeaponSpecLevel(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            return WeaponSpecLevel((eObjectType)weapon.Object_Type, weapon.SlotPosition);
        }

        protected Hashtable m_compatibleObjectTypes = null;

        /// <summary>
        /// Translates object type to compatible object types based on server type
        /// </summary>
        /// <param name="objectType">The object type</param>
        /// <returns>An array of compatible object types</returns>
        protected virtual eObjectType[] GetCompatibleObjectTypes(eObjectType objectType)
        {
            if (m_compatibleObjectTypes == null)
            {
                m_compatibleObjectTypes = new Hashtable();
                m_compatibleObjectTypes[(int)eObjectType.Staff] = new eObjectType[] { eObjectType.Staff };
                m_compatibleObjectTypes[(int)eObjectType.Fired] = new eObjectType[] { eObjectType.Fired };

                m_compatibleObjectTypes[(int)eObjectType.FistWraps] = new eObjectType[] { eObjectType.FistWraps };
                m_compatibleObjectTypes[(int)eObjectType.MaulerStaff] = new eObjectType[] { eObjectType.MaulerStaff };

                //alb
                m_compatibleObjectTypes[(int)eObjectType.CrushingWeapon] = new eObjectType[] { eObjectType.CrushingWeapon, eObjectType.Blunt, eObjectType.Hammer };
                m_compatibleObjectTypes[(int)eObjectType.SlashingWeapon] = new eObjectType[] { eObjectType.SlashingWeapon, eObjectType.Blades, eObjectType.Sword, eObjectType.Axe };
                m_compatibleObjectTypes[(int)eObjectType.ThrustWeapon] = new eObjectType[] { eObjectType.ThrustWeapon, eObjectType.Piercing };
                m_compatibleObjectTypes[(int)eObjectType.TwoHandedWeapon] = new eObjectType[] { eObjectType.TwoHandedWeapon, eObjectType.LargeWeapons };
                m_compatibleObjectTypes[(int)eObjectType.PolearmWeapon] = new eObjectType[] { eObjectType.PolearmWeapon, eObjectType.CelticSpear, eObjectType.Spear };
                m_compatibleObjectTypes[(int)eObjectType.Flexible] = new eObjectType[] { eObjectType.Flexible };
                m_compatibleObjectTypes[(int)eObjectType.Longbow] = new eObjectType[] { eObjectType.Longbow };
                m_compatibleObjectTypes[(int)eObjectType.Crossbow] = new eObjectType[] { eObjectType.Crossbow };
                //TODO: case 5: abilityCheck = Abilities.Weapon_Thrown; break;

                //mid
                m_compatibleObjectTypes[(int)eObjectType.Hammer] = new eObjectType[] { eObjectType.Hammer, eObjectType.CrushingWeapon, eObjectType.Blunt };
                m_compatibleObjectTypes[(int)eObjectType.Sword] = new eObjectType[] { eObjectType.Sword, eObjectType.SlashingWeapon, eObjectType.Blades };
                m_compatibleObjectTypes[(int)eObjectType.LeftAxe] = new eObjectType[] { eObjectType.LeftAxe };
                m_compatibleObjectTypes[(int)eObjectType.Axe] = new eObjectType[] { eObjectType.Axe, eObjectType.SlashingWeapon, eObjectType.Blades }; //eObjectType.LeftAxe removed
                m_compatibleObjectTypes[(int)eObjectType.HandToHand] = new eObjectType[] { eObjectType.HandToHand };
                m_compatibleObjectTypes[(int)eObjectType.Spear] = new eObjectType[] { eObjectType.Spear, eObjectType.CelticSpear, eObjectType.PolearmWeapon };
                m_compatibleObjectTypes[(int)eObjectType.CompositeBow] = new eObjectType[] { eObjectType.CompositeBow };
                m_compatibleObjectTypes[(int)eObjectType.Thrown] = new eObjectType[] { eObjectType.Thrown };

                //hib
                m_compatibleObjectTypes[(int)eObjectType.Blunt] = new eObjectType[] { eObjectType.Blunt, eObjectType.CrushingWeapon, eObjectType.Hammer };
                m_compatibleObjectTypes[(int)eObjectType.Blades] = new eObjectType[] { eObjectType.Blades, eObjectType.SlashingWeapon, eObjectType.Sword, eObjectType.Axe };
                m_compatibleObjectTypes[(int)eObjectType.Piercing] = new eObjectType[] { eObjectType.Piercing, eObjectType.ThrustWeapon };
                m_compatibleObjectTypes[(int)eObjectType.LargeWeapons] = new eObjectType[] { eObjectType.LargeWeapons, eObjectType.TwoHandedWeapon };
                m_compatibleObjectTypes[(int)eObjectType.CelticSpear] = new eObjectType[] { eObjectType.CelticSpear, eObjectType.Spear, eObjectType.PolearmWeapon };
                m_compatibleObjectTypes[(int)eObjectType.Scythe] = new eObjectType[] { eObjectType.Scythe };
                m_compatibleObjectTypes[(int)eObjectType.RecurvedBow] = new eObjectType[] { eObjectType.RecurvedBow };

                m_compatibleObjectTypes[(int)eObjectType.Shield] = new eObjectType[] { eObjectType.Shield };
                m_compatibleObjectTypes[(int)eObjectType.Poison] = new eObjectType[] { eObjectType.Poison };
                //TODO: case 45: abilityCheck = Abilities.instruments; break;
            }

            eObjectType[] res = (eObjectType[])m_compatibleObjectTypes[(int)objectType];
            if (res == null)
                return new eObjectType[0];
            return res;
        }

        /// <summary>
        /// determines current weaponspeclevel
        /// </summary>
        public int WeaponBaseSpecLevel(eObjectType objectType, int slotPosition)
        {
            // Use axe spec if left hand axe is not in the left hand slot.
            if (objectType is eObjectType.LeftAxe && slotPosition is not Slot.LEFTHAND)
                return GameServer.ServerRules.GetObjectBaseSpecLevel(this, eObjectType.Axe);

            // Use left axe spec if axe is in the left hand slot.
            if (slotPosition is Slot.LEFTHAND && objectType is eObjectType.Axe)
                return GameServer.ServerRules.GetObjectBaseSpecLevel(this, eObjectType.LeftAxe);

            return GameServer.ServerRules.GetObjectBaseSpecLevel(this, objectType);
        }

        /// <summary>
        /// determines current weaponspeclevel
        /// </summary>
        public int WeaponBaseSpecLevel(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            return WeaponBaseSpecLevel((eObjectType)weapon.Object_Type, weapon.SlotPosition);
        }

        /// <summary>
        /// Gets the weaponskill of weapon
        /// </summary>
        public override double GetWeaponSkill(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            int classBaseWeaponSkill = weapon.SlotPosition == (int)eInventorySlot.DistanceWeapon ? CharacterClass.WeaponSkillRangedBase : CharacterClass.WeaponSkillBase;
            double weaponSkill = Level * classBaseWeaponSkill / 200.0 * (1 + 0.01 * GetWeaponStat(weapon) / 2) * Effectiveness;
            return Math.Max(1, weaponSkill * GetModified(eProperty.WeaponSkill) * 0.01);
        }

        /// <summary>
        /// calculates weapon stat
        /// </summary>
        /// <param name="weapon"></param>
        /// <returns></returns>
        public override int GetWeaponStat(DbInventoryItem weapon)
        {
            if (weapon != null)
            {
                switch ((eObjectType)weapon.Object_Type)
                {
                    // DEX modifier
                    case eObjectType.Staff:
                    case eObjectType.Fired:
                    case eObjectType.Longbow:
                    case eObjectType.Crossbow:
                    case eObjectType.CompositeBow:
                    case eObjectType.RecurvedBow:
                    case eObjectType.Thrown:
                    case eObjectType.Shield:
                    return GetModified(eProperty.Dexterity);

                    // STR+DEX modifier
                    case eObjectType.ThrustWeapon:
                    case eObjectType.Piercing:
                    case eObjectType.Spear:
                    case eObjectType.Flexible:
                    case eObjectType.HandToHand:
                    return (GetModified(eProperty.Strength) + GetModified(eProperty.Dexterity)) >> 1;
                }
            }
            // STR modifier for others
            return GetModified(eProperty.Strength);
        }

        /// <summary>
        /// calculate item armor factor influenced by quality, con and duration
        /// </summary>
        public override double GetArmorAF(eArmorSlot slot)
        {
            if (slot is eArmorSlot.NOTSET)
                return 0;

            DbInventoryItem item = Inventory.GetItem((eInventorySlot)slot);

            if (item == null)
                return 0;

            int characterLevel = Level;

            if (RealmLevel > 39)
                characterLevel++;

            int armorFactorCap = characterLevel * 2;
            double armorFactor = Math.Min(item.DPS_AF, (eObjectType)item.Object_Type is eObjectType.Cloth ? characterLevel : armorFactorCap);
            armorFactor += BaseBuffBonusCategory[(int)eProperty.ArmorFactor] / 6.0; // Base AF buff.
            armorFactor *= item.Quality * 0.01 * item.Condition / item.MaxCondition; // Apply condition and quality before the second cap. Maybe incorrect, but it makes base AF buffs a little more useful.
            armorFactor = Math.Min(armorFactor, armorFactorCap);
            armorFactor += base.GetArmorAF(slot);

            /*GameSpellEffect effect = SpellHandler.FindEffectOnTarget(this, typeof(VampiirArmorDebuff));
            if (effect != null && slot == (effect.SpellHandler as VampiirArmorDebuff).Slot)
                armorFactor -= (int) (effect.SpellHandler as VampiirArmorDebuff).Spell.Value;*/

            return armorFactor;
        }

        /// <summary>
        /// Calculates armor absorb level
        /// </summary>
        public override double GetArmorAbsorb(eArmorSlot slot)
        {
            if (slot == eArmorSlot.NOTSET)
                return 0;

            DbInventoryItem item = Inventory.GetItem((eInventorySlot)slot);

            if (item == null)
                return 0;

            // Debuffs can't lower absorb below 0%: https://darkageofcamelot.com/article/friday-grab-bag-08302019
            return Math.Clamp((item.SPD_ABS + GetModified(eProperty.ArmorAbsorption)) * 0.01, 0, 1);
        }

        /// <summary>
        /// Weaponskill thats shown to the player
        /// </summary>
        public virtual int DisplayedWeaponSkill
        {
            get
            {
                int itemBonus = WeaponSpecLevel(ActiveWeapon) - WeaponBaseSpecLevel(ActiveWeapon) - RealmLevel / 10;
                double m = 0.56 + itemBonus / 70.0;
                double weaponSpec = WeaponSpecLevel(ActiveWeapon) + itemBonus * m;
                double oldWStoNewWSScalar = (3 + .02 * GetWeaponStat(ActiveWeapon)) / (1 + .005 * GetWeaponStat(ActiveWeapon));
                return (int)(GetWeaponSkill(ActiveWeapon) * (1.00 + weaponSpec * 0.01) * oldWStoNewWSScalar);
            }
        }

        //// <summary>
        /// Gets the weapondamage of currently used weapon
        /// Used to display weapon damage in stats, 16.5dps = 1650
        /// </summary>
        /// <param name="weapon">the weapon used for attack</param>
        public override double WeaponDamage(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            return ApplyWeaponQualityAndConditionToDamage(weapon, WeaponDamageWithoutQualityAndCondition(weapon));
        }

        public double WeaponDamageWithoutQualityAndCondition(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            double Dps = weapon.DPS_AF;

            // Apply dps cap before quality and condition.
            // http://www.classesofcamelot.com/faq.asp?mode=view&cat=10
            int dpsCap = 12 + 3 * Level;

            if (RealmLevel > 39)
                dpsCap += 3;

            if (Dps > dpsCap)
                Dps = dpsCap;

            Dps *= 1 + GetModified(eProperty.DPS) * 0.01;
            return Dps * 0.1;
        }

        public static double ApplyWeaponQualityAndConditionToDamage(DbInventoryItem weapon, double damage)
        {
            return damage * weapon.Quality * 0.01 * weapon.Condition / weapon.MaxCondition;
        }

        public override bool CanCastWhileAttacking()
        {
            switch (CharacterClass)
            {
                case ClassVampiir:
                case ClassMaulerAlb:
                case ClassMaulerMid:
                case ClassMaulerHib:
                return true;
            }

            return false;
        }

        public override void OnAttackEnemy(AttackData ad)
        {
            //Console.WriteLine(string.Format("OnAttack called on {0}", Name));

            // Note that this function is called whenever an attack is made, regardless of whether that attack was successful.
            // i.e. missed melee swings and resisted spells still trigger

            if (effectListComponent is null)
                return;

            Stealth(false);

            //Cancel SpeedOfTheRealm (Hastener Speed)
            if (effectListComponent.Effects.ContainsKey(eEffect.MovementSpeedBuff))
            {
                var effects = effectListComponent.GetSpellEffects(eEffect.MovementSpeedBuff);

                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] is null)
                        continue;

                    var spellEffect = effects[i] as ECSGameSpellEffect;
                    if (spellEffect != null && spellEffect.Name.ToLower().Equals("speed of the realm"))
                    {
                        EffectService.RequestImmediateCancelEffect(effects[i]);
                    }
                }
            }

            if (ad != null && ad.Damage > 0)
                TryCancelMovementSpeedBuffs(true);

            var oProcEffects = effectListComponent.GetSpellEffects(eEffect.OffensiveProc);
            //OffensiveProcs
            if (ad != null && ad.Attacker == this && oProcEffects != null && ad.AttackType != AttackData.eAttackType.Spell && ad.AttackResult != eAttackResult.Missed)
            {
                for (int i = 0; i < oProcEffects.Count; i++)
                {
                    var oProcEffect = oProcEffects[i];

                    (oProcEffect.SpellHandler as OffensiveProcSpellHandler).EventHandler(ad);
                }
            }

            DirtyTricksECSGameEffect dt = (DirtyTricksECSGameEffect)EffectListService.GetAbilityEffectOnTarget(this, eEffect.DirtyTricks);
            if (dt != null)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    player.Out.SendSpellEffectAnimation(this, ad.Target, 471, 0, false, 1);

                dt.EventHandler(ad);
            }

            TripleWieldECSGameEffect tw = (TripleWieldECSGameEffect)EffectListService.GetAbilityEffectOnTarget(this, eEffect.TripleWield);
            if (tw != null)
            {
                tw.EventHandler(ad);
            }

            if (ad.Target is IGamePlayer && ad.Target != this)
            {
                LastAttackTickPvP = GameLoop.GameLoopTime;
            }
            else
            {
                LastAttackTickPvE = GameLoop.GameLoopTime;
            }

            // Don't cancel offensive focus spell
            if (ad.AttackType != eAttackType.Spell)
                CancelFocusSpell();
        }

        /// <summary>
        /// Stores the amount of realm points gained by other players on last death
        /// </summary>
        protected long m_lastDeathRealmPoints;

        /// <summary>
        /// Gets/sets the amount of realm points gained by other players on last death
        /// </summary>
        public long LastDeathRealmPoints
        {
            get { return m_lastDeathRealmPoints; }
            set { m_lastDeathRealmPoints = value; }
        }

        /// <summary>
        /// Method to broadcast Player to Discord
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="realm">The realm</param>
        public static void BroadcastDeathOnDiscord(string message, string name, string lastname, string playerClass, int level, long playedTime)
        {
            int color = 0;
            TimeSpan timeLived = TimeSpan.FromSeconds(playedTime);
            string timeLivedString = timeLived.Days + "d " + timeLived.Hours + "h " + timeLived.Minutes + "m ";

            string playerName = "";
            if (lastname != "")
                playerName = name + " " + lastname;
            else
                playerName = name;

            var DiscordObituaryHook =
                "https://discord.com/api/webhooks/929154632389910558/kfJbtzDC9JzyOXvZ0rYUwaPM31LRUebGzDZKSczUKDk_4YyHmB-WJVsh7pJoa4M9-D1U"; // Make it a property later
            var client = new DiscordWebhookClient(DiscordObituaryHook);

            // Create your DiscordMessage with all parameters of your message.
            var discordMessage = new DiscordMessage(
                "",
                username: "Atlas Obituary",
                avatarUrl: "https://cdn.discordapp.com/attachments/919610633656369214/928726197645496382/skull2.png",
                tts: false,
                embeds: new[]
                {
                    new DiscordMessageEmbed(
                        author: new DiscordMessageEmbedAuthor(playerName),
                        color: color,
                        description: message,
                        fields: new[]
                        {
                            new DiscordMessageEmbedField("Level", level.ToString()),
                            new DiscordMessageEmbedField("Class", playerClass),
                            new DiscordMessageEmbedField("Time alive", timeLivedString)
                        }
                    )
                }
            );
            client.SendToDiscord(discordMessage);
        }

        /// <summary>
        /// Called when the player dies
        /// </summary>
        /// <param name="killer">the killer</param>
        public override void ProcessDeath(GameObject killer)
        {
            // Ambient trigger upon killing player
            if (killer is GameNPC)
                (killer as GameNPC).FireAmbientSentence(GameNPC.eAmbientTrigger.killing, killer as GameLiving);

            CharacterClass.Die(killer);

            bool killingBlowByEnemyRealm = killer != null && killer.Realm != eRealm.None && killer.Realm != Realm;

            TargetObject = null;

            string playerMessage;
            string publicMessage;
            ushort messageDistance = WorldMgr.DEATH_MESSAGE_DISTANCE;
            m_releaseType = eReleaseType.Normal;

            string location = "";
            if (CurrentAreas.Count > 0 && (CurrentAreas[0] is Area.BindArea) == false)
                location = (CurrentAreas[0] as AbstractArea).Description;
            else
                location = CurrentZone.Description;

            if (killer == null)
            {
                if (killingBlowByEnemyRealm)
                {
                    playerMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledLocation", GetName(0, true), location);
                    publicMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledLocation", GetName(0, true), location);
                }
                else
                {
                    playerMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.Killed", GetName(0, true));
                    publicMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.Killed", GetName(0, true));
                }
            }
            else
            {
                if (IsDuelPartner(killer as GameLiving))
                {
                    m_releaseType = eReleaseType.Duel;
                    messageDistance = WorldMgr.YELL_DISTANCE;

                    playerMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.DuelDefeated", GetName(0, true), killer.GetName(1, false));
                    publicMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.DuelDefeated", GetName(0, true), killer.GetName(1, false));
                }
                else
                {
                    messageDistance = 0;

                    if (killingBlowByEnemyRealm)
                    {
                        if (killer is MimicNPC mimic)
                        {
                            mimic.Kills++;
                            mimic.KillStreak++;
                        }

                        KillStreak = 0;

                        switch (CurrentRegionID)
                        {
                            // Thid
                            case 252: MimicBattlegrounds.ThidBattleground.UpdateBattleStats(this); break;
                        }

                        playerMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledByLocation", GetName(0, true), killer.GetName(1, false), location);
                        publicMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledByLocation", GetName(0, true), killer.GetName(1, false), location);
                    }
                    else
                    {
                        playerMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledBy", GetName(0, true), killer.GetName(1, false));
                        publicMessage = LanguageMgr.GetTranslation("EN", "GamePlayer.Die.KilledBy", GetName(0, true), killer.GetName(1, false));
                    }
                }
            }

            Duel?.Stop();

            eChatType messageType;

            if (m_releaseType == eReleaseType.Duel)
                messageType = eChatType.CT_Emote;
            else if (killer == null)
            {
                messageType = eChatType.CT_PlayerDied;
            }
            else
            {
                switch (killer.Realm)
                {
                    case eRealm.Albion: messageType = eChatType.CT_KilledByAlb; break;
                    case eRealm.Midgard: messageType = eChatType.CT_KilledByMid; break;
                    case eRealm.Hibernia: messageType = eChatType.CT_KilledByHib; break;
                    default: messageType = eChatType.CT_PlayerDied; break; // killed by mob
                }
            }

            if (killer is GamePlayer && killer != this)
            {
                ((GamePlayer)killer).Out.SendMessage(LanguageMgr.GetTranslation(((GamePlayer)killer).Client.Account.Language, "GamePlayer.Die.YouKilled", GetName(0, false)), eChatType.CT_PlayerDied, eChatLoc.CL_SystemWindow);
                ((GamePlayer)killer).Out.SendMessage(playerMessage, messageType, eChatLoc.CL_SystemWindow);
            }

            List<GamePlayer> players;

            if (messageDistance == 0)
                players = ClientService.GetPlayersOfRegion(CurrentRegion);
            else
                players = GetPlayersInRadius(messageDistance);

            foreach (GamePlayer player in players)
            {
                // on normal server type send messages only to the killer and dead players realm
                // check for gameplayer is needed because killers realm don't see deaths by guards
                if (player.Realm == Realm
                    || (Properties.DEATH_MESSAGES_ALL_REALMS && (killer is IGamePlayer || killer is GameKeepGuard))) //Only show Player/Guard kills if shown to all realms

                    player.Out.SendMessage(publicMessage, messageType, eChatLoc.CL_SystemWindow);
            }

            IsSitting = false;
            IsSwimming = false;

            // then buffs drop messages
            //GameLivingProcessDeath(killer);

            if (ControlledBrain != null)
                CommandNpcRelease();

            base.ProcessDeath(killer);

            if (m_releaseType == eReleaseType.Duel)
            {
                foreach (GamePlayer player in killer.GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
                {
                    if (player != killer)
                        // Message: {0} wins the duel!
                        player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GamePlayer.Duel.Die.KillerWinsDuel", killer.Name), eChatType.CT_Emote, eChatLoc.CL_SystemWindow);
                }
                // Message: {0} wins the duel!
                //Message.SystemToOthers(Client, LanguageMgr.GetTranslation(this, "GamePlayer.Duel.Die.KillerWinsDuel", killer.Name), eChatType.CT_Emote);

                Release(m_releaseType, false);
            }

            //lock (m_LockObject)
            //{
            //if (m_releaseTimer != null)
            //{
            //    m_releaseTimer.Stop();
            //    m_releaseTimer = null;
            //}

            //if (m_healthRegenerationTimer != null)
            //{
            //    m_healthRegenerationTimer.Stop();
            //    m_healthRegenerationTimer = null;
            //}

            //m_automaticRelease = m_releaseType == eReleaseType.Duel;
            //m_releasePhase = 0;
            //m_deathTick = GameLoop.GameLoopTime; // we use realtime, because timer window is realtime

            //m_releaseTimer = new ECSGameTimer(this);
            //m_releaseTimer.Callback = new ECSGameTimer.ECSTimerCallback(ReleaseTimerCallback);
            //m_releaseTimer.Start(1000);

            // clear target object so no more actions can used on this target, spells, styles, attacks...
            //TargetObject = null;

            //// first penalty is 5% of expforlevel, second penalty comes from release
            //int xpLossPercent;
            //if (Level < 40)
            //{
            //    xpLossPercent = MaxLevel - Level;
            //}
            //else
            //{
            //    xpLossPercent = MaxLevel - 40;
            //}

            //if (realmDeath || killer?.Realm == Realm) //Live PvP servers have 3 con loss on pvp death, can be turned off in server properties -Unty
            //{
            //    int conpenalty = 0;
            //    switch (GameServer.Instance.Configuration.ServerType)
            //    {
            //        case EGameServerType.GST_Normal:
            //        xpLossPercent = 0;
            //        m_deathtype = eDeathType.RvR;
            //        break;

            //        case EGameServerType.GST_PvP:
            //       // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Die.DeadRVR"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            //        xpLossPercent = 0;
            //        m_deathtype = eDeathType.PvP;
            //        if (ServerProperties.Properties.PVP_DEATH_CON_LOSS)
            //        {
            //            conpenalty = 3;
            //            TempProperties.SetProperty(DEATH_CONSTITUTION_LOSS_PROPERTY, conpenalty);
            //        }
            //        break;
            //    }
            //}
            //else
            //{
            //    if (Level >= ServerProperties.Properties.PVE_EXP_LOSS_LEVEL)
            //    {
            //        //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Die.LoseExperience"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            //        // if this is the first death in level, you lose only half the penalty
            //        switch (DeathCount)
            //        {
            //            case 0:
            //            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Die.DeathN1"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            //            xpLossPercent /= 3;
            //            break;
            //            case 1:
            //            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Die.DeathN2"), eChatType.CT_YouDied, eChatLoc.CL_SystemWindow);
            //            xpLossPercent = xpLossPercent * 2 / 3;
            //            break;
            //        }

            //        DeathCount++;
            //        m_deathtype = eDeathType.PvE;
            //        long xpLoss = (ExperienceForNextLevel - ExperienceForCurrentLevel) * xpLossPercent / 1000;
            //        GainExperience(new(-xpLoss, 0, 0, 0, 0, 0, false, true, eXPSource.Other));
            //        //TempProperties.SetProperty(DEATH_EXP_LOSS_PROPERTY, xpLoss);
            //    }

            //    if (Level >= ServerProperties.Properties.PVE_CON_LOSS_LEVEL)
            //    {
            //        int conLoss = DeathCount;
            //        if (conLoss > 3)
            //            conLoss = 3;
            //        else if (conLoss < 1)
            //            conLoss = 1;
            //        //TempProperties.SetProperty(DEATH_CONSTITUTION_LOSS_PROPERTY, conLoss);
            //    }

            //    if (realmDeath)
            //        LastDeathPvP = true;
            //}
            //GameEventMgr.AddHandler(this, GamePlayerEvent.Revive, new DOLEventHandler(OnRevive));
            //}

            //if (SiegeWeapon != null)
            //    SiegeWeapon.ReleaseControl();

            // sent after buffs drop
            // GamePlayer.Die.CorpseLies:		{0} just died. {1} corpse lies on the ground.
            //Message.SystemToOthers2(this, eChatType.CT_PlayerDied, "GamePlayer.Die.CorpseLies", GetName(0, true), GetPronoun(Client, 1, true));

            //if (m_releaseType == eReleaseType.Duel)
            //{
            //    foreach (GamePlayer player in killer.GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
            //    {
            //        if (player != killer)
            //            // Message: {0} wins the duel!
            //            player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GamePlayer.Duel.Die.KillerWinsDuel", killer.Name), eChatType.CT_Emote, eChatLoc.CL_SystemWindow);
            //    }
            //    // Message: {0} wins the duel!
            //    //Message.SystemToOthers(Client, LanguageMgr.GetTranslation(this, "GamePlayer.Duel.Die.KillerWinsDuel", killer.Name), eChatType.CT_Emote);
            //}

            // deal out exp and realm points based on server rules
            // no other way to keep correct message order...
            //GameServer.ServerRules.OnPlayerKilled(this, killer);
            //if (m_releaseType != eReleaseType.Duel)
            //    DeathTime = PlayedTime;

            //CancelAllConcentrationEffects();
            //effectListComponent.CancelAll();
        }

        // Needed to skip over GameNPC ProcessDeath
        public virtual void GameLivingProcessDeath(GameObject killer)
        {
            try
            {
                attackComponent.StopAttack();

                if (killer is GameLiving livingKiller)
                    attackComponent.Attackers.TryAdd(livingKiller, long.MaxValue);

                List<GamePlayer> playerAttackers = new();

                foreach (GameObject attacker in attackComponent.Attackers.Keys)
                {
                    if (attacker is not GameLiving livingAttacker)
                        continue;

                    GamePlayer player = attacker as GamePlayer;

                    if (attacker is GameNPC npcAttacker && npcAttacker.Brain is IControlledBrain npcAttackerBrain)
                    {
                        // Ok, we're a pet - if our Player owner isn't in the attacker list, let's make them a 'virtual' attacker
                        player = npcAttackerBrain.GetPlayerOwner();

                        if (player != null)
                        {
                            if (!attackComponent.Attackers.ContainsKey(player))
                            {
                                if (!playerAttackers.Contains(player))
                                    playerAttackers.Add(player);
                            }

                            // Pet gets the killed message as well
                            livingAttacker.EnemyKilled(this);
                        }
                    }

                    if (player != null)
                    {
                        if (!playerAttackers.Contains(player))
                            playerAttackers.Add(player);

                        if (player.Group != null)
                        {
                            foreach (GamePlayer groupPlayer in player.Group.GetPlayersInTheGroup())
                            {
                                if (groupPlayer.IsWithinRadius(this, WorldMgr.MAX_EXPFORKILL_DISTANCE) && playerAttackers.Contains(groupPlayer) == false)
                                    playerAttackers.Add(groupPlayer);
                            }
                        }
                    }
                    else
                        livingAttacker.EnemyKilled(this);
                }

                foreach (GamePlayer player in playerAttackers)
                    player.EnemyKilled(this);

                foreach (Quests.DataQuest q in DataQuestList)
                    q.Notify(GameLivingEvent.Dying, this, new DyingEventArgs(killer, playerAttackers));

                attackComponent.Attackers.Clear();

                // clear all of our targets
                rangeAttackComponent.AutoFireTarget = null;
                TargetObject = null;

                // cancel all left effects
                EffectList.CancelAll();
                effectListComponent.CancelAll();

                // Stop the regeneration timers
                StopHealthRegeneration();
                StopPowerRegeneration();
                StopEnduranceRegeneration();

                //Reduce health to zero
                Health = 0;

                // Remove all last attacked times
                LastAttackedByEnemyTickPvE = 0;
                LastAttackedByEnemyTickPvP = 0;

                //Let's send the notification at the end
                Notify(GameLivingEvent.Dying, this, new DyingEventArgs(killer));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                //isDying flag is ALWAYS set to false even if exception happens so it can get remove from the list
                isDeadOrDying = false;
            }
        }

        public override void EnemyKilled(GameLiving enemy)
        {
            if (Group != null)
            {
                foreach (GameLiving living in Group.GetMembersInTheGroup())
                {
                    if (living == this)
                        continue;

                    if (enemy.attackComponent.Attackers.ContainsKey(living))
                        continue;

                    if (IsWithinRadius(living, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                        Notify(GameLivingEvent.EnemyKilled, living, new EnemyKilledEventArgs(enemy));
                }
            }

            if (CurrentZone.IsRvR)
            {
                var activeConquest = ConquestService.ConquestManager.ActiveObjective;
                int baseContribution = enemy.RealmPointsValue / 2; //todo turn it into a server prop?

                if (activeConquest != null && GetDistance(new Point2D(activeConquest.Keep.X, activeConquest.Keep.Y)) <=
                    ServerProperties.Properties.MAX_CONQUEST_RANGE)
                {
                    //TODO: add something here
                    if (Group != null)
                    {
                        //activeConquest.Contribute(this, (baseContribution/Group.MemberCount) + 20); //offset to minimize the grouping penalty by a bit
                    }
                    else
                    {
                        //activeConquest.Contribute(this, baseContribution);
                    }
                }
            }

            base.EnemyKilled(enemy);
        }

        /// <summary>
        /// Check this flag to see wether this living is involved in combat
        /// </summary>
        public override bool InCombat => base.InCombat || MimicBrain.HasAggro;

        #endregion Combat

        #region Duel

        public GameDuel Duel { get; private set; }
        public GameLiving DuelPartner => Duel?.GetPartnerOf(this);
        public bool IsDuelReady { get; set; }

        public void OnDuelStart(GameDuel duel)
        {
            Duel?.Stop();
            Duel = duel;
        }

        public void OnDuelStop()
        {
            if (Duel == null)
                return;

            IsDuelReady = false;
            Duel = null;
        }

        public bool IsDuelPartner(GameLiving living)
        {
            if (living == null)
                return false;

            GameLiving partner = DuelPartner;

            if (partner == null)
                return false;

            if (living is GameNPC npc && npc.Brain is ControlledMobBrain brain)
                living = brain.GetLivingOwner();

            return partner == living;
        }

        #endregion Duel

        #region Stealth / Wireframe

        private bool m_isWireframe = false;

        /// <summary>
        /// Player is drawn as a Wireframe.  Not sure why or when this is used.  -- Tolakram
        /// </summary>
        //public bool IsWireframe
        //{
        //    get { return m_isWireframe; }
        //    set
        //    {
        //        bool needUpdate = m_isWireframe != value;
        //        m_isWireframe = value;
        //        if (needUpdate && ObjectState == eObjectState.Active)
        //            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
        //            {
        //                if (player == null) continue;
        //                player.Out.SendPlayerModelTypeChange(this, (byte)(value ? 1 : 0));
        //            }
        //    }
        //}

        private bool m_isTorchLighted = false;

        /// <summary>
        /// Is player Torch lighted ?
        /// </summary>
        public bool IsTorchLighted
        {
            get { return m_isTorchLighted; }
            set { m_isTorchLighted = value; }
        }

        /// <summary>
        /// Property that holds tick when stealth state was changed last time
        /// </summary>
        public const string STEALTH_CHANGE_TICK = "StealthChangeTick";

        /// <summary>
        /// The stealth state of this player
        /// </summary>
        public override bool IsStealthed => effectListComponent.ContainsEffectForEffectType(eEffect.Stealth);

        /// <summary>
        /// Set player's stealth state
        /// </summary>
        /// <param name="goStealth">true is stealthing, false if unstealthing</param>
        public override void Stealth(bool goStealth)
        {
            if (IsStealthed == goStealth)
                return;

            if (goStealth && !InCombat)
            {
                if (effectListComponent.ContainsEffectForEffectType(eEffect.Pulse))
                    return;

                new StealthECSGameEffect(new ECSGameEffectInitParams(this, 0, 1));
                return;
            }

            if (effectListComponent.ContainsEffectForEffectType(eEffect.Stealth))
                EffectService.RequestImmediateCancelEffect(EffectListService.GetEffectOnTarget(this, eEffect.Stealth), false);

            if (effectListComponent.ContainsEffectForEffectType(eEffect.Vanish))
                EffectService.RequestImmediateCancelEffect(EffectListService.GetEffectOnTarget(this, eEffect.Vanish));
        }

        // UncoverStealthAction is what unstealths player if they are too close to mobs.
        public void StartStealthUncoverAction()
        {
            MimicUncoverStealthAction action = TempProperties.GetProperty<MimicUncoverStealthAction>(UNCOVER_STEALTH_ACTION_PROP);
            //start the uncover timer
            if (action == null)
                action = new MimicUncoverStealthAction(this);

            action.Interval = 1000;
            action.Start(1000);
            TempProperties.SetProperty(UNCOVER_STEALTH_ACTION_PROP, action);
        }

        // UncoverStealthAction is what unstealths player if they are too close to mobs.
        public void StopStealthUncoverAction()
        {
            MimicUncoverStealthAction action = TempProperties.GetProperty<MimicUncoverStealthAction>(UNCOVER_STEALTH_ACTION_PROP);
            //stop the uncover timer
            if (action != null)
            {
                action.Stop();
                TempProperties.RemoveProperty(UNCOVER_STEALTH_ACTION_PROP);
            }
        }

        /// <summary>
        /// The temp property that stores the uncover stealth action
        /// </summary>
        protected const string UNCOVER_STEALTH_ACTION_PROP = "UncoverStealthAction";

        /// <summary>
        /// Uncovers the player if a mob is too close
        /// </summary>
        protected class MimicUncoverStealthAction : ECSGameTimerWrapperBase
        {
            /// <summary>
            /// Constructs a new uncover stealth action
            /// </summary>
            /// <param name="actionSource">The action source</param>
            public MimicUncoverStealthAction(MimicNPC actionSource) : base(actionSource) { }

            /// <summary>
            /// Called on every timer tick
            /// </summary>
            protected override int OnTick(ECSGameTimer timer)
            {
                MimicNPC mimic = (MimicNPC)timer.Owner;

                foreach (GameNPC npc in mimic.GetNPCsInRadius(1024))
                {
                    // Friendly mobs do not uncover stealthed players
                    if (!GameServer.ServerRules.IsAllowedToAttack(npc, mimic, true))
                        continue;

                    // Npc with player owner don't uncover
                    if (npc.Brain != null
                        && (npc.Brain as IControlledBrain) != null
                        && (npc.Brain as IControlledBrain).GetPlayerOwner() != null)
                        continue;

                    double npcLevel = Math.Max(npc.Level, 1.0);
                    double stealthLevel = mimic.GetModifiedSpecLevel(Specs.Stealth);
                    double detectRadius = 125.0 + ((npcLevel - stealthLevel) * 20.0);

                    // we have detect hidden and enemy don't = higher range
                    if (npc.HasAbility("Detect Hidden") && EffectListService.GetAbilityEffectOnTarget(mimic, eEffect.Camouflage) == null)
                        detectRadius += 125;

                    if (detectRadius < 126) detectRadius = 126;

                    double distanceToPlayer = npc.GetDistanceTo(mimic);

                    if (distanceToPlayer > detectRadius)
                        continue;

                    double fieldOfView = 90.0;  //90 degrees  = standard FOV
                    double fieldOfListen = 120.0; //120 degrees = standard field of listening

                    if (npc.Level > 50)
                        fieldOfListen += (npc.Level - mimic.Level) * 3;

                    double angle = npc.GetAngle(mimic);

                    //player in front
                    fieldOfView /= 2.0;
                    bool canSeePlayer = (angle >= 360 - fieldOfView || angle < fieldOfView);

                    //If npc can not see nor hear the player, continue the loop
                    fieldOfListen /= 2.0;
                    if (canSeePlayer == false &&
                        !(angle >= (45 + 60) - fieldOfListen && angle < (45 + 60) + fieldOfListen) &&
                        !(angle >= (360 - 45 - 60) - fieldOfListen && angle < (360 - 45 - 60) + fieldOfListen))
                        continue;

                    double chanceMod = 1.0;

                    //Chance to detect player decreases after 125 coordinates!
                    if (distanceToPlayer > 125)
                        chanceMod = 1f - (distanceToPlayer - 125.0) / (detectRadius - 125.0);

                    double chanceToUncover = 0.1 + (npc.Level - stealthLevel) * 0.01 * chanceMod;

                    if (Util.ChanceDouble(chanceToUncover))
                    {
                        if (!canSeePlayer)
                            npc.TurnTo(mimic, 10000);
                    }
                }

                return Interval;
            }
        }

        /// <summary>
        /// This handler is called by the unstealth check of mobs
        /// </summary>
        public void UncoverLosHandler(GamePlayer player, eLosCheckResponse response, ushort sourceOID, ushort targetOID)
        {
            GameObject target = CurrentRegion.GetObject(targetOID);

            if ((target == null) || (player.IsStealthed == false))
                return;

            if (response is eLosCheckResponse.TRUE)
            {
                player.Out.SendMessage(target.GetName(0, true) + " uncovers you!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                player.Stealth(false);
            }
        }

        /// <summary>
        /// Checks whether this player can detect stealthed enemy
        /// </summary>
        /// <param name="enemy"></param>
        /// <returns>true if enemy can be detected</returns>
        public virtual bool CanDetect(IGamePlayer enemy)
        {
            if (enemy.CurrentRegionID != CurrentRegionID)
                return false;

            if (!IsAlive)
                return false;

            if (enemy.EffectList.GetOfType<VanishEffect>() != null)
                return false;

            if (Client.Account.PrivLevel > 1)
                return true;

            if (enemy.Client.Account.PrivLevel > 1)
                return false;

            if (effectListComponent.ContainsEffectForEffectType(eEffect.TrueSight))
                return true;

            if (HasAbilityType(typeof(AtlasOF_SeeHidden))
                && (enemy.CharacterClass is ClassMinstrel
                     || enemy.CharacterClass is ClassRanger
                     || enemy.CharacterClass is ClassHunter
                     || enemy.CharacterClass is ClassScout)
                && IsWithinRadius((GameObject)enemy, 650)
                && !enemy.EffectListComponent.ContainsEffectForEffectType(eEffect.Camouflage))
            {
                return true;
            }

            /*
             * http://www.critshot.com/forums/showthread.php?threadid=3142
             * The person doing the looking has a chance to find them based on their level, minus the stealthed person's stealth spec.
             *
             * -Normal detection range = (enemy lvl  your stealth spec) * 20 + 125
             * -Detect Hidden Range = (enemy lvl  your stealth spec) * 50 + 250
             * -See Hidden range = 2700 - (38 * your stealth spec)
             */

            int EnemyStealthLevel = enemy.GetModifiedSpecLevel(Specs.Stealth);

            if (EnemyStealthLevel > 50)
                EnemyStealthLevel = 50;

            int levelDiff = Level - EnemyStealthLevel;

            if (levelDiff < 0)
                levelDiff = 0;

            int range = 0;
            bool enemyHasCamouflage = EffectListService.GetAbilityEffectOnTarget((GameLiving)enemy, eEffect.Camouflage) != null;
            bool enemyHasVanish = EffectListService.GetAbilityEffectOnTarget((GameLiving)enemy, eEffect.Vanish) != null;
            if (HasAbility("Detect Hidden") && !enemyHasVanish && !enemyHasCamouflage)
            {
                // we have detect hidden and enemy don't = higher range
                range = levelDiff * 50 + 250; // Detect Hidden advantage
                //range = levelDiff * 50 + 300; // Detect Hidden advantage
            }
            else
            {
                //range = levelDiff * 20 + 125; // Normal detection range
                range = levelDiff * 20 + 125;
            }

            //if (ConquestService.ConquestManager.IsPlayerNearFlag(this))
            //{
            //    range += 50;
            //}

            // Mastery of Stealth Bonus
            /*
             //removed, this is NF MoStealth. OF Version does not add range, only movespeed
            RAPropertyEnhancer mos = GetAbility<MasteryOfStealthAbility>();
            if (mos != null && !enemyHasCamouflage)
                if (!HasAbility(Abilities.DetectHidden) || !enemy.HasAbility(Abilities.DetectHidden))
                    range += mos.GetAmountForLevel(CalculateSkillLevel(mos));
            */
            range += BaseBuffBonusCategory[(int)eProperty.Skill_Stealth];

            // //Buff (Stealth Detection)
            // //Increases the target's ability to detect stealthed players and monsters.
            // GameSpellEffect iVampiirEffect = SpellHandler.FindEffectOnTarget((GameLiving)this, "VampiirStealthDetection");
            // if (iVampiirEffect != null)
            //     range += (int)iVampiirEffect.Spell.Value;
            //
            // //Infill Only - Greater Chance to Detect Stealthed Enemies for 1 minute
            // //after executing a klling blow on a realm opponent.
            // GameSpellEffect HeightenedAwareness = SpellHandler.FindEffectOnTarget((GameLiving)this, "HeightenedAwareness");
            // if (HeightenedAwareness != null)
            //     range += (int)HeightenedAwareness.Spell.Value;
            //
            // //Nightshade Only - Greater chance of remaining hidden while stealthed for 1 minute
            // //after executing a killing blow on a realm opponent.
            // GameSpellEffect SubtleKills = SpellHandler.FindEffectOnTarget((GameLiving)enemy, "SubtleKills");
            // if (SubtleKills != null)
            // {
            //     range -= (int)SubtleKills.Spell.Value;
            //     if (range < 0) range = 0;
            // }
            //
            // // Apply Blanket of camouflage effect
            // GameSpellEffect iSpymasterEffect1 = SpellHandler.FindEffectOnTarget((GameLiving)enemy, "BlanketOfCamouflage");
            // if (iSpymasterEffect1 != null)
            // {
            //     range -= (int)iSpymasterEffect1.Spell.Value;
            //     if (range < 0) range = 0;
            // }
            //
            // // Apply Lookout effect
            // GameSpellEffect iSpymasterEffect2 = SpellHandler.FindEffectOnTarget((GameLiving)this, "Loockout");
            // if (iSpymasterEffect2 != null)
            //     range += (int)iSpymasterEffect2.Spell.Value;
            //
            // // Apply Prescience node effect
            // GameSpellEffect iConvokerEffect = SpellHandler.FindEffectOnTarget((GameLiving)enemy, "Prescience");
            // if (iConvokerEffect != null)
            //     range += (int)iConvokerEffect.Spell.Value;

            //Hard cap is 1900
            if (range > 1900)
                range = 1900;

            //everyone can see your own group stealthed
            else if (enemy.Group != null && Group != null && enemy.Group == Group)
            {
                range = 2500;
            }

            // Fin
            // vampiir stealth range, uncomment when add eproperty stealthrange i suppose
            return IsWithinRadius((GameObject)enemy, range);
        }

        #endregion Stealth / Wireframe

        #region Equipment/Encumberance

        /// <summary>
        /// Gets the total possible Encumberance
        /// </summary>
        public virtual int MaxEncumberance
        {
            get
            {
                double enc = (double)Strength;
                RAPropertyEnhancer ab = GetAbility<AtlasOF_LifterAbility>();
                if (ab != null)
                    enc *= 1 + ((double)ab.Amount / 100);

                return (int)enc;
            }
        }

        /// <summary>
        /// Gets the current Encumberance
        /// </summary>
        public virtual int Encumberance
        {
            get { return Inventory.InventoryWeight; }
        }

        /// <summary>
        /// The Encumberance state of this player
        /// </summary>
        protected bool m_overencumbered = true;

        /// <summary>
        /// Gets/Set the players Encumberance state
        /// </summary>
        public bool IsOverencumbered
        {
            get { return m_overencumbered; }
            set { m_overencumbered = value; }
        }

        /// <summary>
        /// Updates the appearance of the equipment this player is using
        /// </summary>
        public virtual void UpdateEquipmentAppearance()
        {
            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (player == null)
                    continue;

                player.Out.SendLivingEquipmentUpdate(this);
            }
        }

        /// <summary>
        /// Updates Encumberance and its effects
        /// </summary>
        public void UpdateEncumberance()
        {
            if (Inventory.InventoryWeight > MaxEncumberance)
            {
                //IsOverencumbered = true;
                // Out.SendUpdateMaxSpeed();
                //   if (MaxSpeed == 0)
                //   Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "PropertyCalc.MaxSpeed.YouAreEncumbered"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                // else
                // Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.UpdateEncumberance.EncumberedMoveSlowly"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
            else if (IsOverencumbered)
            {
                //IsOverencumbered = false;
                //Out.SendUpdateMaxSpeed();
            }
            //Out.SendEncumberance();
        }

        public override void UpdateHealthManaEndu()
        {
            //Out.SendCharStatsUpdate();
            //Out.SendUpdateWeaponAndArmorStats();
            //UpdateEncumberance();
            //UpdatePlayerStatus();
            base.UpdateHealthManaEndu();
        }

        /// <summary>
        /// Get the bonus names
        /// </summary>
        public string ItemBonusName(int BonusType)
        {
            string BonusName = string.Empty;

            if (BonusType == 1) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus1");//Strength
            if (BonusType == 2) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus2");//Dexterity
            if (BonusType == 3) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus3");//Constitution
            if (BonusType == 4) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus4");//Quickness
            if (BonusType == 5) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus5");//Intelligence
            if (BonusType == 6) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus6");//Piety
            if (BonusType == 7) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus7");//Empathy
            if (BonusType == 8) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus8");//Charisma
            if (BonusType == 9) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus9");//Power
            if (BonusType == 10) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus10");//Hits
            if (BonusType == 11) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus11");//Body
            if (BonusType == 12) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus12");//Cold
            if (BonusType == 13) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus13");//Crush
            if (BonusType == 14) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus14");//Energy
            if (BonusType == 15) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus15");//Heat
            if (BonusType == 16) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus16");//Matter
            if (BonusType == 17) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus17");//Slash
            if (BonusType == 18) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus18");//Spirit
            if (BonusType == 19) BonusName = LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.ItemBonusName.Bonus19");//Thrust
            return BonusName;
        }

        /// <summary>
        /// Adds magical bonuses whenever item was equipped
        /// </summary>
        /// <param name="e"></param>
        /// <param name="sender">inventory</param>
        /// <param name="arguments"></param>
        public virtual void OnItemEquipped(DbInventoryItem item, eInventorySlot slot)
        {
            if (item == null)
                return;

            if (item is IGameInventoryItem inventoryItem)
                inventoryItem.OnEquipped(this);

            if (item.Item_Type is >= Slot.RIGHTHAND and <= Slot.RANGED)
            {
                if (item.Hand == 1) // 2h
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.WieldBothHands", item.GetName(0, false))), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                else if (item.SlotPosition == Slot.LEFTHAND)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.WieldLeftHand", item.GetName(0, false))), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                else
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.WieldRightHand", item.GetName(0, false))), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }

            if ((eInventorySlot)item.Item_Type == eInventorySlot.Horse)
            {
                if (item.SlotPosition == Slot.HORSE)
                {
                    ActiveHorse.ID = (byte)(item.SPD_ABS == 0 ? 1 : item.SPD_ABS);
                    ActiveHorse.Name = item.Creator;
                }

                return;
            }
            else if ((eInventorySlot)item.Item_Type == eInventorySlot.HorseArmor)
            {
                if (item.SlotPosition == Slot.HORSEARMOR)
                    ActiveHorse.Saddle = (byte)item.DPS_AF;

                return;
            }
            else if ((eInventorySlot)item.Item_Type == eInventorySlot.HorseBarding)
            {
                if (item.SlotPosition == Slot.HORSEBARDING)
                    ActiveHorse.Barding = (byte)item.DPS_AF;

                return;
            }

            if (!item.IsMagical)
                return;

            Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Magic", item.GetName(0, false))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);

            if (item.Bonus1 != 0)
            {
                ItemBonus[item.Bonus1Type] += item.Bonus1;

                if (item.Bonus1Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus1Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus2 != 0)
            {
                ItemBonus[item.Bonus2Type] += item.Bonus2;

                if (item.Bonus2Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus2Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus3 != 0)
            {
                ItemBonus[item.Bonus3Type] += item.Bonus3;

                if (item.Bonus3Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus3Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus4 != 0)
            {
                ItemBonus[item.Bonus4Type] += item.Bonus4;

                if (item.Bonus4Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus4Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus5 != 0)
            {
                ItemBonus[item.Bonus5Type] += item.Bonus5;

                if (item.Bonus5Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus5Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus6 != 0)
            {
                ItemBonus[item.Bonus6Type] += item.Bonus6;

                if (item.Bonus6Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus6Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus7 != 0)
            {
                ItemBonus[item.Bonus7Type] += item.Bonus7;

                if (item.Bonus7Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus7Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus8 != 0)
            {
                ItemBonus[item.Bonus8Type] += item.Bonus8;

                if (item.Bonus8Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus8Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus9 != 0)
            {
                ItemBonus[item.Bonus9Type] += item.Bonus9;

                if (item.Bonus9Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus9Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus10 != 0)
            {
                ItemBonus[item.Bonus10Type] += item.Bonus10;

                if (item.Bonus10Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemEquipped.Increased", ItemBonusName(item.Bonus10Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.ExtraBonus != 0)
                ItemBonus[item.ExtraBonusType] += item.ExtraBonus;

            if ((ePrivLevel)Client.Account.PrivLevel == ePrivLevel.Player && Client.Player != null && Client.Player.ObjectState == eObjectState.Active)
            {
                if (item.SpellID > 0 || item.SpellID1 > 0)
                    TempProperties.SetProperty("ITEMREUSEDELAY" + item.Id_nb, CurrentRegion.Time);
            }

            // This was used during Atlas to only allow two self buff charges.
            /*// Max 2 charges.
            if (item.SpellID > 0 && SelfBuffChargeIDs.Contains(item.SpellID) && LoyaltyManager.GetPlayerRealmLoyalty(this).Days > 30)
            {
                if (ActiveBuffCharges < 2)
                    UseItemCharge(item, (int) eUseType.Use1);
                else
                {
                    Out.SendMessage("You may only use two buff charge effects. This item fails to affect you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
            }

            // Max 2 charges.
            if (item.SpellID1 > 0 && SelfBuffChargeIDs.Contains(item.SpellID1) && LoyaltyManager.GetPlayerRealmLoyalty(this).Days > 30)
            {
                if (ActiveBuffCharges < 2)
                    UseItemCharge(item, (int) eUseType.Use2);
                else
                {
                    Out.SendMessage("You may only use two buff charge effects. This item fails to affect you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
            }*/

            _statsSenderOnEquipmentChange ??= new(this, OnStatsSendCompletionAfterEquipmentChange);
        }

        private StatsSenderOnEquipmentChange _statsSenderOnEquipmentChange;

        private int OnStatsSendCompletionAfterEquipmentChange()
        {
            _statsSenderOnEquipmentChange = null;
            return 0; // Must return 0 to stop the timer. This is just to make `OnTick` cleaner.
        }

        public class StatsSenderOnEquipmentChange : ECSGameTimerWrapperBase
        {
            private new MimicNPC Owner { get; }
            private Func<int> _onCompletion;

            public StatsSenderOnEquipmentChange(GameObject owner, Func<int> OnCompletion) : base(owner)
            {
                Owner = owner as MimicNPC;
                _onCompletion = OnCompletion;
                Start(0);
            }

            protected override int OnTick(ECSGameTimer timer)
            {
                if (Owner.ObjectState is not eObjectState.Active)
                    return _onCompletion();

                if (!IsAlive)
                    return _onCompletion();

                int maxHealth = Owner.MaxHealth;

                if (Owner.Health < maxHealth)
                    Owner.StartHealthRegeneration();
                else if (Owner.Health > maxHealth)
                    Owner.Health = maxHealth;

                int maxMana = Owner.MaxMana;

                if (Owner.Mana < maxMana)
                    Owner.StartPowerRegeneration();
                else if (Owner.Mana > maxMana)
                    Owner.Mana = maxMana;

                int maxEndurance = Owner.MaxEndurance;

                if (Owner.Endurance < maxEndurance)
                    Owner.StartEnduranceRegeneration();
                else if (Owner.Endurance > maxEndurance)
                    Owner.Endurance = maxEndurance;

                return _onCompletion();
            }
        }

        private int m_activeBuffCharges = 0;

        public int ActiveBuffCharges
        {
            get
            {
                return m_activeBuffCharges;
            }
            set
            {
                m_activeBuffCharges = value;
            }
        }

        private List<int> m_selfBuffIds;

        public List<int> SelfBuffChargeIDs
        {
            get
            {
                if (m_selfBuffIds == null)
                {
                    m_selfBuffIds = new List<int>();
                    m_selfBuffIds.Add(31133); //str/con charge
                    m_selfBuffIds.Add(31132); //dex/qui charge
                    m_selfBuffIds.Add(31131); //acuity charge
                    m_selfBuffIds.Add(31130); //AF charge
                }

                return m_selfBuffIds;
            }
        }

        /// <summary>
        /// Removes magical bonuses whenever item was unequipped
        /// </summary>
        /// <param name="e"></param>
        /// <param name="sender">inventory</param>
        /// <param name="arguments"></param>
        public virtual void OnItemUnequipped(DbInventoryItem item, eInventorySlot slot)
        {
            if (item == null)
                return;

            if (item.Item_Type is >= Slot.RIGHTHAND and <= Slot.RANGED)
            {
                if (item.Hand == 1) // 2h
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.BothHandsFree", item.GetName(0, false))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
                else if (slot == eInventorySlot.LeftHandWeapon)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.LeftHandFree", item.GetName(0, false))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
                else
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.RightHandFree", item.GetName(0, false))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            //if (slot == eInventorySlot.Mythical && (eInventorySlot)item.Item_Type == eInventorySlot.Mythical && item is GameMythirian mythirian)
            //    mythirian.OnUnEquipped(this);

            if ((eInventorySlot)item.Item_Type == eInventorySlot.Horse)
            {
                if (IsOnHorse)
                    IsOnHorse = false;

                ActiveHorse.ID = 0;
                ActiveHorse.Name = "";
                return;
            }
            else if ((eInventorySlot)item.Item_Type == eInventorySlot.HorseArmor)
            {
                ActiveHorse.Saddle = 0;
                return;
            }
            else if ((eInventorySlot)item.Item_Type == eInventorySlot.HorseBarding)
            {
                ActiveHorse.Barding = 0;
                return;
            }

            // Cancel any self buffs that are unequipped.
            if (item.SpellID > 0 && SelfBuffChargeIDs.Contains(item.SpellID) && Inventory.EquippedItems.Where(x => x.SpellID == item.SpellID).Count() <= 1)
                CancelChargeBuff(item.SpellID);

            if (!item.IsMagical)
                return;

            if (item.Bonus1 != 0)
            {
                ItemBonus[item.Bonus1Type] -= item.Bonus1;

                if (item.Bonus1Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus1Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus2 != 0)
            {
                ItemBonus[item.Bonus2Type] -= item.Bonus2;

                if (item.Bonus2Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus2Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus3 != 0)
            {
                ItemBonus[item.Bonus3Type] -= item.Bonus3;

                if (item.Bonus3Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus3Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus4 != 0)
            {
                ItemBonus[item.Bonus4Type] -= item.Bonus4;

                if (item.Bonus4Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus4Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus5 != 0)
            {
                ItemBonus[item.Bonus5Type] -= item.Bonus5;

                if (item.Bonus5Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus5Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus6 != 0)
            {
                ItemBonus[item.Bonus6Type] -= item.Bonus6;

                if (item.Bonus6Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus6Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus7 != 0)
            {
                ItemBonus[item.Bonus7Type] -= item.Bonus7;

                if (item.Bonus7Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus7Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus8 != 0)
            {
                ItemBonus[item.Bonus8Type] -= item.Bonus8;

                if (item.Bonus8Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus8Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus9 != 0)
            {
                ItemBonus[item.Bonus9Type] -= item.Bonus9;

                if (item.Bonus9Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus9Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.Bonus10 != 0)
            {
                ItemBonus[item.Bonus10Type] -= item.Bonus10;

                if (item.Bonus10Type < 20)
                    Out.SendMessage(string.Format(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.OnItemUnequipped.Decreased", ItemBonusName(item.Bonus10Type))), eChatType.CT_Skill, eChatLoc.CL_SystemWindow);
            }

            if (item.ExtraBonus != 0)
                ItemBonus[item.ExtraBonusType] -= item.ExtraBonus;

            if (item is IGameInventoryItem inventoryItem)
                inventoryItem.OnUnEquipped(this);

            if (ObjectState == eObjectState.Active)
            {
                //Out.SendCharStatsUpdate();
                //Out.SendCharResistsUpdate();
                //Out.SendUpdateWeaponAndArmorStats();
                //Out.SendUpdateMaxSpeed();
                //Out.SendEncumberance();
                //Out.SendUpdatePlayerSkills();
                //UpdatePlayerStatus();

                if (IsAlive)
                {
                    if (Health < MaxHealth)
                        StartHealthRegeneration();
                    else if (Health > MaxHealth)
                        Health = MaxHealth;

                    if (Mana < MaxMana)
                        StartPowerRegeneration();
                    else if (Mana > MaxMana)
                        Mana = MaxMana;

                    if (Endurance < MaxEndurance)
                        StartEnduranceRegeneration();
                    else if (Endurance > MaxEndurance)
                        Endurance = MaxEndurance;
                }
            }
        }

        private void CancelChargeBuff(int spellID)
        {
            EffectService.RequestCancelEffect(effectListComponent.GetSpellEffects().FirstOrDefault(x => x.SpellHandler.Spell.ID == spellID));
        }

        public virtual void RefreshItemBonuses()
        {
            m_itemBonus = new PropertyIndexer();
            string slotToLoad = string.Empty;
            switch (VisibleActiveWeaponSlots)
            {
                case 16: slotToLoad = "rightandleftHandSlot"; break;
                case 18: slotToLoad = "leftandtwoHandSlot"; break;
                case 31: slotToLoad = "leftHandSlot"; break;
                case 34: slotToLoad = "twoHandSlot"; break;
                case 51: slotToLoad = "distanceSlot"; break;
                case 240: slotToLoad = "righttHandSlot"; break;
                case 242: slotToLoad = "twoHandSlot"; break;
                default: break;
            }

            //log.Debug("VisibleActiveWeaponSlots= " + VisibleActiveWeaponSlots);
            foreach (DbInventoryItem item in Inventory.EquippedItems)
            {
                if (item == null)
                    continue;

                // skip weapons. only active weapons should fire equip event, done in player.SwitchWeapon
                bool add = true;
                if (slotToLoad != string.Empty)
                {
                    switch (item.SlotPosition)
                    {
                        case Slot.TWOHAND:
                        if (slotToLoad.Contains("twoHandSlot") == false)
                        {
                            add = false;
                        }
                        break;

                        case Slot.RIGHTHAND:
                        if (slotToLoad.Contains("right") == false)
                        {
                            add = false;
                        }
                        break;

                        case Slot.SHIELD:
                        case Slot.LEFTHAND:
                        if (slotToLoad.Contains("left") == false)
                        {
                            add = false;
                        }
                        break;

                        case Slot.RANGED:
                        if (slotToLoad != "distanceSlot")
                        {
                            add = false;
                        }
                        break;

                        default: break;
                    }
                }

                if (!add)
                    continue;

                if (item is IGameInventoryItem)
                {
                    //(item as IGameInventoryItem).CheckValid(this);
                }

                if (item.IsMagical)
                {
                    if (item.Bonus1 != 0)
                    {
                        ItemBonus[item.Bonus1Type] += item.Bonus1;
                    }
                    if (item.Bonus2 != 0)
                    {
                        ItemBonus[item.Bonus2Type] += item.Bonus2;
                    }
                    if (item.Bonus3 != 0)
                    {
                        ItemBonus[item.Bonus3Type] += item.Bonus3;
                    }
                    if (item.Bonus4 != 0)
                    {
                        ItemBonus[item.Bonus4Type] += item.Bonus4;
                    }
                    if (item.Bonus5 != 0)
                    {
                        ItemBonus[item.Bonus5Type] += item.Bonus5;
                    }
                    if (item.Bonus6 != 0)
                    {
                        ItemBonus[item.Bonus6Type] += item.Bonus6;
                    }
                    if (item.Bonus7 != 0)
                    {
                        ItemBonus[item.Bonus7Type] += item.Bonus7;
                    }
                    if (item.Bonus8 != 0)
                    {
                        ItemBonus[item.Bonus8Type] += item.Bonus8;
                    }
                    if (item.Bonus9 != 0)
                    {
                        ItemBonus[item.Bonus9Type] += item.Bonus9;
                    }
                    if (item.Bonus10 != 0)
                    {
                        ItemBonus[item.Bonus10Type] += item.Bonus10;
                    }
                    if (item.ExtraBonus != 0)
                    {
                        ItemBonus[item.ExtraBonusType] += item.ExtraBonus;
                    }
                }
            }
        }

        /// <summary>
        /// Handles a bonus change on an item.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected virtual void OnItemBonusChanged(int bonusType, int bonusAmount)
        {
            if (bonusType == 0 || bonusAmount == 0)
                return;

            ItemBonus[bonusType] += bonusAmount;

            if (ObjectState is eObjectState.Active)
            {
                //Out.SendCharStatsUpdate();
                //Out.SendCharResistsUpdate();
                //Out.SendUpdateWeaponAndArmorStats();
                //Out.SendUpdateMaxSpeed();
                //Out.SendEncumberance();
                // Out.SendUpdatePlayerSkills();
                //UpdatePlayerStatus();

                if (IsAlive)
                {
                    if (Health < MaxHealth)
                        StartHealthRegeneration();
                    else if (Health > MaxHealth)
                        Health = MaxHealth;

                    if (Mana < MaxMana)
                        StartPowerRegeneration();
                    else if (Mana > MaxMana)
                        Mana = MaxMana;

                    if (Endurance < MaxEndurance)
                        StartEnduranceRegeneration();
                    else if (Endurance > MaxEndurance)
                        Endurance = MaxEndurance;
                }
            }
        }

        #endregion Equipment/Encumberance

        #region Shade

        protected ShadeECSGameEffect m_ShadeEffect = null;

        /// <summary>
        /// The shade effect of this player
        /// </summary>
        public ShadeECSGameEffect ShadeEffect
        {
            get { return m_ShadeEffect; }
            set { m_ShadeEffect = value; }
        }

        /// <summary>
        /// Gets flag indication whether player is in shade mode
        /// </summary>
        public bool IsShade
        {
            get
            {
                bool shadeModel = Model == ShadeModel;
                return m_ShadeEffect != null ? true : shadeModel;
            }
        }

        /// <summary>
        /// Create a shade effect for this player.
        /// </summary>
        /// <returns></returns>
        protected virtual ShadeECSGameEffect CreateShadeEffect()
        {
            return CharacterClass.CreateShadeEffect();
        }

        /// <summary>
        /// The model ID used on character creation.
        /// </summary>
        public ushort CreationModel
        {
            get
            {
                return Model;
            }
        }

        /// <summary>
        /// The model ID used for shade morphs.
        /// </summary>
        public ushort ShadeModel
        {
            get
            {
                // Aredhel: Bit fishy, necro in caster from could use
                // Traitor's Dagger... FIXME!

                if (CharacterClass.ID == (int)eCharacterClass.Necromancer)
                    return 822;

                switch (Race)
                {
                    // Albion Models.
                    //case (int)eRace.Inconnu: return (ushort)(DBCharacter.Gender + 1351);
                    //case (int)eRace.Briton: return (ushort)(DBCharacter.Gender + 1353);
                    //case (int)eRace.Avalonian: return (ushort)(DBCharacter.Gender + 1359);
                    //case (int)eRace.Highlander: return (ushort)(DBCharacter.Gender + 1355);
                    //case (int)eRace.Saracen: return (ushort)(DBCharacter.Gender + 1357);
                    //case (int)eRace.HalfOgre: return (ushort)(DBCharacter.Gender + 1361);

                    //// Midgard Models.
                    //case (int)eRace.Troll: return (ushort)(DBCharacter.Gender + 1363);
                    //case (int)eRace.Dwarf: return (ushort)(DBCharacter.Gender + 1369);
                    //case (int)eRace.Norseman: return (ushort)(DBCharacter.Gender + 1365);
                    //case (int)eRace.Kobold: return (ushort)(DBCharacter.Gender + 1367);
                    //case (int)eRace.Valkyn: return (ushort)(DBCharacter.Gender + 1371);
                    //case (int)eRace.Frostalf: return (ushort)(DBCharacter.Gender + 1373);

                    //// Hibernia Models.
                    //case (int)eRace.Firbolg: return (ushort)(DBCharacter.Gender + 1375);
                    //case (int)eRace.Celt: return (ushort)(DBCharacter.Gender + 1377);
                    //case (int)eRace.Lurikeen: return (ushort)(DBCharacter.Gender + 1379);
                    //case (int)eRace.Elf: return (ushort)(DBCharacter.Gender + 1381);
                    //case (int)eRace.Sylvan: return (ushort)(DBCharacter.Gender + 1383);
                    //case (int)eRace.Shar: return (ushort)(DBCharacter.Gender + 1385);

                    default: return Model;
                }
            }
        }

        /// <summary>
        /// Changes shade state of the player.
        /// </summary>
        /// <param name="state">The new state.</param>
        public virtual void Shade(bool state)
        {
            CharacterClass.Shade(state);
        }

        #endregion Shade

        #region Guild

        private Guild _guild;

        public Guild Guild
        {
            get { return _guild; }
            set { _guild = value; }
        }

        #endregion Guild

        #region X/Y/Z/Region/Realm/Position...

        /// <summary>
        /// Holds all areas this player is currently within
        /// </summary>
        private ReaderWriterList<IArea> m_currentAreas = new ReaderWriterList<IArea>();

        /// <summary>
        /// Holds all areas this player is currently within
        /// </summary>
        public override IList<IArea> CurrentAreas
        {
            get { return m_currentAreas; }
            set { m_currentAreas.FreezeWhile(l => { l.Clear(); l.AddRange(value); }); }
        }

        /// <summary>
        /// Property that saves last maximum Z value
        /// </summary>
        public const string MAX_LAST_Z = "max_last_z";

        /// <summary>
        /// Gets or sets the current speed of this player
        /// </summary>
        public override short CurrentSpeed
        {
            set
            {
                base.CurrentSpeed = value;

                if (value != 0)
                    OnMimicMove();
            }
        }

        public void OnMimicMove()
        {
            if (IsSitting)
                Sit(false);
        }

        /// <summary>
        /// Holds the players max Z for fall damage
        /// </summary>
        private int m_maxLastZ;

        /// <summary>
        /// Gets or sets the players max Z for fall damage
        /// </summary>
        public int MaxLastZ
        {
            get { return m_maxLastZ; }
            set { m_maxLastZ = value; }
        }

        /// <summary>
        /// Gets or sets the realm of this player
        /// </summary>
        public override eRealm Realm
        {
            get { return base.Realm; }
            set { base.Realm = value; }
        }

        /// <summary>
        /// Gets or sets the heading of this player
        /// </summary>
        public override ushort Heading
        {
            set
            {
                base.Heading = value;

                if (attackComponent.AttackState && ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                {
                    AttackData ad = attackComponent.attackAction.LastAttackData;

                    if (ad != null && ad.IsMeleeAttack && (ad.AttackResult == eAttackResult.TargetNotVisible || ad.AttackResult == eAttackResult.OutOfRange))
                    {
                        if (ad.Target != null && IsObjectInFront(ad.Target, 120) && IsWithinRadius(ad.Target, attackComponent.AttackRange))
                            attackComponent.attackAction.OnEnterMeleeRange();
                    }
                }
            }
        }

        protected bool m_climbing;

        /// <summary>
        /// Gets/sets the current climbing state
        /// </summary>
        public bool IsClimbing
        {
            get { return m_climbing; }
            set
            {
                if (value == m_climbing) return;
                m_climbing = value;
            }
        }

        protected bool m_swimming;

        /// <summary>
        /// Gets/sets the current swimming state
        /// </summary>
        public virtual bool IsSwimming
        {
            get { return m_z <= CurrentZone.Waterlevel; }
            set
            {
                if (value == m_swimming)
                    return;

                m_swimming = value;
            }
        }

        protected long m_beginDrowningTick;
        protected eWaterBreath m_currentWaterBreathState;
        protected ECSGameTimer m_drowningTimer;
        protected ECSGameTimer m_holdBreathTimer;

        protected int DrowningTimerCallback(ECSGameTimer callingTimer)
        {
            if (!IsAlive)
                return 0;

            if (ObjectState != eObjectState.Active)
                return 0;

            if (GameLoop.GameLoopTime - m_beginDrowningTick > 15000)
            {
                TakeDamage(null, eDamageType.Natural, MaxHealth, 0);

                return 0;
            }
            else
                TakeDamage(null, eDamageType.Natural, MaxHealth / 20, 0);

            return 1000;
        }

        /// <summary>
        /// The diving state of this player
        /// </summary>
        protected bool m_diving;

        public bool IsDiving
        {
            get => m_diving;
            set
            {
                // Force the diving state instead of trusting the client.
                if (!value)
                    value = IsUnderwater;

                if (value && !CurrentZone.IsDivingEnabled && value && Client.Account.PrivLevel == 1)
                {
                    Z += 1;
                    Out.SendPlayerJump(false);
                    return;
                }

                if (m_diving == value)
                    return;

                m_diving = value;

                if (m_diving)
                {
                    if (!CanBreathUnderWater)
                        UpdateWaterBreathState(eWaterBreath.Holding);
                }
                else
                    UpdateWaterBreathState(eWaterBreath.None);
            }
        }

        protected bool m_canBreathUnderwater;
        public bool CanBreathUnderWater
        {
            get => m_canBreathUnderwater;
            set
            {
                if (m_canBreathUnderwater == value)
                    return;

                m_canBreathUnderwater = value;

                if (IsDiving)
                {
                    if (m_canBreathUnderwater)
                        UpdateWaterBreathState(eWaterBreath.None);
                    else
                        UpdateWaterBreathState(eWaterBreath.Holding);
                }
            }
        }

        public void UpdateWaterBreathState(eWaterBreath state)
        {
            if (Client.Account.PrivLevel != 1)
                return;

            switch (state)
            {
                case eWaterBreath.None:
                {
                    m_holdBreathTimer.Stop();
                    m_drowningTimer.Stop();
                    Out.SendCloseTimerWindow();
                    break;
                }
                case eWaterBreath.Holding:
                {

                    m_drowningTimer.Stop();

                    if (!m_holdBreathTimer.IsAlive)
                    {
                        Out.SendTimerWindow("Holding Breath", 30);
                        m_holdBreathTimer.Start(30000);
                    }

                    break;
                }
                case eWaterBreath.Drowning:
                {
                    if (m_holdBreathTimer.IsAlive)
                    {
                        m_holdBreathTimer.Stop();

                        if (!m_drowningTimer.IsAlive)
                        {
                            Out.SendTimerWindow("Drowning", 15);
                            m_beginDrowningTick = CurrentRegion.Time;
                            m_drowningTimer.Start(0);
                        }
                    }
                    else
                    {
                        // In case the player gets out of water right before the timer is stopped (and ticks instead).
                        UpdateWaterBreathState(eWaterBreath.None);
                        return;
                    }

                    break;
                }
            }

            m_currentWaterBreathState = state;
        }

        protected bool _sitting;

        public override bool IsSitting
        {
            get => _sitting;
            set
            {
                if (!_sitting && value)
                {
                    CurrentSpellHandler?.CasterMoves();

                    if (attackComponent.AttackState && ActiveWeaponSlot == eActiveWeaponSlot.Distance)
                        attackComponent.StopAttack();
                }

                _sitting = value;
            }
        }

        /// <summary>
        /// Gets or sets the max speed of this player
        /// (delegate to PlayerCharacter)
        /// </summary>
        public override short MaxSpeedBase
        {
            get { return base.MaxSpeedBase; }
            set { base.MaxSpeedBase = value; }
        }

        public override bool IsMoving => base.IsMoving || IsStrafing;

        private bool _isOnHorse = false;

        public ControlledHorse ActiveHorse
        {
            get { return null; }
        }

        public virtual bool IsOnHorse
        {
            get { return false; }
            set { _isOnHorse = value; }
        }

        public bool IsSprinting => effectListComponent.ContainsEffectForEffectType(eEffect.Sprint);

        public virtual bool Sprint(bool state)
        {
            if (state == IsSprinting)
                return state;

            if (state)
            {
                // Can't start sprinting with 10 endurance on 1.68 server.
                if (Endurance <= 10)
                    return false;

                if (IsStealthed)
                    return false;

                if (!IsAlive)
                    return false;

                new SprintECSGameEffect(new ECSGameEffectInitParams(this, 0, 1, null));

                return true;
            }
            else
            {
                if (effectListComponent.ContainsEffectForEffectType(eEffect.Sprint))
                    EffectService.RequestImmediateCancelEffect(EffectListService.GetEffectOnTarget(this, eEffect.Sprint), false);

                return false;
            }
        }

        protected bool m_strafing;

        public override bool IsStrafing
        {
            get { return false; }
            set { m_strafing = value; }
        }

        public virtual bool Sit(bool sit)
        {
            Sprint(false);

            if (IsSitting == sit)
                return sit;

            if (!IsAlive)
                return false;

            if (IsStunned)
                return false;

            if (IsMezzed)
                return false;

            if (sit && (CurrentSpeed > 0 || IsStrafing))
                return false;

            // Stop attacking if the player sits down.
            if (sit && attackComponent.AttackState)
                attackComponent.StopAttack();

            IsSitting = sit;

            if (sit)
            {
                Emote(eEmote.Drink);
                return true;
            }
            else
            {
                Emote(eEmote.LetsGo);

                return false;
            }
        }

        /// <summary>
        /// Sets the Living's ground-target Coordinates inside the current Region
        /// </summary>
        public override void SetGroundTarget(int groundX, int groundY, int groundZ)
        {
            ECSGameEffect volley = EffectListService.GetEffectOnTarget(this, eEffect.Volley);//volley check for gt
            if (volley != null)
            {
                //Out.SendMessage("You can't change ground target under volley effect!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            else
            {
                base.SetGroundTarget(groundX, groundY, groundZ);

                //Out.SendMessage(String.Format("You ground-target {0},{1},{2}", groundX, groundY, groundZ), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //if (SiegeWeapon != null)
                //    SiegeWeapon.SetGroundTarget(groundX, groundY, groundZ);
            }
        }

        /// <summary>
        /// Holds unique locations array
        /// </summary>
        protected readonly GameLocation[] m_lastUniqueLocations;

        /// <summary>
        /// Gets unique locations array
        /// </summary>
        public GameLocation[] LastUniqueLocations
        {
            get { return m_lastUniqueLocations; }
        }

        ///// <summary>
        ///// Updates Health, Mana, Sitting, Endurance, Concentration and Alive status to client
        ///// </summary>
        //public void UpdatePlayerStatus()
        //{
        //    Out.SendStatusUpdate();
        //}

        #endregion X/Y/Z/Region/Realm/Position...

        #region ControlledNpc

        /// <summary>
        /// Sets the controlled object for this player
        /// (delegates to CharacterClass)
        /// </summary>
        /// <param name="controlledNpc"></param>
        public override void SetControlledBrain(IControlledBrain controlledBrain)
        {
            if (controlledBrain == ControlledBrain)
                return;

            //if (controlledBrain.Owner != this)
            //throw new ArgumentException("ControlledNpc with wrong owner is set (mimic=" + Name + ", owner=" + controlledBrain.Owner.Name + ")", "controlledNpc");

            if (ControlledBrain == null)
                InitControlledBrainArray(1);

            ControlledBrain = controlledBrain;
        }

        /// <summary>
        /// Releases controlled object
        /// (delegates to CharacterClass)
        /// </summary>
        public virtual void CommandNpcRelease()
        {
            CharacterClass.CommandNpcRelease();
        }

        /// <summary>
        /// Commands controlled object to attack
        /// </summary>
        public virtual void CommandNpcAttack()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null || !GameServer.ServerRules.IsAllowedToAttack(this, TargetObject as GameLiving, false))
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!IsWithinRadius(TargetObject, 2000))
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.TooFarAwayForPet"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!TargetInView)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.Attack.CantSeeTarget"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.KillTarget", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.Attack(TargetObject);
        }

        /// <summary>
        /// Commands controlled object to follow
        /// </summary>
        public virtual void CommandNpcFollow()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.FollowYou", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.Disengage();
            npc.Follow(this);
        }

        /// <summary>
        /// Commands controlled object to stay where it is
        /// </summary>
        public virtual void CommandNpcStay()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.Stay", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.Disengage();
            npc.Stay();
        }

        /// <summary>
        /// Commands controlled object to go to players location
        /// </summary>
        public virtual void CommandNpcComeHere()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.ComeHere", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.Disengage();
            npc.ComeHere();
        }

        /// <summary>
        /// Commands controlled object to go to target
        /// </summary>
        public virtual void CommandNpcGoTarget()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            GameObject target = TargetObject;
            if (target == null)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcGoTarget.MustSelectDestination"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (GetDistance(new Point2D(target.X, target.Y)) > 1250)
            {
                //Out.SendMessage("Your target is too far away for your pet to reach!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.GoToTarget", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.Disengage();
            npc.Goto(target);
        }

        /// <summary>
        /// Changes controlled object state to passive
        /// </summary>
        public virtual void CommandNpcPassive()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.Passive", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.SetAggressionState(eAggressionState.Passive);
        }

        /// <summary>
        /// Changes controlled object state to aggressive
        /// </summary>
        public virtual void CommandNpcAgressive()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.Aggressive", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.SetAggressionState(eAggressionState.Aggressive);
        }

        /// <summary>
        /// Changes controlled object state to defensive
        /// </summary>
        public virtual void CommandNpcDefensive()
        {
            IControlledBrain npc = ControlledBrain;
            if (npc == null)
                return;

            if (npc.Body.IsConfused)
            {
                //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.IsConfused", npc.Body.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            //Out.SendMessage(LanguageMgr.GetTranslation(Client.Account.Language, "GamePlayer.CommandNpcAttack.Denfensive", npc.Body.GetName(0, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            npc.SetAggressionState(eAggressionState.Defensive);
        }

        #endregion ControlledNpc
    }
}