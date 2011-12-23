#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Mods.RA.Buildings;
using OpenRA.Traits;
using XRandom = OpenRA.Thirdparty.Random;

//TODO:
// effectively clear the area around the production buildings' spawn points.
// don't spam the build unit button, only queue one unit then wait for the backoff period.
//    just make the build unit action only occur once every second.

// later:
// don't build units randomly, have a method to it.
// explore spawn points methodically
// once you find a player, attack the player instead of spawn points.

namespace OpenRA.Mods.RA.AI
{
    class TestAIInfo
        : IBotInfo, ITraitInfo
    {
        public readonly string Name = "Unnamed Bot";

        string IBotInfo.Name { get { return this.Name; } }
        public object Create(ActorInitializer init) { return new TestAI(this); }
    }
    class TestAI : ITick, IBot, INotifyDamage
    {
        readonly TestAIInfo Info;

		bool enabled;
		public Player p;
	
		public int ticks;
		private int timer_mcv;
		private int timer_queue;
		private int timer_units;
		private int timer_super;
		private int timer_map;

		private float[,] aggroGrid;

		XRandom random = new XRandom(); //we do not use the synced random number generator.

		//private Player p;

		int2 baseCenter;
		bool baseReady;

		public World world { get { return this.p.PlayerActor.World; } }

        public TestAI(TestAIInfo inf)
        {
            this.Info = inf;

			timer_mcv = 10+random.Next(60);
			timer_queue = 20 + random.Next(60);
			timer_units = 30 + random.Next(60);
			timer_super = 40 + random.Next(60);
			timer_map = 50 + random.Next(60);
        }
        public void Tick(Actor self)
        {
			if (!enabled)
				return;
			++ticks;

			if (ticks >= timer_mcv || ticks==10)
			{
				/* Deploy an MCV if we don't have one. */
				var mcv = self.World.Actors
					.FirstOrDefault(a => a.Owner == p && a.Info == Rules.Info["mcv"]);

				if (mcv == null)
				{
					timer_mcv += 60 * 60;
				}
				else
				{
					DeployMcv(self);
					timer_mcv += 120;
				}
			}
			if (ticks >= timer_queue || ticks==20)
			{
				/* Check through the building queues to see what should be done. */
				timer_queue += 15;
				ai_CheckBuildQueue();
			}

			if (ticks >= timer_units)
			{
				timer_units += 30;
				ai_checkUnits();
			}

			if (ticks >= timer_super)
			{
				timer_super += 60;

				ai_supportPowers();
			} 
			if (ticks >= timer_map)
			{
				timer_map += 60;
			}
		}

		#region MCV specific
		bool DeployMcv(Actor self)
		{
			/* find our mcv and deploy it */
			var mcv = self.World.Actors
				.FirstOrDefault(a => a.Owner == p && a.Info == Rules.Info["mcv"]);

			if (mcv != null)
			{
				/* Look for a deployable position if it doenst work here.. */
				Transforms T = mcv.TraitOrDefault<Transforms>();

				if (mcv.IsIdle)
				{
					if (!T.CanDeploy())
					{
						/* TODO: Check for hostiles near the baseCenter - if they last too long, then deploy somewhere else. */
						if (baseReady && baseCenter != mcv.Location)
						{
							world.IssueOrder(new Order("Move", mcv, false) { TargetLocation = baseCenter });
							return true;
						}
						if (baseReady && baseCenter == mcv.Location)
						{
							baseReady = false;
							int2? target = ChooseBuildLocation("FACT", mcv.Location);
							if (target == null) return false;
							world.IssueOrder(new Order("Move", mcv, false) { TargetLocation = (int2)target });
							return true;
						}
					}

					baseCenter = mcv.Location;
					baseReady = true;
					world.IssueOrder(new Order("DeployTransform", mcv, false));
					return true;
				}
			}
			else
			{
				//BotDebug("AI: Can't find the MCV.");
			}
			return false;
		}
		#endregion

		#region Building Management
		TestBaseBuilder[] builders;

		PowerManager playerPower;

