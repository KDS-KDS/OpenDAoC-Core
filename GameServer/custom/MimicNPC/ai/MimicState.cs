﻿using System;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using DOL.GS;
using DOL.GS.Scripts;
using DOL.GS.ServerProperties;
using log4net;

namespace DOL.AI.Brain
{
    public class MimicState : FSMState
    {
        protected static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected MimicBrain _brain = null;

        public bool Init;
        public bool Flee;

        public MimicState(MimicBrain brain) : base()
        {
            _brain = brain;
        }

        public override void Think() { }
        public override void Enter() { }
        public override void Exit() { }
    }

    public class MimicState_WakingUp : MimicState
    {
        public MimicState_WakingUp(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.WAKING_UP;
        }

        public override void Think()
        {
            if (!Init)
            {
                _brain.AggroLevel = 100;
                _brain.AggroRange = 3600;

                _brain.PvPMode = false;
                _brain.Roam = true;
                _brain.Defend = false;

                _brain.Body.RoamingRange = WorldMgr.VISIBILITY_DISTANCE;

                _brain.CheckDefensiveAbilities();
                //_brain.Body.SortSpells();

                Init = true;
            }

            if (_brain.Body.Group != null)
            {
                if (_brain.Body.Group.MimicGroup.CampPoint != null)
                    _brain.FSM.SetCurrentState(eFSMStateType.CAMP);
                else if (_brain.Body.Group.LivingLeader != _brain.Body)
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER);
                    return;
                }
            }

            if (!_brain.Body.attackComponent.AttackState && _brain.Body.CanRoam)
            {
                _brain.FSM.SetCurrentState(eFSMStateType.ROAMING);
                return;
            }

            if (_brain.HasPatrolPath())
            {
                _brain.FSM.SetCurrentState(eFSMStateType.PATROLLING);
                return;
            }

