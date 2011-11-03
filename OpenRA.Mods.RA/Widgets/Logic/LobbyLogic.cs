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
using System.Drawing;
using System.Linq;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets.Logic
{
	public class LobbyLogic
	{
		Widget lobby, LocalPlayerTemplate, RemotePlayerTemplate, EmptySlotTemplate, EmptySlotTemplateHost,
			   LocalSpectatorTemplate, RemoteSpectatorTemplate, NewSpectatorTemplate;

		ScrollPanelWidget Players;
		Dictionary<string, string> CountryNames;
		string MapUid;
		Map Map;

		ColorPickerPaletteModifier PlayerPalettePreview;

		readonly OrderManager orderManager;

		[ObjectCreator.UseCtor]
		internal LobbyLogic(Widget widget, World world, OrderManager orderManager)
		{
			this.orderManager = orderManager;
			this.lobby = widget;
			Game.BeforeGameStart += CloseWindow;
			Game.LobbyInfoChanged += UpdateCurrentMap;
			Game.LobbyInfoChanged += UpdatePlayerList;
			UpdateCurrentMap();

			PlayerPalettePreview = world.WorldActor.Trait<ColorPickerPaletteModifier>();
			PlayerPalettePreview.Ramp = Game.Settings.Player.ColorRamp;

			Players = lobby.GetWidget<ScrollPanelWidget>("PLAYERS");
			LocalPlayerTemplate = Players.GetWidget("TEMPLATE_LOCAL");
			RemotePlayerTemplate = Players.GetWidget("TEMPLATE_REMOTE");
			EmptySlotTemplate = Players.GetWidget("TEMPLATE_EMPTY");
			EmptySlotTemplateHost = Players.GetWidget("TEMPLATE_EMPTY_HOST");
			LocalSpectatorTemplate = Players.GetWidget("TEMPLATE_LOCAL_SPECTATOR");
			RemoteSpectatorTemplate = Players.GetWidget("TEMPLATE_REMOTE_SPECTATOR");
			NewSpectatorTemplate = Players.GetWidget("TEMPLATE_NEW_SPECTATOR");

			var mapPreview = lobby.GetWidget<MapPreviewWidget>("LOBBY_MAP_PREVIEW");
			mapPreview.Map = () => Map;
			mapPreview.OnMouseDown = mi => LobbyUtils.SelectSpawnPoint( orderManager, mapPreview, Map, mi );
			mapPreview.SpawnColors = () => LobbyUtils.GetSpawnColors( orderManager, Map );

			CountryNames = Rules.Info["world"].Traits.WithInterface<CountryInfo>()
				.Where(c => c.Selectable)
				.ToDictionary(a => a.Race, a => a.Name);
			CountryNames.Add("random", "Random");

			var mapButton = lobby.GetWidget<ButtonWidget>("CHANGEMAP_BUTTON");
			mapButton.OnClick = () =>
			{
				var onSelect = new Action<Map>(m =>
				{
					orderManager.IssueOrder(Order.Command("map " + m.Uid));
					Game.Settings.Server.Map = m.Uid;
					Game.Settings.Save();
				});

				Widget.OpenWindow("MAP_CHOOSER", new WidgetArgs()
				{
					{ "initialMap", MapUid },
					{ "onExit", () => {} },
					{ "onSelect", onSelect }
				});
			};

			mapButton.IsVisible = () => mapButton.Visible && Game.IsHost;

			var disconnectButton = lobby.GetWidget<ButtonWidget>("DISCONNECT_BUTTON");
			disconnectButton.OnClick = () =>
			{
				CloseWindow();
				Game.Disconnect();
				Game.LoadShellMap();
				Widget.OpenWindow("MAINMENU_BG");
			};

			var allowCheats = lobby.GetWidget<CheckboxWidget>("ALLOWCHEATS_CHECKBOX");
			allowCheats.IsChecked = () => orderManager.LobbyInfo.GlobalSettings.AllowCheats;
			allowCheats.OnClick = () =>
			{
				if (Game.IsHost)
					orderManager.IssueOrder(Order.Command(
						"allowcheats {0}".F(!orderManager.LobbyInfo.GlobalSettings.AllowCheats)));
			};

			var startGameButton = lobby.GetWidget<ButtonWidget>("START_GAME_BUTTON");
			startGameButton.OnClick = () =>
			{
				mapButton.Visible = false;
				disconnectButton.Visible = false;
				orderManager.IssueOrder(Order.Command("startgame"));
			};

			// Todo: Only show if the map requirements are met for player slots
			startGameButton.IsVisible = () => Game.IsHost;

			bool teamChat = false;
			var chatLabel = lobby.GetWidget<LabelWidget>("LABEL_CHATTYPE");
			var chatTextField = lobby.GetWidget<TextFieldWidget>("CHAT_TEXTFIELD");
			chatTextField.OnEnterKey = () =>
			{
				if (chatTextField.Text.Length == 0)
					return true;

                if (chatTextField.Text[0] == '/')
                {
                    orderManager.IssueOrder(Order.Command(chatTextField.Text.Substring(1)));
                }
                else
                {
                    orderManager.IssueOrder(Order.Chat(teamChat, chatTextField.Text));
                }
				chatTextField.Text = "";
				return true;
			};

			chatTextField.OnTabKey = () =>
			{
				teamChat ^= true;
				chatLabel.Text = (teamChat) ? "Team:" : "Chat:";
				return true;
			};

			Game.AddChatLine += AddChatLine;
		}

		public void CloseWindow()
		{
			Game.LobbyInfoChanged -= UpdateCurrentMap;
			Game.LobbyInfoChanged -= UpdatePlayerList;
			Game.AddChatLine -= AddChatLine;
			Game.BeforeGameStart -= CloseWindow;

			Widget.CloseWindow();
		}

		void AddChatLine(Color c, string from, string text)
		{
			lobby.GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").AddLine(c, from, text);
		}

		void UpdateCurrentMap()
		{
			if (MapUid == orderManager.LobbyInfo.GlobalSettings.Map) return;
			MapUid = orderManager.LobbyInfo.GlobalSettings.Map;
			Map = new Map(Game.modData.AvailableMaps[MapUid].Path);

			var title = Widget.RootWidget.GetWidget<LabelWidget>("LOBBY_TITLE");
			title.Text = "OpenRA Multiplayer Lobby - " + orderManager.LobbyInfo.GlobalSettings.ServerName + " - " + Map.Title;
		}

		void UpdatePlayerList()
		{
			// This causes problems for people who are in the process of editing their names (the widgets vanish from beneath them)
			// Todo: handle this nicer
			Players.RemoveChildren();

			foreach (var kv in orderManager.LobbyInfo.Slots)
			{
				var s = kv.Value;
				var c = orderManager.LobbyInfo.ClientInSlot(kv.Key);
				Widget template;

				if (c == null)
				{
					if (Game.IsHost)
					{
						template = EmptySlotTemplateHost.Clone();
						var name = template.GetWidget<DropDownButtonWidget>("NAME");
						name.GetText = () => s.Closed ? "Closed" : "Open";
						name.OnMouseDown = _ => LobbyUtils.ShowSlotDropDown(name, s, c, orderManager);
					}
					else
					{
						template = EmptySlotTemplate.Clone();
						var name = template.GetWidget<LabelWidget>("NAME");
						name.GetText = () => s.Closed ? "Closed" : "Open";
					}

					var join = template.GetWidget<ButtonWidget>("JOIN");
					if (join != null)
					{
						join.OnClick = () => orderManager.IssueOrder(Order.Command("slot " + s.PlayerReference));
						join.IsVisible = () => !s.Closed && c == null && !orderManager.LocalClient.IsReady;
					}
				}

				else if ((c.Index == orderManager.LocalClient.Index && !c.IsReady) || (c.Bot != null && Game.IsHost))
				{
					template = LocalPlayerTemplate.Clone();

					var botReady = (c.Bot != null && Game.IsHost
							&& orderManager.LocalClient.IsReady);
					var ready = botReady || c.IsReady;

					if (c.Bot == null)
					{
						LobbyUtils.SetupNameWidget(orderManager, c, template.GetWidget<TextFieldWidget>("NAME"));
					}
					else
					{
						var name = template.GetWidget<DropDownButtonWidget>("BOT_DROPDOWN");
						name.IsVisible = () => true;
						name.IsDisabled = () => ready;
						name.GetText = () => c.Name;
						name.OnMouseDown = _ => LobbyUtils.ShowSlotDropDown(name, s, c, orderManager);
					}			

					var color = template.GetWidget<DropDownButtonWidget>("COLOR");
					color.IsDisabled = () => s.LockColor;
					color.OnMouseDown = _ => LobbyUtils.ShowColorDropDown(color, c, orderManager, PlayerPalettePreview);

					var colorBlock = color.GetWidget<ColorBlockWidget>("COLORBLOCK");
					colorBlock.GetColor = () => c.ColorRamp.GetColor(0);

					var faction = template.GetWidget<DropDownButtonWidget>("FACTION");
					faction.IsDisabled = () => s.LockRace;
					faction.OnMouseDown = _ => LobbyUtils.ShowRaceDropDown(faction, c, orderManager, CountryNames);

					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[c.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => c.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<DropDownButtonWidget>("TEAM");
					team.IsDisabled = () => s.LockTeam;
					team.OnMouseDown = _ => LobbyUtils.ShowTeamDropDown(team, c, orderManager, Map);
					team.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString();

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.IsReady;
					status.OnClick = CycleReady;
					status.IsVisible = () => c.Bot == null;			
				}
				else
				{
					template = RemotePlayerTemplate.Clone();
					template.GetWidget<LabelWidget>("NAME").GetText = () => c.Name;
					var color = template.GetWidget<ColorBlockWidget>("COLOR");
					color.GetColor = () => c.ColorRamp.GetColor(0);

					var faction = template.GetWidget<LabelWidget>("FACTION");
					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[c.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => c.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<LabelWidget>("TEAM");
					team.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString();

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.IsReady;
					if (c.Index == orderManager.LocalClient.Index)
						status.OnClick = CycleReady;
					status.IsVisible = () => c.Bot == null;

					var kickButton = template.GetWidget<ButtonWidget>("KICK");
					kickButton.IsVisible = () => Game.IsHost && c.Index != orderManager.LocalClient.Index;
					kickButton.OnClick = () => orderManager.IssueOrder(Order.Command("kick " + c.Index));
				}

				template.IsVisible = () => true;
				Players.AddChild(template);
			}

			// Add spectators
			foreach (var client in orderManager.LobbyInfo.Clients.Where(client => client.Slot == null))
			{
				var c = client;
				Widget template;
				// Editable spectator
				if (c.Index == orderManager.LocalClient.Index && !c.IsReady)
				{
					template = LocalSpectatorTemplate.Clone();
					LobbyUtils.SetupNameWidget(orderManager, c, template.GetWidget<TextFieldWidget>("NAME"));

					var color = template.GetWidget<DropDownButtonWidget>("COLOR");
					color.OnMouseDown = _ => LobbyUtils.ShowColorDropDown(color, c, orderManager, PlayerPalettePreview);

					var colorBlock = color.GetWidget<ColorBlockWidget>("COLORBLOCK");
					colorBlock.GetColor = () => c.ColorRamp.GetColor(0);

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.IsReady;
					status.OnClick += CycleReady;
				}
				// Non-editable spectator
				else
				{
					template = RemoteSpectatorTemplate.Clone();
					template.GetWidget<LabelWidget>("NAME").GetText = () => c.Name;
					var color = template.GetWidget<ColorBlockWidget>("COLOR");
					color.GetColor = () => c.ColorRamp.GetColor(0);

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.IsReady;
					if (c.Index == orderManager.LocalClient.Index)
						status.OnClick += CycleReady;

					var kickButton = template.GetWidget<ButtonWidget>("KICK");
					kickButton.IsVisible = () => Game.IsHost && c.Index != orderManager.LocalClient.Index;
					kickButton.OnClick = () => orderManager.IssueOrder(Order.Command("kick " + c.Index));
				}

				template.IsVisible = () => true;
				Players.AddChild(template);
			}

			// Spectate button
			if (orderManager.LocalClient.Slot != null && !orderManager.LocalClient.IsReady)
			{
				var spec = NewSpectatorTemplate.Clone();
				var btn = spec.GetWidget<ButtonWidget>("SPECTATE");
				btn.OnClick = () => orderManager.IssueOrder(Order.Command("spectate"));
				spec.IsVisible = () => true;
				Players.AddChild(spec);
			}
		}

		bool SpawnPointAvailable(int index) { return (index == 0) || orderManager.LobbyInfo.Clients.All(c => c.SpawnPoint != index); }

		void CycleReady()
		{
			orderManager.IssueOrder(Order.Command("ready"));
		}
	}
}