		private void ai_CheckBuildQueue()
		{
			playerPower = p.PlayerActor.Trait<PowerManager>();

			/* For each building queue:
			 * * Check if something can be built. 
			 * * Choose an option randomly, or weighted.
			 * * Verify if there are enough funds/power/etc. 
			 */ 

			/* For each completed building in building queue:
			 * * search for possible location to place.
			 * * Buildings with "weapons" should be placed between base and enemy...
			 * * When placing a building, there should be a 1-tile gap around it.  This ignores the "bib". 
			 */

			if (builders == null)
			{
				builders = new TestBaseBuilder[] {
					new TestBaseBuilder( this, "Building", q => ChooseBuildingToBuild(q, true) ),
					new TestBaseBuilder( this, "Defense", q => ChooseDefenseToBuild(q) ) };
			}

			foreach (var b in builders)
				b.Tick();

			/* TODO: Check if construction should be cancelled:
			 * Building an item that requires power when there's insufficient power
			 * Building something when low on funds, but a refinery/ore miner should be contructed instead.  
			 */

			/* TODO: Check if buildings should be sold. 
			 * Obviously for emergancies, but only to build an ore minter/refinery.  
			 */ 
		}

		/** 
		 * ai_priority_cash: Inhibits production of units and buildings not related to money.
		 * 
		 * */
		private bool ai_priority_cash;

		ActorInfo ChooseBuildingToBuild(ProductionQueue queue, bool buildPower)
		{
			var buildableThings = queue.BuildableItems();

			if (!HasAdequatePower())	/* try to maintain 20% excess power */
			{
				if (!buildPower) return null;

				/* find the best thing we can build which produces power */
				return buildableThings.Where(a => GetPowerProvidedBy(a) > 0)
					.OrderByDescending(a => GetPowerProvidedBy(a)).FirstOrDefault();
			}

			/* TODO: Handle Priority building for miners or refineries */
			var myMiners = p.World
				.ActorsWithTrait<Harvester>()
				.Where(a => a.Actor.Owner == p)
				.Select(a => a.Actor.Info.Name).ToArray();
			var myRefineries = p.World
				.ActorsWithTrait<OreRefinery>()
				.Where (a => a.Actor.Owner == p)
				.Select(a => a.Actor.Info.Name).ToArray();
			/*
			if (myRefineries.Count() == 0 || myMiners.Count() == 0)
			{
				ai_priority_cash = true;
				return buildableThings.Where(a => a.Traits.GetOrDefault<BuildingInfo>(). != null)
					.FirstOrDefault();
			}
			*/
			ai_priority_cash = false;
			
			
			var myBuildings = p.World
				.ActorsWithTrait<Building>()
				.Where(a => a.Actor.Owner == p)
				.Select(a => a.Actor.Info.Name).ToArray();
			 
			/*
			foreach (var frac in Info.BuildingFractions)
				if (buildableThings.Any(b => b.Name == frac.Key))
					if (myBuildings.Count(a => a == frac.Key) < frac.Value * myBuildings.Length)
					{
						if (playerPower.ExcessPower >= Rules.Info[frac.Key].Traits.Get<BuildingInfo>().Power)
							return Rules.Info[frac.Key];
						else
							return buildableThings.Where(a => GetPowerProvidedBy(a) > 0)
								.OrderByDescending(a => GetPowerProvidedBy(a)).FirstOrDefault();
					}
			*/

			/* Final step: Pick a building we don't have. */
			var bInfo = buildableThings.Random(random);
				if (myBuildings.Count(a => a == bInfo.Name) == 0)
				{
					if (playerPower.ExcessPower >= Rules.Info[bInfo.Name].Traits.Get<BuildingInfo>().Power &&
						ChooseBuildLocation(bInfo.Name) != null)
						return bInfo;
				}

			return null;
		}

		ActorInfo ChooseDefenseToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();

			/* Maintain power, and cash */
			if (!HasAdequatePower() || ai_priority_cash == true)
			{
				return null;
			}

			var myBuildingList = p.World
				.ActorsWithTrait<Building>()
				.Where(a => a.Actor.Owner == p);
			var myBuildings = myBuildingList.Select(a => a.Actor.Info.Name).ToArray();

