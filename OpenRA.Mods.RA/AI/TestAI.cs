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
	
		private int ticks;
		private int timer_mcv;

		//private Player p;

		int2 baseCenter;

		public World world { get { return this.p.PlayerActor.World; } }

        public TestAI(TestAIInfo inf)
        {
            this.Info = inf;

			timer_mcv = 10;
        }
        public void Tick(Actor self)
        {
			++ticks;

			if (ticks >= timer_mcv)
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
        }
        public void Damaged(Actor self, AttackInfo e)
        {
        }

        public void Activate(Player pl)
        {
			this.p = pl;

			enabled = true;

			//builders = new BaseBuilder[] {
			//	new BaseBuilder( this, "Building", q => ChooseBuildingToBuild(q, true) ),
			//	new BaseBuilder( this, "Defense", q => ChooseBuildingToBuild(q, false) ) };
        }

		bool DeployMcv(Actor self)
		{
			/* find our mcv and deploy it */
			var mcv = self.World.Actors
				.FirstOrDefault(a => a.Owner == p && a.Info == Rules.Info["mcv"]);

			if (mcv != null)
			{
				Order ord = new Order("DeployTransform", mcv, false);
				/* Look for a deployable position if it doenst work here.. */
				Transforms T = mcv.TraitOrDefault<Transforms>();

				if (!T.CanDeploy())
				{
					if (mcv.IsIdle)
					{
						world.IssueOrder(new Order("Move", mcv, false) { TargetLocation = baseCenter });
					}
					return true;
				}
				baseCenter = mcv.Location;
				world.IssueOrder(ord);
				return true;
			}
			else
			{
				//BotDebug("AI: Can't find the MCV.");
			}
			return false;
		}

        IBotInfo IBot.Info { get { return Info; } }
    }

	
}