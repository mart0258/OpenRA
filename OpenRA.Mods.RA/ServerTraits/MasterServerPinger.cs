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
using System.Net;
using OpenRA.Server;
using S = OpenRA.Server.Server;
using System.Linq;
using OpenRA.FileFormats;

namespace OpenRA.Mods.RA.Server
{
	public class MasterServerPinger : ServerTrait, ITick, INotifySyncLobbyInfo, IStartGame
	{
		const int MasterPingInterval = 60 * 3;	// 3 minutes. server has a 5 minute TTL for games, so give ourselves a bit
												// of leeway.
		public int TickTimeout { get { return MasterPingInterval * 10000; } }
		public void Tick(S server)
		{
			if (Environment.TickCount - lastPing > MasterPingInterval * 1000)
				PingMasterServer(server);
			else
				lock (masterServerMessages)
					while (masterServerMessages.Count > 0)
						server.SendChat(null, masterServerMessages.Dequeue());

		}


		public void LobbyInfoSynced(S server) { PingMasterServer(server); }
		public void GameStarted(S server) { PingMasterServer(server); }

		int lastPing = 0;
		bool isInitialPing = true;

		volatile bool isBusy;
		Queue<string> masterServerMessages = new Queue<string>();
		public void PingMasterServer(S server)
		{
			if (isBusy || !server.Settings.AdvertiseOnline) return;

			lastPing = Environment.TickCount;
			isBusy = true;

			Action a = () =>
				{
					try
					{
						var url = "ping.php?port={0}&yaml={1}";
						if (isInitialPing) url += "&new=1";

						using (var wc = new WebClient())
						{
							wc.Proxy = null;
							var gameAnnouncement = new GameAnnouncement()
							{
								Port = server.Settings.ExternalPort,
								Name = server.Settings.Name,
								State = server.GameStarted ? 2 : 1,	// todo: post-game states, etc.
								Players = server.lobbyInfo.Clients.Count,
								Mods = string.Join(",", Game.CurrentMods.Select(f => "{0}@{1}".F(f.Key, f.Value.Version)).ToArray()),
								Map = server.lobbyInfo.GlobalSettings.Map,
								MaxPlayers = server.Map.PlayerCount
							};
							 wc.DownloadData(server.Settings.MasterServer + url.F(server.Settings.ExternalPort, gameAnnouncement));

							if (isInitialPing)
							{
								isInitialPing = false;
								lock (masterServerMessages)
									masterServerMessages.Enqueue("Master server communication established.");
							}
						}
					}
					catch(Exception ex)
					{
						Log.Write("server", ex.ToString());
						lock( masterServerMessages )
							masterServerMessages.Enqueue( "Master server communication failed." );
					}

					isBusy = false;
				};

			a.BeginInvoke(null, null);
		}
	}
	
	class GameAnnouncement
	{
		int Port;
		string Name;
		int State;
		int Players;
		string Mods;
		string Map;
		int MaxPlayers;
	}
}