            if (!_brain.PreventCombat)
            {
                if (_brain.CheckProximityAggro(_brain.AggroRange))
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }
            }

            _brain.FSM.SetCurrentState(eFSMStateType.IDLE);
            base.Think();
        }
    }

    public class MimicState_Idle : MimicState
    {
        public MimicState_Idle(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.IDLE;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering IDLE");

            base.Enter();
        }

        public override void Think()
        {
            if (_brain.HasPatrolPath())
            {
                _brain.FSM.SetCurrentState(eFSMStateType.PATROLLING);
                return;
            }

            if (_brain.Body.CanRoam)
            {
                _brain.FSM.SetCurrentState( eFSMStateType.ROAMING);
                return;
            }

            if (_brain.IsBeyondTetherRange())
            {
                _brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
                return;
            }

            if (!_brain.PreventCombat)
            {
                if (_brain.CheckProximityAggro(_brain.AggroRange))
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }
            }

            _brain.CheckSpells(MimicBrain.eCheckSpellType.Defensive);

            base.Think();
        }
    }

    public class MimicState_FollowLeader : MimicState
    {
        GameLiving leader;

        public MimicState_FollowLeader(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.FOLLOW_THE_LEADER;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering FOLLOW_THE_LEADER");

            if (_brain.Body.Group != null)
            {
                leader = _brain.Body.Group.LivingLeader;
                _brain.Body.Follow(_brain.Body.Group.LivingLeader, _brain.Body.movementComponent.FollowMinDistance, _brain.Body.movementComponent.FollowMaxDistance);
            }
            else
                _brain.FSM.SetCurrentState(eFSMStateType.IDLE);

            if (_brain.PvPMode)
            {
                _brain.AggroRange = 3600;
            }
            else
                _brain.AggroRange = 500;

            base.Enter();
        }

        public override void Think()
        {
            if (leader == null)
                leader = _brain.Body.Group.LivingLeader;

            //if (!_brain.PreventCombat)
            //{
                //if (_brain.CheckProximityAggro(_brain.AggroRange))
                //{
                //    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                //    return;
                //}
            //}

            if (_brain.Body.FollowTarget != leader)
                _brain.Body.Follow(_brain.Body.Group.LivingLeader, 300, _brain.Body.movementComponent.FollowMaxDistance);

            if (!_brain.Body.IsMoving && !_brain.Body.InCombat)
            {
                if (!_brain.CheckSpells(MimicBrain.eCheckSpellType.Defensive))
                {
                    if (!CheckStats(75))
                    {
                        if (_brain.Body.IsSitting)
                            _brain.MimicBody.Sit(false);

                       //_brain.Body.Group?.MimicGroup.AddToQueue(new MimicGroup.QueueRequest(_brain.Body, eQueueMessage.WaitForBuffs, eMimicGroupRole.Leader));
                    }
                    else
                        _brain.MimicBody.Sit(true);
                }
            }

            base.Think();
        }

        private bool CheckStats(short threshold)
        {
            if (_brain.Body.HealthPercent < threshold || (_brain.Body.MaxMana > 0 && _brain.Body.ManaPercent < threshold) || _brain.Body.EndurancePercent < threshold)
                return true;

            return false;
        }

        public override void Exit()
        {
            _brain.Body.StopFollowing();

            if (_brain.PvPMode)
            {
                _brain.AggroRange = 3600;
            }
            else
                _brain.AggroRange = 500;

            base.Exit();
        }
    }

    public class MimicState_Aggro : MimicState
    {
        private const int LEAVE_WHEN_OUT_OF_COMBAT_FOR = 25000;

        private long _aggroTime = GameLoop.GameLoopTime; // Used to prevent leaving on the first think tick, due to `InCombatInLast` returning false.

        public MimicState_Aggro(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.AGGRO;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering AGGRO");

            if (_brain.Body.IsSitting)
                _brain.MimicBody.Sit(false);

            _aggroTime = GameLoop.GameLoopTime;
            base.Enter();
        }

        public override void Exit()
        {
            if (_brain.Body.attackComponent.AttackState)
                _brain.Body.StopAttack();

            _brain.Body.TargetObject = null;

            //_brain.Body.SpawnPoint = new Point3D(_brain.Body.X, _brain.Body.Y, _brain.Body.Z);
            base.Exit();
        }

        public override void Think()
        {
            if (_brain.PvPMode)
            {
                if (!_brain.HasAggro)
                {
                    _brain.CheckProximityAggro(_brain.AggroRange);
                }
            }

            //if (_brain.IsFleeing)
            //{
            //    if (!_brain.Body.IsWithinRadius(_brain.Body.TargetObject, 500))
            //    {
            //        _brain.IsFleeing = false;
            //    }
            //}

            if (!_brain.HasAggro || (!_brain.Body.InCombatInLast(LEAVE_WHEN_OUT_OF_COMBAT_FOR) && _aggroTime + LEAVE_WHEN_OUT_OF_COMBAT_FOR <= GameLoop.GameLoopTime))
            {
                if (!_brain.Body.IsMezzed && !_brain.Body.IsStunned)
                {
                    if (_brain.PvPMode)
                    {
                        if (_brain.Roam)
                        {
                            if (_brain.Body.Group != null)
                            {
                                if (_brain.Body.Group.LivingLeader == _brain.Body)
                                    _brain.FSM.SetCurrentState(eFSMStateType.ROAMING);
                                else
                                    _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER);
                            }
                            else
                                _brain.FSM.SetCurrentState(eFSMStateType.ROAMING);
                        }
                        else if (_brain.Defend)
                        {
                            if (_brain.Body.Group != null)
                            {
                                if (_brain.Body.Group.LivingLeader == _brain.Body)
                                    _brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
                                else
                                    _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER);
                            }
                            else
                                _brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
                        }
                    }
                    else
                    {
                        if (_brain.Body.Group != null)
                        {
                            if (_brain.Body.Group.MimicGroup.CampPoint != null)
                                _brain.FSM.SetCurrentState(eFSMStateType.CAMP);
                            else if (_brain.Body.Group.LivingLeader == _brain.Body)
                                _brain.FSM.SetCurrentState(eFSMStateType.IDLE);
                            else
                                _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW_THE_LEADER);
                        }
                    }

                    return;
                }
            }

            if (_brain.Body.Flags.HasFlag(GameNPC.eFlags.STEALTH))
                _brain.Body.Flags ^= GameNPC.eFlags.STEALTH;

            _brain.AttackMostWanted();
            _brain.CheckOffensiveAbilities();

            base.Think();
        }
    }

    public class MimicState_Roaming : MimicState
    {
        private const int ROAM_COOLDOWN = 10 * 1000;
        private long _lastRoamTick = 0;

        private const int ROAM_CHANCE_DEFEND = 20;
        private const int ROAM_CHANCE_ROAM = 99;

        private bool _processingRequest;

        public MimicState_Roaming(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.ROAMING;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering ROAM");

            base.Enter();
        }

        public override void Exit()
        {
            _processingRequest = false;

            base.Exit();
        }

        public override void Think()
        {
            //if (_brain.IsBeyondTetherRange())
            //{
            //    _brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
            //    return;
            //}

            //MimicGroup.QueueRequest request = _brain.Body.Group?.MimicGroup.ProcessQueue(eMimicGroupRole.Leader);

            //if (request != null)
            //{
            //    switch (request.QueueMessage)
            //    {
            //        case eQueueMessage.WaitForBuffs:
            //            _processingRequest = true;
            //        break;
            //    }
            //}

            if (!_brain.PreventCombat)
            {
                if (_brain.CheckProximityAggro(_brain.AggroRange))
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }
            }

            if (!_brain.Body.IsCasting)
            {
                int chance = Properties.GAMENPC_RANDOMWALK_CHANCE;

                if (_brain.PvPMode)
                {
                    if (_brain.Roam)
                        chance = ROAM_CHANCE_ROAM;
                    else
                        chance = ROAM_CHANCE_DEFEND;
                }

                _processingRequest = false;

                if (_brain.Body.Group != null)
                {
                    foreach (GameLiving groupMember in _brain.Body.Group.GetMembersInTheGroup())
                        if (groupMember is MimicNPC)
                            if (groupMember.IsCasting)
                                _processingRequest = true;
                }

                if (!_processingRequest && _lastRoamTick + ROAM_COOLDOWN <= GameLoop.GameLoopTime && Util.Chance(chance) && !_brain.Body.IsMoving)
                {
                    //_brain.Body.SpawnPoint = new Point3D(_brain.Body.X, _brain.Body.Y, _brain.Body.Z);
                    _brain.Body.Roam(_brain.Body.MaxSpeed);
                    _brain.Body.FireAmbientSentence(GameNPC.eAmbientTrigger.roaming, _brain.Body);
                    _lastRoamTick = GameLoop.GameLoopTime;
                }
            }

            _brain.CheckSpells(MimicBrain.eCheckSpellType.Defensive);
            base.Think();
        }
    }

    public class MimicState_Camp : MimicState
    {
        private int aggroRange;

        public MimicState_Camp(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.CAMP;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering CAMP");

            int randomX = _brain.Body.CurrentRegion.IsDungeon ? Util.Random(-50, 50) : Util.Random(-100, 100);
            int randomY = _brain.Body.CurrentRegion.IsDungeon ? Util.Random(-50, 50) : Util.Random(-100, 100);

            _brain.Body.SpawnPoint = _brain.Body.Group.MimicGroup.CampPoint;
            _brain.Body.SpawnPoint.X += randomX;
            _brain.Body.SpawnPoint.Y += randomY;

            _brain.AggroRange = _brain.Body.CurrentRegion.IsDungeon ? 250 : 500;

            if (_brain.Body.WasStealthed)
                _brain.Body.Flags |= GameNPC.eFlags.STEALTH;

            _brain.ClearAggroList();
            _brain.Body.ReturnToSpawnPoint(GamePlayer.PLAYER_BASE_SPEED);

            base.Enter();
        }

        public override void Think()
        {
            if (_brain.CheckProximityAggro(_brain.AggroRange))
            {
                _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                return;
            }

            if (!_brain.Body.IsMoving && !_brain.Body.InCombat)
            {
                if (!_brain.CheckSpells(MimicBrain.eCheckSpellType.Defensive))
                {
                    if (!CheckStats(75))
                    {
                        if (_brain.Body.IsSitting)
                            _brain.MimicBody.Sit(false);

                        //_brain.Body.Group?.MimicGroup.AddToQueue(new MimicGroup.QueueRequest(_brain.Body, eQueueMessage.WaitForBuffs, eMimicGroupRole.Leader));
                    }
                    else
                        _brain.MimicBody.Sit(true);
                }
            }

            base.Think();
        }

        private bool CheckStats(short threshold)
        {
            if (_brain.Body.HealthPercent < threshold || (_brain.Body.MaxMana > 0 && _brain.Body.ManaPercent < threshold) || _brain.Body.EndurancePercent < threshold)
                return true;
            
            return false;
        }
    }

    public class MimicState_ReturnToSpawn : MimicState
    {
        public MimicState_ReturnToSpawn(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.RETURN_TO_SPAWN;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is entering RETURN_TO_SPAWN");

            if (_brain.Body.WasStealthed)
                _brain.Body.Flags |= GameNPC.eFlags.STEALTH;

            _brain.ClearAggroList();
            _brain.Body.ReturnToSpawnPoint(GamePlayer.PLAYER_BASE_SPEED);
            base.Enter();
        }

        public override void Think()
        {
            if (!_brain.Body.IsNearSpawn &&
                (!_brain.HasAggro || !_brain.Body.IsEngaging) &&
                (!_brain.Body.IsReturningToSpawnPoint) &&
                _brain.Body.CurrentSpeed == 0)
            {
                _brain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
                _brain.Body.TurnTo(_brain.Body.SpawnHeading);
                return;
            }

            if (_brain.Body.IsNearSpawn)
            {
                _brain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
                _brain.Body.TurnTo(_brain.Body.SpawnHeading);
                return;
            }

            if (!_brain.PreventCombat)
            {
                if (_brain.CheckProximityAggro(_brain.AggroRange))
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }
            }

            base.Think();
        }
    }

    public class MimicState_Patrolling : MimicState
    {
        public MimicState_Patrolling(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.PATROLLING;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} is PATROLLING");

            _brain.Body.MoveOnPath(_brain.Body.MaxSpeed);
            _brain.ClearAggroList();
            base.Enter();
        }

        public override void Think()
        {
            if (_brain.IsBeyondTetherRange())
            {
                _brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
            }

            if (!_brain.PreventCombat)
            {
                if (_brain.CheckProximityAggro(_brain.AggroRange))
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }
            }

            base.Think();
        }
    }

    public class MimicState_Dead : MimicState
    {
        public MimicState_Dead(MimicBrain brain) : base(brain)
        {
            StateType = eFSMStateType.DEAD;
        }

        public override void Enter()
        {
            if (ECS.Debug.Diagnostics.StateMachineDebugEnabled)
                Console.WriteLine($"{_brain.Body} has entered DEAD state");

            _brain.ClearAggroList();
            base.Enter();
        }

        public override void Think()
        {
            _brain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
            base.Think();
        }
    }
}