			/* First check support buildings, such as chronosphere, iron curtain and silo. */

			/* Next, build a defense that has a weapon, but only if one building has aggro.. */
			foreach (var building in myBuildingList)
			{
				if (ai_getAggro(building.Actor.Location) > 0)
				{
					var buildableTurrets = buildableThings
						.Where(a => a.Traits.GetOrDefault<AttackTurreted>() != null ||
								a.Traits.GetOrDefault<AttackTesla>() != null);
					if (buildableTurrets.Count() > 0)
					{
						/* TODO: Make better choice for defense. */
						var bInfo = buildableTurrets.Random(random);

						if (playerPower.ExcessPower >= Rules.Info[bInfo.Name].Traits.Get<BuildingInfo>().Power &&
							ChooseBuildLocation(bInfo.Name) != null)
							return bInfo;
					}

					break;
				}
			}

			/* TODO: Check if some buildings could be walled off. Candidates include tesla. 
			   Wait until at least a tech center is built. */

			return null;
		}

		bool HasAdequatePower()
		{
			/* note: CNC `fact` provides a small amount of power. don't get jammed because of that. */
			return playerPower.PowerProvided > 50 &&
				(playerPower.PowerProvided > playerPower.PowerDrained * 1.2 ||
				playerPower.PowerProvided > playerPower.PowerDrained + 200);
		}

