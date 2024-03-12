using DOL.Database;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.RealmAbilities;
using DOL.GS.Scripts;
using DOL.GS.ServerProperties;
using DOL.GS.SkillHandler;
using DOL.GS.Spells;
using DOL.Language;
using log4net;
using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using static DOL.AI.Brain.StandardMobBrain;
using static DOL.GS.Styles.Style;

namespace DOL.AI.Brain
{
    public class MimicBrain : ABrain, IOldAggressiveBrain
    {
        public static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public override bool IsActive => Body != null && Body.IsAlive && Body.ObjectState == GameObject.eObjectState.Active;

        public bool IsMainPuller
        { get { return Body.Group?.MimicGroup.MainPuller == Body; } }

        public bool IsMainTank
        { get { return Body.Group?.MimicGroup.MainTank == Body; } }

        public bool IsMainLeader
        { get { return Body.Group?.MimicGroup.MainLeader == Body; } }

        public bool IsMainCC
        { get { return Body.Group?.MimicGroup.MainCC == Body; } }

        public bool IsMainAssist
        { get { return Body.Group?.MimicGroup.MainAssist == Body; } }

        private MimicNPC _mimicBody;

        public MimicNPC MimicBody
        {
            get { return _mimicBody; }
            set { _mimicBody = value; }
        }

        public const int MAX_AGGRO_DISTANCE = 3600;
        public const int MAX_AGGRO_LIST_DISTANCE = 6000;
        private const int EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD = 500;

        public bool PreventCombat;
        public bool PvPMode;
        public bool Defend;
        public bool Roam;
        public bool IsFleeing;
        public bool IsPulling;

        public GameObject LastTargetObject;
        public bool IsFlanking;
        public Point2D TargetFlankPosition;

        public Point3D TargetFleePosition;

        // Used for AmbientBehaviour "Seeing" - maintains a list of GamePlayer in range
        public List<GamePlayer> PlayersSeen = new();

        /// <summary>
        /// Constructs a new MimicBrain
        /// </summary>
        public MimicBrain() : base()
        {
            FSM = new FSM();
            FSM.Add(new MimicState_Idle(this));
            FSM.Add(new MimicState_WakingUp(this));
            FSM.Add(new MimicState_Aggro(this));
            FSM.Add(new MimicState_ReturnToSpawn(this));
            FSM.Add(new MimicState_Patrolling(this));
            FSM.Add(new MimicState_Roaming(this));
            FSM.Add(new MimicState_FollowLeader(this));
            FSM.Add(new MimicState_Camp(this));
            FSM.Add(new MimicState_Duel(this));
            FSM.Add(new MimicState_Dead(this));

            FSM.SetCurrentState(eFSMStateType.WAKING_UP);
        }

        /// <summary>
        /// Returns the string representation of the MimicBrain
        /// </summary>
        public override string ToString()
        {
            return base.ToString() + ", AggroLevel=" + AggroLevel.ToString() + ", AggroRange=" + AggroRange.ToString();
        }

        public override bool Stop()
        {
            // tolakram - when the brain stops, due to either death or no players in the vicinity, clear the aggro list
            if (base.Stop())
            {
                ClearAggroList();
                return true;
            }

            return false;
        }

        public override void KillFSM()
        {
            FSM.KillFSM();
        }

        #region AI

        public override void Think()
        {
            FSM.Think();
        }

        public void OnGroupMemberAttacked(AttackData ad)
        {
            if (FSM.GetState(eFSMStateType.CAMP) == FSM.GetCurrentState())
            {
                if (!Body.IsWithinRadius(ad.Attacker, AggroRange))
                    return;
            }

            switch (ad.AttackResult)
            {
                case eAttackResult.Blocked:
                case eAttackResult.Evaded:
                case eAttackResult.Fumbled:
                case eAttackResult.HitStyle:
                case eAttackResult.HitUnstyled:
                case eAttackResult.Missed:
                case eAttackResult.Parried:
                AddToAggroList(ad.Attacker, ad.Attacker.EffectiveLevel + ad.Damage + ad.CriticalDamage);
                break;
            }

            if (FSM.GetState(eFSMStateType.AGGRO) != FSM.GetCurrentState())
                FSM.SetCurrentState(eFSMStateType.AGGRO);
        }

        public virtual bool CheckProximityAggro(int aggroRange)
        {
            FireAmbientSentence();

            //Check aggro only if our aggro list is empty and we're not in combat.
            if (AggroLevel > 0 && aggroRange > 0 && !HasAggro && !Body.AttackState && Body.CurrentSpellHandler == null)
            {
                CheckPlayerAggro();
                CheckNPCAggro(aggroRange);
            }

            // Some calls rely on this method to return if there's something in the aggro list, not necessarily to perform a proximity aggro check.
            // But this doesn't necessarily return whether or not the check was positive, only the current state (LoS checks take time).
            return HasAggro;
        }

        public virtual bool IsBeyondTetherRange()
        {
            if (Body.MaxDistance != 0)
            {
                int distance = Body.GetDistanceTo(Body.SpawnPoint);
                int maxDistance = Body.MaxDistance > 0 ? Body.MaxDistance : -Body.MaxDistance * AggroRange / 100;
                return maxDistance > 0 && distance > maxDistance;
            }
            else
                return false;
        }

        public virtual bool HasPatrolPath()
        {
            return Body.MaxSpeedBase > 0 &&
                Body.CurrentSpellHandler == null &&
                !Body.IsMoving &&
                !Body.attackComponent.AttackState &&
                !Body.InCombat &&
                !Body.IsMovingOnPath &&
                Body.PathID != null &&
                Body.PathID != "" &&
                Body.PathID != "NULL";
        }

        /// <summary>
        /// Check for aggro against players
        /// </summary>
        protected virtual void CheckPlayerAggro()
        {
            foreach (GamePlayer player in Body.GetPlayersInRadius((ushort)AggroRange))
            {
                if (!CanAggroTarget(player))
                    continue;

                if (player.IsStealthed || player.Steed != null)
                    continue;

                if (player.effectListComponent.ContainsEffectForEffectType(eEffect.Shade))
                    continue;

                if (Properties.ALWAYS_CHECK_LOS)
                    // We don't know if the LoS check will be positive, so we have to ask other players
                    player.Out.SendCheckLos(Body, player, new CheckLosResponse(LosCheckForAggroCallback));
                else
                {
                    AddToAggroList(player, 1);
                    return;
                }
            }
        }

