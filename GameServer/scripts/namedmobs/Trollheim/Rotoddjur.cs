﻿using System;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.Events;

namespace DOL.GS
{
	public class Rotoddjur : GameEpicNPC
	{
		public Rotoddjur() : base() { }

		[ScriptLoadedEvent]
		public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			if (log.IsInfoEnabled)
				log.Info("Rotoddjur Initializing...");
		}
		public override int GetResist(eDamageType damageType)
		{
			switch (damageType)
			{
				case eDamageType.Slash: return 40;// dmg reduction for melee dmg
				case eDamageType.Crush: return 40;// dmg reduction for melee dmg
				case eDamageType.Thrust: return 40;// dmg reduction for melee dmg
				default: return 30;// dmg reduction for rest resists
			}
		}
		public override double AttackDamage(InventoryItem weapon)
		{
			return base.AttackDamage(weapon) * Strength / 100;
		}
		public override int AttackRange
		{
			get { return 350; }
			set { }
		}
		public override double GetArmorAF(eArmorSlot slot)
		{
			return 500;
		}
		public override double GetArmorAbsorb(eArmorSlot slot)
		{
			// 85% ABS is cap.
			return 0.35;
		}
		public override int MaxHealth
		{
			get { return 10000; }
		}
        public override void Die(GameObject killer)
        {
			foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(CurrentRegionID))
			{
				if (npc != null)
				{
					if (npc.IsAlive && npc.Brain is RotoddjurAddBrain)
					{
						npc.RemoveFromWorld();
					}
				}
			}
			base.Die(killer);
        }
        public override bool AddToWorld()
		{
			INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(60165428);
			LoadTemplate(npcTemplate);
			Strength = npcTemplate.Strength;
			Dexterity = npcTemplate.Dexterity;
			Constitution = npcTemplate.Constitution;
			Quickness = npcTemplate.Quickness;
			Piety = npcTemplate.Piety;
			Intelligence = npcTemplate.Intelligence;
			Empathy = npcTemplate.Empathy;

			Faction = FactionMgr.GetFactionByID(150);
			Faction.AddFriendFaction(FactionMgr.GetFactionByID(150));
			RotoddjurBrain sbrain = new RotoddjurBrain();
			SetOwnBrain(sbrain);
			LoadedFromScript = false;//load from database
			SaveIntoDatabase();
			base.AddToWorld();
			return true;
		}
	}
}
namespace DOL.AI.Brain
{
	public class RotoddjurBrain : StandardMobBrain
	{
		public RotoddjurBrain()
			: base()
		{
			AggroLevel = 100;
			AggroRange = 500;
		}
		public static bool IsPulled = false;
		public override void Think()
		{
			if(!HasAggressionTable())
            {
				FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
				Body.Health = Body.MaxHealth;
				IsPulled = false;
				foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
				{
					if (npc != null)
					{
						if (npc.IsAlive && npc.Brain is RotoddjurAddBrain)
						{
							npc.RemoveFromWorld();
						}
					}
				}
			}
			if(HasAggro)
            {
				GameLiving target = Body.TargetObject as GameLiving;
				if (Util.Chance(25) && target != null)
				{
					if (!target.effectListComponent.ContainsEffectForEffectType(eEffect.DamageOverTime))
					{
						Body.CastSpell(RotodddjurDot, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
					}
				}
				if(IsPulled==false)
                {
					SpawnMushrooms();
					IsPulled = true;
                }
			}
			base.Think();
		}
		public void SpawnMushrooms()
		{
			for (int i = 0; i < Util.Random(4, 5); i++)
			{
				RotoddjurAdd add = new RotoddjurAdd();
				add.X = Body.X + Util.Random(-100, 100);
				add.Y = Body.Y + Util.Random(-100, 100);
				add.Z = Body.Z;
				add.CurrentRegion = Body.CurrentRegion;
				add.Heading = Body.Heading;
				add.RespawnInterval = -1;
				add.AddToWorld();
			}
		}
		private Spell m_RotodddjurDot;
		private Spell RotodddjurDot
		{
			get
			{
				if (m_RotodddjurDot == null)
				{
					DBSpell spell = new DBSpell();
					spell.AllowAdd = false;
					spell.CastTime = 3;
					spell.RecastDelay = 0;
					spell.ClientEffect = 3475;
					spell.Icon = 3475;
					spell.TooltipId = 3475;
					spell.Name = "Orm Poison";
					spell.Description = "Inflicts 70 damage to the target every 3 sec for 30 seconds";
					spell.Message1 = "An acidic cloud surrounds you!";
					spell.Message2 = "{0} is surrounded by an acidic cloud!";
					spell.Message3 = "The acidic mist around you dissipates.";
					spell.Message4 = "The acidic mist around {0} dissipates.";
					spell.Damage = 80;
					spell.Duration = 30;
					spell.Frequency = 30;
					spell.Range = 500;
					spell.SpellID = 11855;
					spell.Target = eSpellTarget.Enemy.ToString();
					spell.Type = eSpellType.DamageOverTime.ToString();
					spell.DamageType = (int)eDamageType.Body;
					spell.Uninterruptible = true;
					m_RotodddjurDot = new Spell(spell, 70);
					SkillBase.AddScriptedSpell(GlobalSpellsLines.Mob_Spells, m_RotodddjurDot);
				}
				return m_RotodddjurDot;
			}
		}
	}
}
/////////////////////////////////////////////////////////////////adds///////////////////////////////////////////////////
namespace DOL.AI.Brain
{
	public class RotoddjurAddBrain : StandardMobBrain
	{
		public RotoddjurAddBrain()
			: base()
		{
			AggroLevel = 100;
			AggroRange = 1000;
		}
		public override void Think()
		{
			base.Think();
		}
	}
}
namespace DOL.GS
{
	public class RotoddjurAdd : GameNPC
	{
		public override int GetResist(eDamageType damageType)
		{
			switch (damageType)
			{
				case eDamageType.Slash: return 35; // dmg reduction for melee dmg
				case eDamageType.Crush: return 35; // dmg reduction for melee dmg
				case eDamageType.Thrust: return 35; // dmg reduction for melee dmg
				default: return 35; // dmg reduction for rest resists
			}
		}
		public override double AttackDamage(InventoryItem weapon)
		{
			return base.AttackDamage(weapon) * Strength / 100;
		}
		public override double GetArmorAF(eArmorSlot slot)
		{
			return 400;
		}
		public override double GetArmorAbsorb(eArmorSlot slot)
		{
			// 85% ABS is cap.
			return 0.35;
		}
		public override short Strength { get => base.Strength; set => base.Strength = 350; }
		public override short Quickness { get => base.Quickness; set => base.Quickness = 80; }
		public override int MaxHealth
		{
			get { return 3000; }
		}
		public override bool AddToWorld()
		{
			Model = 820;
			Name = "Rotoddjur's Servant";
			RespawnInterval = -1;
			MaxSpeedBase = 225;

			Size = (byte)Util.Random(40, 60);
			Level = (byte)Util.Random(62, 66);
			Faction = FactionMgr.GetFactionByID(150);
			Faction.AddFriendFaction(FactionMgr.GetFactionByID(150));
			RotoddjurAddBrain add = new RotoddjurAddBrain();
			SetOwnBrain(add);
			base.AddToWorld();
			return true;
		}
	}
}