		int GetPowerProvidedBy(ActorInfo building)
		{
			var bi = building.Traits.GetOrDefault<BuildingInfo>();
			if (bi == null) return 0;
			return bi.Power;
		}
		internal IEnumerable<ProductionQueue> FindQueues(string category)
		{
			return world.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == p && a.Trait.Info.Type == category)
				.Select(a => a.Trait);
		}

		public int2? ChooseBuildLocation(string actorType, int2 position)
		{
			/* TODO: If building has a weapon, locate a better position for it rather than the outword spiral. */
			int MaxBaseDistance = 25;
			var bi = Rules.Info[actorType].Traits.Get<BuildingInfo>();

			for (var k = 0; k < MaxBaseDistance; k++)
			{
				var tiles = world.FindTilesInCircle(position, k).Shuffle(random);

				foreach (var t in tiles)
					if (world.CanPlaceBuilding(actorType, bi, t, null))
						if (bi.IsCloseEnoughToBase(world, p, actorType, t))
							if (NoBuildingsUnder(Util.ExpandFootprint(
								FootprintUtils.UnpathableTiles(actorType, bi, t), false)))
								return t;
			}

			return null;		// i don't know where to put it.
		}
		public int2? ChooseBuildLocation(string actorType)
		{
			return ChooseBuildLocation(actorType, baseCenter);
		}

		bool NoBuildingsUnder(IEnumerable<int2> cells)
		{
			var bi = world.WorldActor.Trait<BuildingInfluence>();
			return cells.All(c => bi.GetBuildingAt(c) == null);
		}

		#endregion

		#region Unit Management

		Cache<aiGroups, List<Actor>> controlGroups = new Cache<aiGroups, List<Actor>>(_ => new List<Actor>());
		enum aiGroups
		{
			GROUP_DEFENSE, /* Unassigned units, or those responsible for defense */
			GROUP_ASSAULT, /* Responsible for a direct attack. */
			GROUP_FLANK, /* Responsible for hitting weak targets */
			GROUP_SIEGE, /* Engages with artillery at maximum range; non-artillery only attack if the enemy approahces. */
			GROUP_NAVAL, /* Responsible for Naval engagements */
			GROUP_AIRCRAFT, /* Responsible for aerial attacks */

			GROUP_HARVESTER, /* Harvesters, and their escorts */
			GROUP_MCV, /* MCVs */

			GROUP_SPECIAL_ASSAULT, /* Units in this group are part of assault, but are busy because of an order. */
			GROUP_SPECIAL_FLANK, /* Units in this group are part of assault, but are busy because of an order. */

		}

		List<Actor> allUnits = new List<Actor>();

		void ai_checkUnits()
		{
			allUnits.RemoveAll(a => a.Destroyed);
			
			foreach (var unitGroup in controlGroups)
			{
				unitGroup.Value.RemoveAll(a => a.Destroyed);
			}

			var newUnits = p.PlayerActor.World.ActorsWithTrait<IMove>()
				.Where(a => a.Actor.Owner == p
					&& !allUnits.Contains(a.Actor))
					.Select(a => a.Actor).ToArray();
	
			/* Todo: properly assign units to their groups. */
			controlGroups[aiGroups.GROUP_DEFENSE].AddRange(allUnits);

			ai_checkDefenseGroup();

			ai_buildUnits();
		}

		void ai_buildUnits()
		{
			string[] categories = { "Vehicle", "Infantry", "Plane" };
			foreach (string category in categories)
			{
				var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
				if (queue == null)
					return;

				if (queue.BuildableItems().Count()>0)
				{
					var unit = queue.BuildableItems().Random(random);
					world.IssueOrder(Order.StartProduction(queue.self, unit.Name, 1));
				}
				//var unit = ChooseRandomUnitToBuild(queue);
				//if (unit != null && Info.UnitsToBuild.Any(u => u.Key == unit.Name))
				//	world.IssueOrder(Order.StartProduction(queue.self, unit.Name, 1));
			}
		}

		private bool ai_baseAttacked;
		private int2 ai_baseAttackLocation;

		private int ai_defenceLastTick;
		private int ai_defenceAttackTick;
		private int2 ai_defencePosition;

		int2? ChooseDestinationNear(Actor a, int2 desiredMoveTarget)
		{
			var move = a.TraitOrDefault<IMove>();
			if (move == null) return null;

			int2 xy;
			int loopCount = 0; //avoid infinite loops.
			int range = 2;
			do
			{
				//loop until we find a valid move location
				xy = new int2(desiredMoveTarget.X + random.Next(-range, range), desiredMoveTarget.Y + random.Next(-range, range));
				loopCount++;
				range = Math.Max(range, loopCount / 2);
				if (loopCount > 10) return null;
			} while (!move.CanEnterCell(xy) && xy != a.Location);

			return xy;
		}

		bool TryToMove(Actor a, int2 desiredMoveTarget, bool attackMove)
		{
			var xy = ChooseDestinationNear(a, desiredMoveTarget);
			if (xy == null)
				return false;
			world.IssueOrder(new Order(attackMove ? "AttackMove" : "Move", a, false) { TargetLocation = xy.Value });
			return true;
		}

		private void ai_checkDefenseGroup()
		{
			/* TODO: Move the units to a random point in the base, weighted by chance an enemy will attack from there. */
			/* .5 chance it waits at the "frontal position"
			 * .25 chance it waits at one of the flanks
			 * .25 chance it picks a random point.
			 */

			if (ai_baseAttacked)
			{
				if (ticks - ai_defenceAttackTick < 60) return;
				ai_defenceAttackTick = ticks;

				/* TODO: Determine whether there's a greater concentration of enemies at the current location 
				 * or where it's detecting an attack.  
				 */

				ai_defencePosition = ai_baseAttackLocation;

			}
			else
			{

				if (ticks - ai_defenceLastTick < 600) return;
				ai_defenceLastTick = ticks;

				var myBuildings = p.World
					.ActorsWithTrait<Building>()
					.Where(a => a.Actor.Owner == p);

				var randBuilding = myBuildings.Random(random);

				ai_defencePosition = randBuilding.Actor.CenterLocation;
			}

			/* Send units over. */
			foreach (Actor a in controlGroups[aiGroups.GROUP_DEFENSE])
			{
				TryToMove(a, ai_defencePosition, true);
			}
		}

		#endregion

		#region Support Powers

		private void ai_supportPowers()
		{
			var manager = p.PlayerActor.Trait<SupportPowerManager>();
			var powers = manager.Powers.Where(pow => !pow.Value.Disabled);
			var numPowers = powers.Count();
			if (numPowers == 0) return;

			foreach (var power in powers)
			{
				if (!power.Value.Ready) continue;

				Order order = null;
				int2? target = null;
				

				switch (power.Key)
				{
					case "AirstrikePowerInfoOrder":
						target = ai_findAirstrkeLocation();
						if (target != null)
						{
							order = new Order(power.Key, p.PlayerActor, false) { TargetLocation = (int2)target };
						} 
						break; 
					case "ChronoshiftPowerInfoOrder":
						break;
					case "GpsPowerInfoOrder":
						break;
					case "IronCurtainPowerInfoOrder":
						break;
					case "NukePowerInfoOrder":
						target= ai_findAirstrkeLocation();
						if (target != null)
						{
							order = new Order(power.Key, p.PlayerActor, false) { TargetLocation = (int2)target };
						}
						break;
					case "ParatroopersPowerInfoOrder":
						target = ai_findAirstrkeLocation();
						if (target != null)
						{
							order = new Order(power.Key, p.PlayerActor, false) { TargetLocation = (int2)target };
						}
						break;
					case "SonarPulsePowerInfoOrder":
						break;
					case "SpyPlanePowerInfoOrder":
						target = ai_findAirstrkeLocation();
						if (target != null)
						{
							order = new Order(power.Key, p.PlayerActor, false) { TargetLocation = (int2)target };
						} 
						break;
					default:
						break;
				}
				if (order!=null)
					world.IssueOrder(order);
			}
		}

		private int2? ai_findAirstrkeLocation()
		{
			var liveEnemies = world.Players
				.Where(q => p != q && p.Stances[q] == Stance.Enemy)
				.Where(q => p.WinState == WinState.Undefined && q.WinState == WinState.Undefined);

			/* TODO: More intelligent selection of targets */
			var targets = world.Actors
				.Where(a => a.Owner.Stances[p] == Stance.Enemy && a.HasTrait<IOccupySpace>());
				Actor target = null;

			if (targets.Count() > 0)
			{
				target = targets.Random(random);
				return target.Location;
			}
			
			return null;
		}
		#endregion

		#region Aggro Management
		private void ai_addAggro(int2 pos, float amount)
		{
			/* TODO: Add Aggro */
			aggroGrid[pos.X, pos.Y] += amount;
		}

		private float ai_getAggro(int2 pos)
		{
			return aggroGrid[pos.X, pos.Y];
		}

		void ai_smoothAggro()
		{
			float[,] smoothGrid = new float[world.Map.Bounds.Width + world.Map.Bounds.X,
				world.Map.Bounds.Height + world.Map.Bounds.Y];

		}
		#endregion
		public void Damaged(Actor self, AttackInfo e)
		{
			if (!enabled) return;

			var valued = self.Info.Traits.GetOrDefault<ValuedInfo>();
			var cost = valued != null ? valued.Cost : 0;
			var health = self.TraitOrDefault<Health>();

			/* React to base being attacked. */
			if (self.HasTrait<Building>())
			{
				ai_baseAttacked = true;
				ai_baseAttackLocation = e.Attacker.CenterLocation;
				ai_addAggro(self.CenterLocation, cost * e.Damage / health.MaxHP);
			}
			/* React to harvester attacks. */
			if (self.HasTrait<Harvester>())
			{
				ai_baseAttacked = true;
				ai_baseAttackLocation = self.CenterLocation;
				ai_addAggro(self.CenterLocation, cost * e.Damage / health.MaxHP);
			}

			/* Add Aggro */
			if (health != null)
			{
				ai_addAggro(self.CenterLocation, cost * e.Damage / health.MaxHP);
				ai_addAggro(self.CenterLocation, cost * e.Damage / health.MaxHP);
			}
		}

        public void Activate(Player pl)
        {
			this.p = pl;
			enabled = true;

			aggroGrid = new float[world.Map.Bounds.Width + world.Map.Bounds.X,
				world.Map.Bounds.Height + world.Map.Bounds.Y];


			//builders = new BaseBuilder[] {
			//	new BaseBuilder( this, "Building", q => ChooseBuildingToBuild(q, true) ),
			//	new BaseBuilder( this, "Defense", q => ChooseBuildingToBuild(q, false) ) };
        }



        IBotInfo IBot.Info { get { return Info; } }
    }

	
}