        /// <summary>
        /// Check for aggro against close NPCs
        /// </summary>
        protected virtual void CheckNPCAggro(int aggroRange)
        {
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)aggroRange))
            {
                if (!CanAggroTarget(npc))
                    continue;

                if (npc is GameTaxi or GameTrainingDummy)
                    continue;

                if (Properties.ALWAYS_CHECK_LOS)
                {
                    // Check LoS if either the target or the current mob is a pet
                    if (npc.Brain is ControlledNpcBrain theirControlledNpcBrain && theirControlledNpcBrain.GetPlayerOwner() is GamePlayer theirOwner)
                    {
                        theirOwner.Out.SendCheckLos(Body, npc, new CheckLosResponse(LosCheckForAggroCallback));
                        continue;
                    }
                }

                AddToAggroList(npc, 1);

                return;
            }
        }

        public virtual void FireAmbientSentence()
        {
            if (Body.ambientTexts != null && Body.ambientTexts.Any(item => item.Trigger == "seeing"))
            {
                // Check if we can "see" players and fire off ambient text
                List<GamePlayer> currentPlayersSeen = new();

                foreach (GamePlayer player in Body.GetPlayersInRadius((ushort)AggroRange))
                {
                    if (!PlayersSeen.Contains(player))
                    {
                        Body.FireAmbientSentence(GameNPC.eAmbientTrigger.seeing, player);
                        PlayersSeen.Add(player);
                    }

                    currentPlayersSeen.Add(player);
                }

                for (int i = 0; i < PlayersSeen.Count; i++)
                {
                    if (!currentPlayersSeen.Contains(PlayersSeen[i]))
                        PlayersSeen.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// The interval for thinking, min 1.5 seconds
        /// 10 seconds for 0 aggro mobs
        /// </summary>
        public override int ThinkInterval
        {
            get
            {
                return 500;
            }
        }

        /// <summary>
        /// If this brain is part of a formation, it edits it's values accordingly.
        /// </summary>
        /// <param name="x">The x-coordinate to refer to and change</param>
        /// <param name="y">The x-coordinate to refer to and change</param>
        /// <param name="z">The x-coordinate to refer to and change</param>
        public virtual bool CheckFormation(ref int x, ref int y, ref int z)
        {
            return false;
        }

        /// <summary>
        /// Checks the Abilities
        /// </summary>
        public virtual void CheckDefensiveAbilities()
        {
            if (Body.Abilities == null || Body.Abilities.Count <= 0)
                return;

            foreach (Ability ab in Body.Abilities.Values)
            {
                switch (ab.KeyName)
                {
                    case Abilities.Intercept:
                    {
                        //if (Body.Group != null)
                        //{
                        //    GameLiving interceptTarget;
                        //    List<GameLiving> interceptTargets = new List<GameLiving>();

                        //    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        //    {
                        //        if (groupMember is MimicNPC mimic)
                        //        {
                        //            if (mimic.CharacterClass.ID == (int)eCharacterClass.Cleric ||
                        //                mimic.CharacterClass.ID == (int)eCharacterClass.Druid ||
                        //                mimic.CharacterClass.ID == (int)eCharacterClass.Healer ||
                        //                mimic.CharacterClass.ID == (int)eCharacterClass.Friar ||
                        //                mimic.CharacterClass.ID == (int)eCharacterClass.Bard ||
                        //                mimic.CharacterClass.ID == (int)eCharacterClass.Shaman)
                        //            {
                        //                interceptTargets.Add(groupMember);
                        //            }
                        //        }
                        //    }
                        //}
                        break;
                    }
                    case Abilities.Guard:
                    {
                        break;
                    }
                    case Abilities.Protect:
                    {
                        break;
                    }
                }
            }
        }

        public void CheckOffensiveAbilities()
        {
            if (Body.Abilities == null || Body.Abilities.Count <= 0)
                return;

            if (CanUseAbility())
            {
                foreach (Ability ab in Body.GetAllAbilities())
                {
                    if (Body.GetSkillDisabledDuration(ab) == 0)
                    {
                        switch (ab.KeyName)
                        {
                            case Abilities.Berserk:
                            {
                                if (Body.TargetObject is GameLiving target)
                                {
                                    if (Body.IsWithinRadius(Body.TargetObject, Body.AttackRange) &&
                                        GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                                    {
                                        new BerserkECSGameEffect(new ECSGameEffectInitParams(Body, 20000, 1));
                                        Body.DisableSkill(ab, 420000);
                                    }
                                }

                                break;
                            }

                            case Abilities.Stag:
                            {
                                if (Body.TargetObject is GameLiving target)
                                {
                                    if (Body.IsWithinRadius(Body.TargetObject, Body.AttackRange) &&
                                        GameServer.ServerRules.IsAllowedToAttack(Body, target, true) || Body.HealthPercent < 75)
                                    {
                                        new StagECSGameEffect(new ECSGameEffectInitParams(Body, 30000, 1), ab.Level);
                                        Body.DisableSkill(ab, 900000);
                                    }
                                }

                                break;
                            }

                            case Abilities.Triple_Wield:
                            {
                                if (Body.TargetObject is GameLiving target)
                                {
                                    if (Body.IsWithinRadius(Body.TargetObject, Body.AttackRange) &&
                                        GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                                    {
                                        new TripleWieldECSGameEffect(new ECSGameEffectInitParams(Body, 30000, 1));
                                        Body.DisableSkill(ab, 420000);
                                    }
                                }

                                break;
                            }

                            case Abilities.DirtyTricks:
                            {
                                if (Body.TargetObject is GameLiving target)
                                {
                                    IGamePlayer gamePlayer = target as IGamePlayer;

                                    if (gamePlayer != null && gamePlayer.CharacterClass.ClassType == eClassType.ListCaster)
                                        break;

                                    if (Body.IsWithinRadius(Body.TargetObject, Body.AttackRange) &&
                                        GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                                    {
                                        new DirtyTricksECSGameEffect(new ECSGameEffectInitParams(Body, 30000, 1));
                                        Body.DisableSkill(ab, 420000);
                                    }
                                }

                                break;
                            }

                            case Abilities.ChargeAbility:
                            {
                                if (Body.TargetObject is GameLiving target &&
                                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true) &&
                                    !Body.IsWithinRadius(target, 500))
                                {
                                    ChargeAbility charge = Body.GetAbility<ChargeAbility>();

                                    if (charge != null && Body.GetSkillDisabledDuration(charge) <= 0)
                                        charge.Execute(Body);
                                }

                                break;
                            }
                        }
                    }
                }
            }
        }

        private bool CanUseAbility()
        {
            if (!Body.IsAlive ||
                Body.IsMezzed ||
                Body.IsStunned ||
                Body.IsSitting)
                return false;

            return true;
        }

        #endregion AI

        #region MimicGroup AI

        #region MainPuller

        public void CheckPuller()
        {
            if (IsPulling && Body.TargetObject != null && Body.TargetObject.ObjectState == GameObject.eObjectState.Active)
            {
                if (CheckResetPuller())
                {
                    Body.ReturnToSpawnPoint(Body.MaxSpeed);

                    if ((MimicBody.CharacterClass.ID != (int)eCharacterClass.Hunter ||
                        MimicBody.CharacterClass.ID != (int)eCharacterClass.Ranger ||
                        MimicBody.CharacterClass.ID != (int)eCharacterClass.Scout) &&
                        MimicBody.CharacterClass.ClassType != eClassType.ListCaster)
                    {
                        if (MimicBody.MimicSpec.is2H)
                            Body.SwitchWeapon(eActiveWeaponSlot.TwoHanded);
                        else
                            Body.SwitchWeapon(eActiveWeaponSlot.Standard);
                    }

                    return;
                }
            }

            if (!Body.InCombat)
            {
                if (CheckDelayPull())
                {
                    Body.StopAttack();
                    Body.StopFollowing();
                }
                else
                {
                    GameLiving pullTarget = GetPullTarget();
                    PerformPull(pullTarget);
                }
            }
        }

        public bool CheckDelayPull()
        {
            if (LastTargetObject != null && LastTargetObject.ObjectState == GameObject.eObjectState.Active)
                return true;

            if (CheckSpells(eCheckSpellType.Defensive) || MimicBody.Sit(CheckStats(75)))
                return true;

            if (Body.Group != null &&
                Body.Group.GetMembersInTheGroup().Any(groupMember => groupMember.IsCasting || groupMember.IsSitting))
                return true;

            return false;
        }

        public GameLiving GetPullTarget()
        {
            if (!Body.IsAttacking && !Body.IsCasting && !Body.IsSitting)
            {
                if (Body.Group.MimicGroup.CCTargets.Count > 0)
                    return Body.Group.MimicGroup.CCTargets[Util.Random(Body.Group.MimicGroup.CCTargets.Count - 1)];

                CheckProximityAggro(3600);

                if (AggroList.Count > 0)
                {
                    GameLiving closestTarget;

                    if (Body.Group.MimicGroup.PullFromPoint != null)
                        closestTarget = AggroList.Where(pair => Body.GetConLevel(pair.Key) >= Body.Group.MimicGroup.ConLevelFilter).
                                                   OrderBy(pair => pair.Key.GetDistance(Body.Group.MimicGroup.PullFromPoint)).
                                                   ThenBy(pair => Body.GetDistanceTo(pair.Key)).First().Key;
                    else
                        closestTarget = AggroList.Where(pair => Body.GetConLevel(pair.Key) > Body.Group.MimicGroup.ConLevelFilter).
                                                   OrderBy(pair => Body.GetDistanceTo(pair.Key)).First().Key;

                    return closestTarget;
                }
            }

            return null;
        }

        private bool CheckResetPuller()
        {
            if (Body.TargetObject is GameNPC npcTarget && npcTarget.Brain is StandardMobBrain mobBrain && mobBrain.HasAggro)
            {
                LastTargetObject = Body.TargetObject;
                IsPulling = false;
                Body.StopAttack();
                ClearAggroList();

                return true;
            }

            return false;
        }

        public void PerformPull(GameLiving target)
        {
            if (target == null)
                return;

            IsPulling = true;

            if (Body.Inventory.GetItem(eInventorySlot.DistanceWeapon) != null)
            {
                Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                Body.StartAttack(target);
            }
            else
            {
                //if (Body.CanCastInstantHarmfulSpells)
                //{
                //    foreach(Spell spell in Body.InstantHarmfulSpells)

                //}
                //if (!Body.IsWithinRadius(Body.TargetObject, spell.Range))
                //{
                //    Body.Follow(Body.TargetObject, spell.Range - 100, 5000);
                //    QueuedOffensiveSpell = spell;
                //    return false;
                //}
            }
        }

        #endregion MainPuller

        #region MainLeader

        public bool CheckDelayRoam()
        {
            if (CheckSpells(eCheckSpellType.Defensive) || MimicBody.Sit(CheckStats(75)))
                return true;

            if (Body.Group != null &&
                Body.Group.GetMembersInTheGroup().Any(groupMember => groupMember.IsCasting || groupMember.IsSitting || (groupMember is MimicNPC mimic &&
                                                      mimic.MimicBrain.FSM.GetCurrentState() == mimic.MimicBrain.FSM.GetState(eFSMStateType.FOLLOW_THE_LEADER) &&
                                                      !Body.IsWithinRadius(groupMember, 1000))))
                return true;

            return false;
        }

        #endregion MainLeader

        #region MainCC

        public void CheckMainCC()
        {
            if (Body.Group.MimicGroup.CCTargets.Count > 0)
            {
                if (CheckSpells(eCheckSpellType.CrowdControl))
                    return;
            }

            if (!Body.InCombat && Body.Group.MimicGroup.CCTargets.Count > 0)
            {
                Body.Group.MimicGroup.CCTargets = ValidateCCList(Body.Group.MimicGroup.CCTargets);
            }
        }

        // Test for bad lists. Might not be needed.
        private List<GameLiving> ValidateCCList(List<GameLiving> ccList)
        {
            List<GameLiving> validatedList = new List<GameLiving>();

            if (ccList.Any())
            {
                foreach (GameLiving cc in ccList)
                {
                    if (cc is GameNPC npc && npc != null && npc.IsAlive && ((StandardMobBrain)npc.Brain).HasAggro)
                    {
                        validatedList.Add(cc);
                    }
                }
            }

            return validatedList;
        }

        #endregion MainCC

        #region MainTank

        public bool CheckMainTankTarget()
        {
            if (!IsMainTank)
                return false;

            GameLiving target = null;
            List<GameLiving> listOfTargets = null;

            if (AggroList.Count > 0)
            {
                listOfTargets = (AggroList.Keys.Where(key => key.TargetObject is GameLiving livingTarget && livingTarget != Body &&
                                                             !livingTarget.IsMezzed && !livingTarget.IsRooted).ToList());
            }

            if (listOfTargets != null && listOfTargets.Count > 0)
                target = listOfTargets[Util.Random(listOfTargets.Count - 1)];

            if (target != null)
            {
                Body.TargetObject = target;

                return true;
            }

            return false;
        }

        #endregion MainTank

        public bool CheckStats(short threshold)
        {
            if (Body.HealthPercent < threshold || (Body.MaxMana > 0 && Body.ManaPercent < threshold) || Body.EndurancePercent < threshold)
                return true;

            return false;
        }

        #endregion MimicGroup AI

        #region Aggro

        protected int _aggroRange;

        /// <summary>
        /// Max Aggro range in that this npc searches for enemies
        /// </summary>
        public virtual int AggroRange
        {
            get => Math.Min(_aggroRange, MAX_AGGRO_DISTANCE);
            set => _aggroRange = value;
        }

        /// <summary>
        /// Aggressive Level in % 0..100, 0 means not Aggressive
        /// </summary>
        public virtual int AggroLevel { get; set; }

        protected ConcurrentDictionary<GameLiving, AggroAmount> AggroList { get; private set; } = new();
        protected List<(GameLiving, long)> OrderedAggroList { get; private set; } = new();
        public GameLiving LastHighestThreatInAttackRange { get; private set; }

        public class AggroAmount
        {
            public long Base { get; set; }
            public long Effective { get; set; }

            public AggroAmount(long @base = 0)
            {
                Base = @base;
            }
        }

        /// <summary>
        /// Checks whether living has someone on its aggrolist
        /// </summary>
        public virtual bool HasAggro => !AggroList.IsEmpty;

        /// <summary>
        /// Add aggro table of this brain to that of another living.
        /// </summary>
        public void AddAggroListTo(StandardMobBrain brain)
        {
            if (!brain.Body.IsAlive)
                return;

            foreach (var pair in AggroList)
                brain.AddToAggroList(pair.Key, pair.Value.Base);
        }

        public virtual void AddToAggroList(GameLiving living, long aggroAmount)
        {
            if (Body.IsConfused || !Body.IsAlive || living == null)
                return;

            if (AggroList.IsEmpty)
                Body.FireAmbientSentence(GameNPC.eAmbientTrigger.aggroing, living);

            if (living is IGamePlayer player)
            {
                // Add the whole group to the aggro list.
                if (player.Group != null)
                {
                    foreach (GameLiving livingInGroup in player.Group.GetMembersInTheGroup())
                    {
                        if (livingInGroup is IGamePlayer iPlayer)
                            AggroList.TryAdd((GameLiving)iPlayer, new());
                    }
                }

                // Only protect if `aggroAmount` is positive.
                if (aggroAmount > 0)
                {
                    foreach (ProtectECSGameEffect protect in player.EffectListComponent.GetAbilityEffects().Where(e => e.EffectType == eEffect.Protect))
                    {
                        if (protect.Target != living)
                            continue;

                        IGamePlayer protectSource = (IGamePlayer)protect.Source;

                        if (protectSource.IsIncapacitated || protectSource.IsSitting)
                            continue;

                        if (!living.IsWithinRadius((GameLiving)protectSource, ProtectAbilityHandler.PROTECT_DISTANCE))
                            continue;

                        // P I: prevents 10% of aggro amount
                        // P II: prevents 20% of aggro amount
                        // P III: prevents 30% of aggro amount
                        // guessed percentages, should never be higher than or equal to 50%
                        int abilityLevel = protectSource.GetAbilityLevel(Abilities.Protect);
                        long protectAmount = (long)(abilityLevel * 0.1 * aggroAmount);


                        if (protectAmount > 0)
                        {
                            aggroAmount -= protectAmount;
                            protectSource.Out.SendMessage(LanguageMgr.GetTranslation(protectSource.Client.Account.Language, "AI.Brain.StandardMobBrain.YouProtDist", player.GetName(0, false),
                                                                                     Body.GetName(0, false, protectSource.Client.Account.Language, Body)), eChatType.CT_System, eChatLoc.CL_SystemWindow);

                            AggroList.AddOrUpdate((GameLiving)protectSource, Add, Update, protectAmount);
                        }
                    }
                }
            }

            AggroList.AddOrUpdate(living, Add, Update, aggroAmount);

            static AggroAmount Add(GameLiving key, long arg)
            {
                return new(Math.Max(0, arg));
            }

            static AggroAmount Update(GameLiving key, AggroAmount oldValue, long arg)
            {
                oldValue.Base = Math.Max(0, oldValue.Base + arg);
                return oldValue;
            }
        }

        public virtual void RemoveFromAggroList(GameLiving living)
        {
            AggroList.TryRemove(living, out _);
        }

        public List<(GameLiving, long)> GetOrderedAggroList()
        {
            // Potentially slow, so we cache the result.
            lock (((ICollection)OrderedAggroList).SyncRoot)
            {
                if (!OrderedAggroList.Any())
                    OrderedAggroList = AggroList.OrderByDescending(x => x.Value.Effective).Select(x => (x.Key, x.Value.Effective)).ToList();

                return OrderedAggroList.ToList();
            }
        }

        public long GetBaseAggroAmount(GameLiving living)
        {
            return AggroList.TryGetValue(living, out AggroAmount aggroAmount) ? aggroAmount.Base : 0;
        }

        /// <summary>
        /// Remove all livings from the aggrolist.
        /// </summary>
        public virtual void ClearAggroList()
        {
            AggroList.Clear();

            lock (((ICollection)OrderedAggroList).SyncRoot)
            {
                OrderedAggroList.Clear();
            }

            LastHighestThreatInAttackRange = null;
        }

        /// <summary>
        /// Selects and attacks the next target or does nothing.
        /// </summary>
        public virtual void AttackMostWanted()
        {
            if (!IsActive)
                return;

            //if (PvPMode || CheckAssist == null)

            if (!CheckMainTankTarget())
                Body.TargetObject = CalculateNextAttackTarget();

            if (Body.TargetObject != null)
            {
                if (!IsFleeing && CheckSpells(eCheckSpellType.Offensive))
                {
                    Body.StopAttack();
                }
                else
                {
                    CheckOffensiveAbilities();

                    if (Body.ControlledBrain != null)
                        Body.ControlledBrain.Attack(Body.TargetObject);

                    if (MimicBody.CharacterClass.ClassType == eClassType.ListCaster)
                    {
                        ECSGameAbilityEffect quickCast = EffectListService.GetAbilityEffectOnTarget(Body, eEffect.QuickCast);

                        if (quickCast != null)
                        {
                            CheckSpells(eCheckSpellType.Offensive);
                            return;
                        }

                        // Don't flee if in a group for now. Need better control over when and where and how.
                        if (Body.Group == null)
                        {
                            if ((TargetFleePosition == null && !IsFleeing && Body.IsBeingInterrupted && quickCast == null))
                            {
                                //TODO: Get dynamic distances based on circumstances. Maybe rethink the whole thing.
                                int fleeDistance = 2000 - Body.GetDistance(Body.TargetObject);

                                Flee(fleeDistance);

                                return;
                            }

                            if (Body.IsDestinationValid)
                                return;
                            else if (TargetFleePosition != null)
                            {
                                if (Body.GetDistance(TargetFleePosition) < 5)
                                {
                                    IsFleeing = false;
                                    TargetFleePosition = null;

                                    if (Body.IsWithinRadius(Body.TargetObject, 500))
                                    {
                                        Flee(1800);
                                        return;
                                    }

                                    if (Body.TargetObject != Body)
                                        Body.TurnTo(Body.TargetObject);
                                }
                            }
                        }

                        return;
                    }

                    if (Body.TargetObject != LastTargetObject)
                        ResetFlanking();

                    if (Body.ActiveWeapon?.Item_Type != (int)eInventorySlot.DistanceWeapon && Body.IsWithinRadius(Body.TargetObject, Body.attackComponent.AttackRange))
                    {
                        if (MimicBody.CanUsePositionalStyles && !IsMainTank && Body.ActiveWeapon != null)
                        {
                            if (Body.TargetObject is GameLiving livingTarget)
                            {
                                if (livingTarget.IsMoving || livingTarget.TargetObject == Body)
                                    ResetFlanking();

                                if (TargetFlankPosition == null && !IsFlanking && !livingTarget.IsMoving && livingTarget.TargetObject != Body)
                                {
                                    LastTargetObject = Body.TargetObject;
                                    TargetFlankPosition = GetStylePositionPoint(livingTarget, GetPositional());
                                    Body.StopFollowing();
                                    Body.StopAttack();
                                    Body.WalkTo(new Point3D(TargetFlankPosition.X, TargetFlankPosition.Y, livingTarget.Z), Body.MaxSpeed);
                                    return;
                                }

                                if (Body.IsDestinationValid)
                                {
                                    if (TargetFlankPosition == null)
                                        Body.Follow(Body.TargetObject, 75, 5000);
                                    else
                                        return;
                                }
                                else if (TargetFlankPosition != null)
                                {
                                    if (Body.GetDistance(TargetFlankPosition) < 5)
                                    {
                                        IsFlanking = true;
                                        TargetFlankPosition = null;
                                    }
                                }
                            }
                        }
                    }

                    if ((MimicBody.CharacterClass.ID == (int)eCharacterClass.Minstrel ||
                        (MimicBody.CharacterClass.ID == (int)eCharacterClass.Bard && Body.Group == null)) &&
                        Body.ActiveWeaponSlot != eActiveWeaponSlot.Standard)
                        Body.SwitchWeapon(eActiveWeaponSlot.Standard);

                    Body.StartAttack(Body.TargetObject);

                    LastTargetObject = Body.TargetObject;
                }
            }
        }

        private void Flee(int distance)
        {
            IsFleeing = true;
            MimicBody.Sprint(true);
            TargetFleePosition = GetFleePoint(distance);
            Body.PathTo(TargetFleePosition, Body.MaxSpeed);
        }

        public void ResetFlanking()
        {
            IsFlanking = false;
            TargetFlankPosition = null;
        }

        private GameObject CheckAssist()
        {
            if (Body.Group != null && Body.Group.MimicGroup.MainAssist.InCombat)
            {
                GameObject assistTarget = Body.Group.MimicGroup.CurrentTarget;
                GameObject target = null;

                if (assistTarget != null && CanAggroTarget((GameLiving)assistTarget))
                    target = assistTarget;

                return target;
            }

            return null;

            //if (Body.Group != null)
            //{
            //    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
            //    {
            //        if (groupMember is GameLiving living)
            //            foreach (var attacker in living.attackComponent.Attackers)
            //                AddToAggroList(attacker.Key, 1);
            //    }
            //}
        }

        private Point3D GetFleePoint(int fleeDistance)
        {
            float diffx = (long)Body.TargetObject.X - Body.X;
            float diffy = (long)Body.TargetObject.Y - Body.Y;

            float distance = (float)Math.Sqrt(diffx * diffx + diffy * diffy);

            diffx = (diffx / distance) * fleeDistance;
            diffy = (diffy / distance) * fleeDistance;

            int newX = (int)(Body.TargetObject.X - diffx);
            int newY = (int)(Body.TargetObject.Y - diffy);

            Vector3? target = PathingMgr.Instance.GetClosestPointAsync(Body.CurrentZone, new Vector3(newX, newY, 0));

            return new Point3D((int)target?.X, (int)target?.Y, (int)target?.Z);
        }

        private eOpeningPosition GetPositional()
        {
            eOpeningPosition positional = 0;

            if (MimicBody.CanUseSideStyles && MimicBody.CanUseBackStyles)
            {
                if (Util.RandomBool())
                    positional = eOpeningPosition.Back;
                else
                    positional = eOpeningPosition.Side;
            }
            else if (MimicBody.CanUseSideStyles)
                positional = eOpeningPosition.Side;
            else if (MimicBody.CanUseBackStyles)
                positional = eOpeningPosition.Back;

            return positional;
        }

        private Point2D GetStylePositionPoint(GameLiving living, eOpeningPosition positional)
        {
            ushort heading = 0;

            switch (positional)
            {
                case eOpeningPosition.Side:
                if (Util.RandomBool())
                    heading = (ushort)(living.Heading - 1024);
                else
                    heading = (ushort)(living.Heading + 1024);
                break;

                case eOpeningPosition.Back:
                heading = (ushort)(living.Heading - 2048);
                break;

                case eOpeningPosition.Front:
                heading = living.Heading;
                break;
            }

            if (heading < 0)
                heading += 4096;

            if (heading > 4096)
                heading -= 4096;

            Point2D point = living.GetPointFromHeading(heading, 75);

            return point;
        }

        private long _isHandlingAdditionToAggroListFromLosCheck;
        private bool StartAddToAggroListFromLosCheck => Interlocked.Exchange(ref _isHandlingAdditionToAggroListFromLosCheck, 1) == 0; // Returns true the first time it's called.


        protected virtual void LosCheckForAggroCallback(GamePlayer player, eLosCheckResponse response, ushort sourceOID, ushort targetOID)
        {
            // Make sure only one thread can enter this block to prevent multiple entities from being added to the aggro list.
            // Otherwise mobs could kill one player and immediately go for another one.
            if (response is eLosCheckResponse.TRUE && StartAddToAggroListFromLosCheck)
            {
                if (!HasAggro)
                {
                    GameObject gameObject = Body.CurrentRegion.GetObject(targetOID);

                    if (gameObject is GameLiving gameLiving)
                        AddToAggroList(gameLiving, 1);
                }

                _isHandlingAdditionToAggroListFromLosCheck = 0;
            }
        }

        protected virtual bool ShouldBeRemovedFromAggroList(GameLiving living)
        {
            // Keep Necromancer shades so that we can attack them if their pets die.
            return !living.IsAlive ||
                   living.ObjectState != GameObject.eObjectState.Active ||
                   living.IsStealthed ||
                   living.CurrentRegion != Body.CurrentRegion ||
                   !Body.IsWithinRadius(living, MAX_AGGRO_LIST_DISTANCE) ||
                   (!GameServer.ServerRules.IsAllowedToAttack(Body, living, true) && !living.effectListComponent.ContainsEffectForEffectType(eEffect.Shade));

        }

        protected virtual bool ShouldBeIgnoredFromAggroList(GameLiving living)
        {
            // We're keeping shades in the aggro list so that mobs attack them after their pet dies, so they need to be filtered out here.
            return living.effectListComponent.ContainsEffectForEffectType(eEffect.Shade);
        }

        protected virtual GameLiving CleanUpAggroListAndGetHighestModifiedThreat()

        {
            // Clear cached ordered aggro list.
            // It isn't built here because ordering all entities in the aggro list can be expensive, and we typically don't need it.
            // It's built on demand, when `GetOrderedAggroList` is called.
            OrderedAggroList.Clear();

            int attackRange = Body.AttackRange;
            GameLiving highestThreat = null;
            long highestEffectiveAggro = -1; // Assumes that negative aggro amounts aren't allowed in the list.
            long highestEffectiveAggroInAttackRange = -1; // Assumes that negative aggro amounts aren't allowed in the list.

            foreach (var pair in AggroList)
            {
                GameLiving living = pair.Key;

                if (ShouldBeRemovedFromAggroList(living))
                {
                    AggroList.TryRemove(living, out _);
                    continue;
                }

                if (ShouldBeIgnoredFromAggroList(living))
                    continue;

                // Livings further than `EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD` units away have a reduced effective aggro amount.
                AggroAmount aggroAmount = pair.Value;
                double distance = Body.GetDistanceTo(living);
                aggroAmount.Effective = distance > EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD ?
                                        (long)Math.Floor(aggroAmount.Base * (EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD / distance)) :
                                        aggroAmount.Base;

                if (aggroAmount.Effective > highestEffectiveAggroInAttackRange)
                {
                    if (distance <= attackRange)
                    {
                        highestEffectiveAggroInAttackRange = aggroAmount.Effective;
                        LastHighestThreatInAttackRange = living;
                    }

                    if (aggroAmount.Effective > highestEffectiveAggro)
                    {
                        highestEffectiveAggro = aggroAmount.Effective;
                        highestThreat = living;
                    }
                }
            }

            if (highestThreat == null)
            {
                // The list seems to be full of shades. It could mean we added a shade to the aggro list instead of its pet.
                // Ideally, this should never happen, but it currently can be caused by the way `AddToAggroList` propagates aggro to group members.
                // When that happens, don't bother checking aggro amount and simply return the first pet in the list.
                return AggroList.FirstOrDefault().Key?.ControlledBrain?.Body;
            }

            return highestThreat;
        }

        /// <summary>
        /// Returns the best target to attack from the current aggro list.
        /// </summary>
        protected virtual GameLiving CalculateNextAttackTarget()
        {
            return CleanUpAggroListAndGetHighestModifiedThreat();
        }

        public virtual bool CanAggroTarget(GameLiving target)
        {
            if (!GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                return false;

            // Get owner if target is pet or subpet
            GameLiving realTarget = target;

            if (realTarget as GameNPC != null && ((GameNPC)realTarget).Brain is IControlledBrain npcTargetBrain)
                realTarget = npcTargetBrain.GetLivingOwner();

            if (realTarget == null)
                return false;

            // Only attack if green+ to target
            if (realTarget.IsObjectGreyCon(Body))
                return false;

            if (realTarget is IGamePlayer && realTarget.Realm != Body.Realm)
                return true;

            if (realTarget is GameKeepGuard && realTarget.Realm != Body.Realm)
                return true;

            if (realTarget is GameNPC && realTarget is not MimicNPC && realTarget is not GameKeepGuard && PvPMode)
                return false;

            // We put this here to prevent aggroing non-factions npcs
            return (Body.Realm != eRealm.None || realTarget is not GameNPC) && AggroLevel > 0;
        }

        protected virtual void OnFollowLostTarget(GameObject target)
        {
            AttackMostWanted();

            if (!Body.attackComponent.AttackState)
                Body.ReturnToSpawnPoint(NpcMovementComponent.DEFAULT_WALK_SPEED);
        }

        public virtual void OnAttackedByEnemy(AttackData ad)
        {
            if (!Body.IsAlive || Body.ObjectState != GameObject.eObjectState.Active)
                return;

            if (FSM.GetCurrentState() == FSM.GetState(eFSMStateType.PASSIVE))
                return;

            ConvertDamageToAggroAmount(ad.Attacker, Math.Max(1, ad.Damage + ad.CriticalDamage));

            if (!Body.attackComponent.AttackState && FSM.GetCurrentState() != FSM.GetState(eFSMStateType.AGGRO))
            {
                FSM.SetCurrentState(eFSMStateType.AGGRO);
                Think();
            }
        }

        /// <summary>
        /// Converts a damage amount into an aggro amount, and splits it between the pet and its owner if necessary.
        /// Assumes damage to be superior than 0.
        /// </summary>
        protected virtual void ConvertDamageToAggroAmount(GameLiving attacker, int damage)
        {
            if (attacker is GameNPC NpcAttacker && NpcAttacker.Brain is ControlledNpcBrain controlledBrain)
            {
                damage = controlledBrain.ModifyDamageWithTaunt(damage);

                // Aggro is split between the owner (15%) and their pet (85%).
                int aggroForOwner = (int)(damage * 0.15);

                // We must ensure that the same amount of aggro isn't added for both, otherwise an out-of-combat mob could attack the owner when their pet engages it.
                // The owner must also always generate at least 1 aggro.
                // This works as long as the split isn't 50 / 50.
                if (aggroForOwner == 0)
                {
                    AddToAggroList(controlledBrain.Owner, 1);
                    AddToAggroList(NpcAttacker, Math.Max(2, damage));
                }
                else
                {
                    AddToAggroList(controlledBrain.Owner, aggroForOwner);
                    AddToAggroList(NpcAttacker, damage - aggroForOwner);
                }
            }
            else
                AddToAggroList(attacker, damage);
        }

        #endregion Aggro

        #region Spells

        public enum eCheckSpellType
        {
            Offensive,
            Defensive,
            CrowdControl
        }

        /// <summary>
        /// Checks if any spells need casting
        /// </summary>
        /// <param name="type">Which type should we go through and check for?</param>
        public virtual bool CheckSpells(eCheckSpellType type)
        {
            if (Body == null || Body.Spells == null || Body.Spells.Count <= 0)
                return false;

            bool casted = false;
            List<Spell> spellsToCast = new();

            // Healers should heal whether in combat or out of it.
            if (!casted && Body.CanCastHealSpells)
            {
                GameLiving livingToHeal = null;

                int healThreshold = Properties.NPC_HEAL_THRESHOLD;
                int emergencyThreshold = healThreshold / 2;

                short numNeedHealing = 0;
                bool singleEmergency = false;
                bool groupEmergency = false;

                if (Body.Group != null)
                {
                    short healthPercent = 100;
                    short numEmergency = 0;

                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (groupMember.HealthPercent < healThreshold)
                        {
                            if (groupMember.HealthPercent < healthPercent)
                            {
                                healthPercent = groupMember.HealthPercent;
                                livingToHeal = groupMember;
                                numNeedHealing++;

                                if (groupMember.HealthPercent < emergencyThreshold)
                                    numEmergency++;
                            }
                        }
                    }

                    if (numEmergency == 1)
                        singleEmergency = true;
                    else if (numEmergency > Body.Group.GetMembersInTheGroup().Count / 2)
                        groupEmergency = true;
                }
                else
                {
                    if (Body.HealthPercent < healThreshold)
                    {
                        if (Body.HealthPercent < emergencyThreshold)
                            singleEmergency = true;

                        livingToHeal = Body;
                    }
                }

                Spell spellTocast;

                if ((singleEmergency || groupEmergency) && Body.CanCastInstantHealSpells)
                    spellTocast = Body.InstantHealSpells[Util.Random(Body.InstantHealSpells.Count - 1)];
                else
                {
                    Spell cureDisease = null;

                    if (livingToHeal != null && livingToHeal.IsDiseased && (cureDisease = Body.HealSpells.FirstOrDefault(spell => spell.SpellType == eSpellType.CureDisease)) != null)
                    {
                        spellTocast = cureDisease;
                    }
                    else
                        spellTocast = Body.HealSpells[Util.Random(Body.HealSpells.Count - 1)];
                }

                casted = CheckHealSpells(spellTocast, numNeedHealing, singleEmergency, groupEmergency, livingToHeal);
            }

            if (!casted && type == eCheckSpellType.CrowdControl)
            {
                if (MimicBody.CanCastCrowdControlSpells)
                {
                    Body.TargetObject = MimicBody.Group.MimicGroup.CCTargets[Util.Random(MimicBody.Group.MimicGroup.CCTargets.Count - 1)];

                    foreach (Spell spell in MimicBody.CrowdControlSpells)
                    {
                        if (CanCastOffensiveSpell(spell) && !LivingHasEffect((GameLiving)Body.TargetObject, spell))
                            spellsToCast.Add(spell);
                    }

                    if (spellsToCast.Count > 0)
                    {
                        Spell spell = spellsToCast[Util.Random(spellsToCast.Count - 1)];

                        casted = Body.CastSpell(spell, m_mobSpellLine);

                        if (casted)
                        {
                            MimicBody.Group.MimicGroup.CCTargets.Remove((GameLiving)Body.TargetObject);

                            if (spell.CastTime > 0)
                                Body.StopFollowing();
                            else if (Body.FollowTarget != Body.TargetObject)
                            {
                                Body.Follow(Body.TargetObject, spell.Range - 10, 5000);
                            }
                        }
                    }
                }
            }
            else if (!casted && type == eCheckSpellType.Defensive)
            {
                if (Body.CanCastMiscSpells)
                {
                    foreach (Spell spell in Body.MiscSpells)
                    {
                        if (CheckDefensiveSpells(spell))
                        {
                            casted = true;
                            break;
                        }
                    }
                }
            }
            else if (!casted && type == eCheckSpellType.Offensive)
            {
                if (MimicBody.CharacterClass.ID == (int)eCharacterClass.Cleric)
                {
                    if (!Util.Chance(Math.Max(5, Body.ManaPercent - 50)))
                        return false;
                }

                // Check instant spells, but only cast one to prevent spamming
                if (Body.CanCastInstantHarmfulSpells)
                {
                    foreach (Spell spell in Body.InstantHarmfulSpells)
                    {
                        if (CheckInstantOffensiveSpells(spell))
                            break;
                    }
                }

                if (Body.CanCastInstantMiscSpells)
                {
                    foreach (Spell spell in Body.InstantMiscSpells)
                    {
                        if (CheckInstantDefensiveSpells(spell))
                            break;
                    }
                }

                // TODO: Better nightshade casting logic. For now just make them melee but still use instants.
                if (MimicBody.CharacterClass.ID == (int)eCharacterClass.Nightshade)
                    return false;

                // TODO: This makes Thane and Valewalker use melee when in range rather than cast in all situations.
                //        but still use instants. Need to include other exceptions like maybe low health or endurance.
                if ((MimicBody.CanUsePositionalStyles || MimicBody.CanUseAnytimeStyles) && Body.IsWithinRadius(Body.TargetObject, 550))
                    return false;

                if (MimicBody.CanCastCrowdControlSpells)
                {
                    int ccChance = 50;

                    GameLiving livingTarget = Body.TargetObject as GameLiving;

                    if (livingTarget?.TargetObject == Body && Body.IsWithinRadius(Body.TargetObject, 500))
                        ccChance = 95;

                    if (Body.Group?.MimicGroup.CurrentTarget == Body.TargetObject)
                        ccChance = 0;

                    if (Util.Chance(ccChance))
                    {
                        foreach (Spell spell in MimicBody.CrowdControlSpells)
                        {
                            if (CanCastOffensiveSpell(spell) && !LivingHasEffect((GameLiving)Body.TargetObject, spell))
                                spellsToCast.Add(spell);
                        }
                    }
                }

                if (MimicBody.CanCastBolts && spellsToCast.Count < 1)
                {
                    foreach (Spell spell in MimicBody.BoltSpells)
                    {
                        if (CanCastOffensiveSpell(spell))
                            spellsToCast.Add(spell);
                    }
                }

                if (spellsToCast.Count < 1)
                {
                    foreach (Spell spell in Body.Spells)
                    {
                        if (spell.SpellType == eSpellType.Charm ||
                            spell.SpellType == eSpellType.Amnesia ||
                            spell.SpellType == eSpellType.Confusion ||
                            spell.SpellType == eSpellType.Taunt)
                            continue;

                        if (CanCastOffensiveSpell(spell))
                            spellsToCast.Add(spell);
                    }
                }

                if (spellsToCast.Count > 0)
                {
                    Spell spellToCast = spellsToCast[Util.Random(spellsToCast.Count - 1)];

                    if (spellToCast.Uninterruptible || !Body.IsBeingInterrupted)
                        casted = CheckOffensiveSpells(spellToCast);
                    else if (!spellToCast.Uninterruptible && Body.IsBeingInterrupted)
                    {
                        if (MimicBody.CharacterClass.ClassType == eClassType.ListCaster)
                        {
                            Ability quickCast = Body.GetAbility(Abilities.Quickcast);

                            if (quickCast != null)
                            {
                                if (Body.GetSkillDisabledDuration(quickCast) <= 0)
                                {
                                    // Give mimics a small bump in duration, they don't use it as well as humans.
                                    new QuickCastECSGameEffect(new ECSGameEffectInitParams(Body, QuickCastECSGameEffect.DURATION + 1000, 1));
                                    Body.DisableSkill(quickCast, 180000);

                                    casted = CheckOffensiveSpells(spellToCast);
                                }
                            }
                        }
                    }
                }
            }

            return casted || Body.IsCasting;
        }

        protected bool CanCastOffensiveSpell(Spell spell)
        {
            if (Body.GetSkillDisabledDuration(spell) <= 0)
            {
                if (spell.CastTime > 0)
                {
                    if (spell.Target is eSpellTarget.ENEMY or eSpellTarget.AREA or eSpellTarget.CONE)
                        return true;
                }
            }

            return false;
        }

        protected bool CanCastDefensiveSpell(Spell spell)
        {
            if (spell == null || spell.IsHarmful)
                return false;

            // Make sure we're currently able to cast the spell.
            if (spell.CastTime > 0 && Body.IsBeingInterrupted && !spell.Uninterruptible)
                return false;

            // Make sure the spell isn't disabled.
            if (Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            return true;
        }

        protected bool CheckHealSpells(Spell spell, short numNeedHealing, bool singleEmergency, bool groupEmergency, GameLiving livingToHeal)
        {
            if (!CanCastDefensiveSpell(spell))
                return false;

            GameObject lastTarget = Body.TargetObject;
            Body.TargetObject = null;

            if (livingToHeal != null)
            {
                switch (spell.SpellType)
                {
                    case eSpellType.CureDisease:
                    case eSpellType.CombatHeal:
                    case eSpellType.Heal:
                    case eSpellType.HealOverTime:
                    case eSpellType.MercHeal:
                    case eSpellType.OmniHeal:
                    case eSpellType.PBAoEHeal:
                    case eSpellType.SpreadHeal:

                    if (spell.IsInstantCast)
                    {
                        if (Body.IsWithinRadius(livingToHeal, spell.Range))
                            Body.TargetObject = livingToHeal;
                        break;
                    }

                    if (spell.Target == eSpellTarget.GROUP && numNeedHealing < 2)
                        break;

                    if (spell.Target == eSpellTarget.SELF && numNeedHealing < 2)
                        break;

                    if (!LivingHasEffect(livingToHeal, spell) && Body.IsWithinRadius(livingToHeal, spell.Range))
                    {
                        Body.TargetObject = livingToHeal;
                        break;
                    }

                    break;
                }
            }

            if (Body.TargetObject != null)
            {
                //log.Info("Tried to cast " + spell.Name + " " + spell.SpellType.ToString());
                Body.CastSpell(spell, m_mobSpellLine, false);
                return true;
            }

            Body.TargetObject = lastTarget;
            return false;
        }

        /// <summary>
        /// Checks defensive spells. Handles buffs, heals, etc.
        /// </summary>
        protected bool CheckDefensiveSpells(Spell spell)
        {
            if (!CanCastDefensiveSpell(spell))
                return false;

            bool casted = false;

            Body.TargetObject = null;

            // TODO: Instrument classes need special logic.
            if (spell.NeedInstrument)
            {
                return false;
                switch (spell.SpellType)
                {
                    case eSpellType.PowerRegenBuff:
                    {
                        if (!Body.InCombat && !Body.IsMoving)
                        {
                            if (Body.Group != null)
                            {
                                if (Body.Group.GetMembersInTheGroup().Any(groupMember => groupMember.MaxMana > 0 && groupMember.ManaPercent < 80) && !LivingHasEffect(Body, spell))
                                {
                                    Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                                    Body.TargetObject = Body;
                                }
                            }
                            else if (Body.ManaPercent < 75 && !LivingHasEffect(Body, spell))
                            {
                                Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                                Body.TargetObject = Body;
                            }
                            else if (LivingHasEffect(Body, spell))
                            {
                                Body.TargetObject = Body;
                            }
                        }
                    }
                    break;

                    case eSpellType.HealthRegenBuff:

                    if (!Body.InCombat && !Body.IsMoving && !LivingHasEffect(Body, spell))
                    {
                        ECSGameEffect powerRegen = EffectListService.GetEffectOnTarget(Body, eEffect.Pulse, eSpellType.PowerRegenBuff);

                        if (powerRegen == null)
                        {
                            if (!Body.InCombat && !Body.IsMoving)
                            {
                                if (Body.Group != null)
                                {
                                    if (Body.Group.GetMembersInTheGroup().Any(groupMember => groupMember.HealthPercent < 80) && !LivingHasEffect(Body, spell))
                                    {
                                        Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                                        Body.TargetObject = Body;
                                    }
                                }
                                else if (Body.HealthPercent < 80 && !LivingHasEffect(Body, spell))
                                {
                                    Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                                    Body.TargetObject = Body;
                                }
                                else if (LivingHasEffect(Body, spell))
                                {
                                    Body.TargetObject = Body;
                                }
                            }
                        }
                    }
                    break;

                    case eSpellType.EnduranceRegenBuff:

                    if (Body.InCombat)
                    {
                        if (Body.Group != null)
                        {
                            if (Body.Group.GetMembersInTheGroup().Any(groupMember => groupMember.EndurancePercent < 95) && !LivingHasEffect(Body, spell))
                            {
                                Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                                Body.TargetObject = Body;
                            }
                        }
                    }

                    break;

                    case eSpellType.SpeedEnhancement:
                    {
                        if (!Body.InCombat && Body.IsMoving && !LivingHasEffect(Body, spell))
                        {
                            Body.SwitchWeapon(eActiveWeaponSlot.Distance);
                            Body.TargetObject = Body;
                        }
                    }
                    break;
                }
            }
            //else
            //{
            switch (spell.SpellType)
            {
                #region Summon

                case eSpellType.SummonMinion:

                if (Body.ControlledBrain != null)
                {
                    IControlledBrain[] icb = Body.ControlledBrain.Body.ControlledNpcList;
                    int numberofpets = 0;

                    for (int i = 0; i < icb.Length; i++)
                    {
                        if (icb[i] != null)
                            numberofpets++;
                    }

                    if (numberofpets >= icb.Length)
                        break;

                    int cumulativeLevel = 0;

                    foreach (var petBrain in Body.ControlledBrain.Body.ControlledNpcList)
                    {
                        cumulativeLevel += petBrain != null && petBrain.Body != null ? petBrain.Body.Level : 0;
                    }

                    byte newpetlevel = (byte)(Body.Level * spell.Damage * -0.01);

                    if (newpetlevel > spell.Value)
                        newpetlevel = (byte)spell.Value;

                    if (cumulativeLevel + newpetlevel > 75)
                        break;

                    Body.TargetObject = Body;
                }

                break;

                case eSpellType.SummonCommander:
                case eSpellType.SummonUnderhill:
                case eSpellType.SummonDruidPet:
                case eSpellType.SummonSimulacrum:
                case eSpellType.SummonSpiritFighter:

                if (Body.ControlledBrain != null)
                    return false;

                Body.TargetObject = Body;

                break;

                case eSpellType.PetSpell:
                break;

                #endregion Summon

                #region Pulse

                case eSpellType.SpeedEnhancement when spell.Target != eSpellTarget.PET:

                if (!LivingHasEffect(Body, spell))
                    Body.TargetObject = Body;

                break;

                #endregion Pulse

                #region Buffs

                case eSpellType.SpeedEnhancement when spell.Target == eSpellTarget.PET:
                case eSpellType.CombatSpeedBuff when spell.Duration > 20:
                case eSpellType.BodySpiritEnergyBuff:
                case eSpellType.HeatColdMatterBuff:
                case eSpellType.SpiritResistBuff:
                case eSpellType.EnergyResistBuff:
                case eSpellType.HeatResistBuff:
                case eSpellType.ColdResistBuff:
                case eSpellType.BodyResistBuff:
                case eSpellType.MatterResistBuff:
                case eSpellType.AllMagicResistBuff:
                case eSpellType.EnduranceRegenBuff:
                case eSpellType.PowerRegenBuff:
                case eSpellType.AblativeArmor:
                case eSpellType.AcuityBuff:
                case eSpellType.AFHitsBuff:
                case eSpellType.ArmorAbsorptionBuff:
                case eSpellType.ArmorFactorBuff:
                case eSpellType.Buff:
                case eSpellType.CelerityBuff:
                case eSpellType.ConstitutionBuff:
                case eSpellType.CourageBuff:
                case eSpellType.CrushSlashTrustBuff:
                case eSpellType.DexterityBuff:
                case eSpellType.DexterityQuicknessBuff:
                case eSpellType.EffectivenessBuff:
                case eSpellType.FatigueConsumptionBuff:
                case eSpellType.FlexibleSkillBuff:
                case eSpellType.HasteBuff:
                case eSpellType.HealthRegenBuff:
                case eSpellType.HeroismBuff:
                case eSpellType.KeepDamageBuff:
                case eSpellType.MagicResistBuff:
                case eSpellType.MeleeDamageBuff:
                case eSpellType.MesmerizeDurationBuff:
                case eSpellType.MLABSBuff:
                case eSpellType.PaladinArmorFactorBuff:
                case eSpellType.ParryBuff:
                case eSpellType.PowerHealthEnduranceRegenBuff:
                case eSpellType.StrengthBuff:
                case eSpellType.StrengthConstitutionBuff:
                case eSpellType.SuperiorCourageBuff:
                case eSpellType.ToHitBuff:
                case eSpellType.WeaponSkillBuff:
                case eSpellType.DamageAdd:
                case eSpellType.OffensiveProc:
                case eSpellType.DefensiveProc:
                case eSpellType.DamageShield:
                case eSpellType.Bladeturn:
                {
                    if (spell.Concentration > 0)
                    {
                        if (spell.Concentration > Body.Concentration - Body.UsedConcentration)
                            return false;
                    }

                    if (spell.Target == eSpellTarget.PET)
                    {
                        // TODO: Add logic for damage shield use
                        if (spell.SpellType == eSpellType.DamageShield)
                            return false;

                        if (Body.ControlledBrain?.Body != null)
                        {
                            if (!LivingHasEffect(Body.ControlledBrain.Body, spell))
                                Body.TargetObject = Body.ControlledBrain.Body;
                        }

                        break;
                    }

                    // Buff self
                    if (!LivingHasEffect(Body, spell))
                    {
                        Body.TargetObject = Body;
                        break;
                    }

                    if (Body.Group != null)
                    {
                        if (spell.Target == eSpellTarget.REALM || spell.Target == eSpellTarget.GROUP)
                        {
                            foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                            {
                                if (groupMember != Body)
                                {
                                    if (!LivingHasEffect(groupMember, spell) && Body.IsWithinRadius(groupMember, spell.Range) && groupMember.IsAlive)
                                    {
                                        Body.TargetObject = groupMember;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                break;

                #endregion Buffs

                #region Cure Disease/Poison/Mezz

                case eSpellType.CureDisease:

                //Cure self
                if (Body.IsDiseased)
                {
                    Body.TargetObject = Body;
                    break;
                }

                // Cure group members
                if (Body.Group != null)
                {
                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (groupMember != Body)
                        {
                            if (groupMember.IsDiseased && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                }
                break;

                case eSpellType.CurePoison:
                //Cure self
                if (LivingIsPoisoned(Body))
                {
                    Body.TargetObject = Body;
                    break;
                }

                // Cure group members
                if (Body.Group != null)
                {
                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (groupMember != Body)
                        {
                            if (LivingIsPoisoned(groupMember) && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                }
                break;

                case eSpellType.CureMezz:
                if (Body.Group != null)
                {
                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (groupMember != Body)
                        {
                            if (groupMember.IsMezzed && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                }
                break;

                case eSpellType.CureNearsightCustom:
                if (Body.Group != null)
                {
                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (groupMember != Body)
                        {
                            if (LivingHasEffect(groupMember, spell) && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                }
                break;

                #endregion Cure Disease/Poison/Mezz

                #region Charms

                case eSpellType.Charm:
                break;

                #endregion Charms

                case eSpellType.Resurrect:

                if (Body.Group != null)
                {
                    foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                    {
                        if (!groupMember.IsAlive && Body.IsWithinRadius(groupMember, spell.Range))
                        {
                            Body.TargetObject = groupMember;
                            break;
                        }
                    }
                }
                break;

                case eSpellType.LifeTransfer:

                if (Body.Group != null)
                {
                    if (Body.HealthPercent > 50)
                    {
                        GameLiving livingToHeal = null;
                        int threshold = Properties.NPC_HEAL_THRESHOLD / 2;
                        int lowestHealth = 100;

                        foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        {
                            if (groupMember.HealthPercent < threshold)
                            {
                                if (groupMember.HealthPercent < lowestHealth)
                                {
                                    livingToHeal = groupMember;
                                    lowestHealth = groupMember.HealthPercent;
                                }
                            }
                        }

                        if (livingToHeal != null && livingToHeal.IsAlive)
                            Body.TargetObject = livingToHeal;
                    }
                }

                break;

                case eSpellType.PetConversion:
                break;

                default:
                log.Warn($"CheckDefensiveSpells() encountered an unknown spell type [{spell.SpellType}] for {Body?.Name}");
                break;
            }

            if (Body?.TargetObject != null)
            {
                //log.Info(Body.Name + " tried to cast " + spell.Name + " " + spell.SpellType.ToString() + " on " + Body.TargetObject.Name);
                //log.Info(Body.TargetObject.Name + " effect is " + LivingHasEffect((GameLiving)Body.TargetObject, spell));
                    
                Body.CastSpell(spell, m_mobSpellLine, false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks offensive spells.  Handles dds, debuffs, etc.
        /// </summary>
        protected virtual bool CheckOffensiveSpells(Spell spell, bool quickCast = false)
        {
            if (spell.NeedInstrument && Body.ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                Body.SwitchWeapon(eActiveWeaponSlot.Distance);

            if (!Body.IsWithinRadius(Body.TargetObject, spell.Range))
            {
                Body.Follow(Body.TargetObject, spell.Range - 10, 5000);
                return false;
            }

            bool casted = false;

            if (Body.TargetObject is GameLiving living && (spell.Duration == 0 || !LivingHasEffect(living, spell) || spell.SpellType == eSpellType.DirectDamageWithDebuff || spell.SpellType == eSpellType.DamageSpeedDecrease))
            {
                casted = Body.CastSpell(spell, m_mobSpellLine);
            }

            if (casted)
            {
                if (spell.CastTime > 0)
                    Body.StopFollowing();
                else if (Body.FollowTarget != Body.TargetObject)
                    Body.Follow(Body.TargetObject, spell.Range - 10, GameNPC.STICK_MAXIMUM_RANGE);
            }

            return casted;
        }

        protected virtual bool CheckInstantDefensiveSpells(Spell spell)
        {
            if (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            bool castSpell = false;

            switch (spell.SpellType)
            {
                case eSpellType.SavageCrushResistanceBuff:
                case eSpellType.SavageSlashResistanceBuff:
                case eSpellType.SavageThrustResistanceBuff:
                case eSpellType.SavageCombatSpeedBuff:
                case eSpellType.SavageDPSBuff:
                case eSpellType.SavageParryBuff:
                case eSpellType.SavageEvadeBuff:

                if (spell.SpellType == eSpellType.SavageCrushResistanceBuff ||
                    spell.SpellType == eSpellType.SavageSlashResistanceBuff ||
                    spell.SpellType == eSpellType.SavageThrustResistanceBuff &&
                    !CheckSavageResistSpell(spell.SpellType))
                    break;

                if (!LivingHasEffect(Body, spell))
                    castSpell = true;

                break;

                case eSpellType.BodySpiritEnergyBuff:
                case eSpellType.HeatColdMatterBuff:
                case eSpellType.SpiritResistBuff:
                case eSpellType.EnergyResistBuff:
                case eSpellType.HeatResistBuff:
                case eSpellType.ColdResistBuff:
                case eSpellType.BodyResistBuff:
                case eSpellType.MatterResistBuff:
                {
                    // Temp to stop Paladins/Skalds from spamming.
                    // TODO: Smarter use of resist chants.
                    if (spell.Pulse > 0)
                        break;

                    break;
                }

                case eSpellType.CombatHeal:
                case eSpellType.DamageAdd:
                case eSpellType.ArmorFactorBuff:
                case eSpellType.DexterityQuicknessBuff:
                case eSpellType.EnduranceRegenBuff:
                case eSpellType.CombatSpeedBuff:
                case eSpellType.AblativeArmor:
                case eSpellType.Bladeturn:
                case eSpellType.OffensiveProc:
                case eSpellType.SummonHunterPet:

                if (spell.SpellType == eSpellType.CombatSpeedBuff)
                {
                    if (Body.TargetObject != null && !Body.IsWithinRadius(Body.TargetObject, Body.AttackRange))
                        break;
                }

                if (!LivingHasEffect(Body, spell))
                    castSpell = true;

                break;
            }

            if (castSpell)
                Body.CastSpell(spell, m_mobSpellLine);

            return castSpell;
        }

        /// <summary>
        /// Checks Instant Spells.  Handles Taunts, shouts, stuns, etc.
        /// </summary>
        protected virtual bool CheckInstantOffensiveSpells(Spell spell)
        {
            if (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            GameObject lastTarget = Body.TargetObject;
            Body.TargetObject = null;

            switch (spell.SpellType)
            {
                #region Enemy Spells

                case eSpellType.Taunt:

                if (Body.Group?.MimicGroup.MainTank == Body)
                    Body.TargetObject = lastTarget;

                break;

                case eSpellType.DirectDamage:
                case eSpellType.Lifedrain:
                case eSpellType.DexterityDebuff:
                case eSpellType.DexterityQuicknessDebuff:
                case eSpellType.StrengthDebuff:
                case eSpellType.StrengthConstitutionDebuff:
                case eSpellType.CombatSpeedDebuff:
                case eSpellType.DamageOverTime:
                case eSpellType.MeleeDamageDebuff:
                case eSpellType.AllStatsPercentDebuff:
                case eSpellType.CrushSlashThrustDebuff:
                case eSpellType.EffectivenessDebuff:
                case eSpellType.Disease:
                case eSpellType.Stun:
                case eSpellType.Mez:
                case eSpellType.Mesmerize:

                if (spell.IsPBAoE && !Body.IsWithinRadius(lastTarget, spell.Radius))
                    break;

                // Try to limit the debuffs cast to save mana and time spent doing so.
                if (spell.IsInstantCast && MimicBody.CharacterClass.ClassType == eClassType.ListCaster)
                {
                    if (!Util.Chance(25))
                        break;
                }

                if (!LivingHasEffect(lastTarget as GameLiving, spell))
                {
                    Body.TargetObject = lastTarget;
                }

                break;

                #endregion Enemy Spells
            }

            ECSGameEffect pulseEffect = EffectListService.GetPulseEffectOnTarget(Body, spell);

            if (pulseEffect != null)
                return false;

            if (Body.TargetObject != null && (spell.Duration == 0 || (Body.TargetObject is GameLiving living && !(LivingHasEffect(living, spell)))))
            {
                Body.CastSpell(spell, m_mobSpellLine, true);
                Body.TargetObject = lastTarget;
                return true;
            }

            Body.TargetObject = lastTarget;
            return false;
        }

        protected virtual bool CheckSavageResistSpell(eSpellType spellType)
        {
            eDamageType damageType = eDamageType.Natural;

            switch (spellType)
            {
                case eSpellType.SavageCrushResistanceBuff:
                damageType = eDamageType.Crush;
                break;

                case eSpellType.SavageSlashResistanceBuff:
                damageType = eDamageType.Slash;
                break;

                case eSpellType.SavageThrustResistanceBuff:
                damageType = eDamageType.Thrust;
                break;
            }

            if (Body.attackComponent.Attackers.Count > 0)
            {
                foreach (var attacker in Body.attackComponent.Attackers)
                {
                    if (attacker.Key.ActiveWeapon != null)
                    {
                        if (attacker.Key.ActiveWeapon.Type_Damage != 0 && (int)damageType == attacker.Key.ActiveWeapon.Type_Damage)
                            return true;
                    }
                    else if (attacker.Key is GameNPC npc)
                    {
                        if (npc.MeleeDamageType == damageType)
                            return true;
                    }
                }
            }

            return false;
        }

        protected static SpellLine m_mobSpellLine = SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells);
        //protected static SpellLine m_MimicSpellLine = SkillBase.GetSpellLine("MimicSpellLine");

        /// <summary>
        /// Checks if the living target has a spell effect.
        /// Only to be used for spell casting purposes.
        /// </summary>
        /// <returns>True if the living has the effect of will receive it by our current spell.</returns>
        public bool LivingHasEffect(GameLiving target, Spell spell)
        {
            if (target == null)
                return true;

            ISpellHandler spellHandler = Body.castingComponent.SpellHandler;

            // If we're currently casting 'spell' on 'target', assume it already has the effect.
            // This allows spell queuing while preventing casting on the same target more than once.
            if (spellHandler != null && spellHandler.Spell.ID == spell.ID && spellHandler.Target == target)
                return true;

            // May not be the right place for that, but without that check NPCs with more than one offensive or defensive proc will only buff themselves once.
            if (spell.SpellType is eSpellType.OffensiveProc or eSpellType.DefensiveProc)
            {
                if (target.effectListComponent.Effects.TryGetValue(EffectService.GetEffectFromSpell(spell, m_mobSpellLine.IsBaseLine), out List<ECSGameEffect> existingEffects))
                {
                    if (existingEffects.FirstOrDefault(e => e.SpellHandler.Spell.ID == spell.ID || (spell.EffectGroup > 0 && e.SpellHandler.Spell.EffectGroup == spell.EffectGroup)) != null)
                        return true;
                }

                return false;
            }

            ECSGameEffect pulseEffect = EffectListService.GetPulseEffectOnTarget(target, spell);

            if (pulseEffect != null)
                return true;

            eEffect spellEffect = EffectService.GetEffectFromSpell(spell, m_mobSpellLine.IsBaseLine);
            ECSGameEffect effect = EffectListService.GetEffectOnTarget(target, spellEffect);

            if (effect != null)
                return true;

            eEffect immunityToCheck = eEffect.Unknown;

            switch (spellEffect)
            {
                case eEffect.Stun:
                {
                    immunityToCheck = eEffect.StunImmunity;
                    break;
                }
                case eEffect.Mez:
                {
                    immunityToCheck = eEffect.MezImmunity;
                    break;
                }
                case eEffect.Snare:
                case eEffect.MovementSpeedDebuff:
                case eEffect.MeleeSnare:
                {
                    immunityToCheck = eEffect.SnareImmunity;
                    break;
                }
                case eEffect.Nearsight:
                {
                    immunityToCheck = eEffect.NearsightImmunity;
                    break;
                }
            }

            return immunityToCheck != eEffect.Unknown && EffectListService.GetEffectOnTarget(target, immunityToCheck) != null;
        }

        protected static bool LivingIsPoisoned(GameLiving target)
        {
            foreach (IGameEffect effect in target.EffectList)
            {
                //If the effect we are checking is not a gamespelleffect keep going
                if (effect is not GameSpellEffect)
                    continue;

                GameSpellEffect spellEffect = effect as GameSpellEffect;

                // if this is a DOT then target is poisoned
                if (spellEffect.Spell.SpellType == eSpellType.DamageOverTime)
                    return true;
            }

            return false;
        }

        #endregion Spells

        #region DetectDoor

        public virtual void DetectDoor()
        {
            ushort range = (ushort)(ThinkInterval / 800 * Body.CurrentWaypoint.MaxSpeed);

            foreach (GameDoorBase door in Body.CurrentRegion.GetDoorsInRadius(Body, range))
            {
                if (door is GameKeepDoor)
                {
                    if (Body.Realm != door.Realm)
                        return;

                    door.Open();
                    //Body.Say("GameKeep Door is near by");
                    //somebody can insert here another action for GameKeep Doors
                    return;
                }
                else
                {
                    door.Open();
                    return;
                }
            }

            return;
        }

        #endregion DetectDoor
    }
}