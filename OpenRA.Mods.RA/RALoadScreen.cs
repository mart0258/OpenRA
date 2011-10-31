#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Drawing;
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Network;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA
{
	public class RALoadScreen : ILoadScreen
	{
		Dictionary<string, string> Info;
		static string[] Comments = new[] {	"Filling Crates...", "Charging Capacitors...", "Reticulating Splines...",
												"Planting Trees...", "Building Bridges...", "Aging Empires...",
												"Compiling EVA...", "Constructing Pylons...", "Activating Skynet...",
												"Splitting Atoms..."
		};

		Stopwatch lastLoadScreen = new Stopwatch();
		Rectangle StripeRect;
		Sprite Stripe, Logo;
		float2 LogoPos;

		Renderer r;
		public void Init(Dictionary<string, string> info)
		{
			Info = info;
			// Avoid standard loading mechanisms so we
			// can display loadscreen as early as possible
			r = Game.Renderer;
			if (r == null) return;

			var s = new Sheet("mods/ra/uibits/loadscreen.png");
			Logo = new Sprite(s, new Rectangle(0,0,256,256), TextureChannel.Alpha);
			Stripe = new Sprite(s, new Rectangle(256,0,256,256), TextureChannel.Alpha);
			StripeRect = new Rectangle(0, Renderer.Resolution.Height/2 - 128, Renderer.Resolution.Width, 256);
			LogoPos =  new float2(Renderer.Resolution.Width/2 - 128, Renderer.Resolution.Height/2 - 128);
		}

		public void Display()
		{
			if (r == null)
				return;

			// Update text at most every 0.5 seconds
			if (lastLoadScreen.ElapsedTime() < 0.5)
				return;

			lastLoadScreen.Reset();
			var text = Comments.Random(Game.CosmeticRandom);
			var textSize = r.Fonts["Bold"].Measure(text);

			r.BeginFrame(float2.Zero, 1f);
			WidgetUtils.FillRectWithSprite(StripeRect, Stripe);
			r.RgbaSpriteRenderer.DrawSprite(Logo, LogoPos);
			r.Fonts["Bold"].DrawText(text, new float2(Renderer.Resolution.Width - textSize.X - 20, Renderer.Resolution.Height - textSize.Y - 20), Color.White);
			r.EndFrame( new NullInputHandler() );
		}

		public void StartGame()
		{
			Game.ConnectionStateChanged += orderManager =>
			{
				Widget.CloseWindow();
				switch (orderManager.Connection.ConnectionState)
				{
					case ConnectionState.PreConnecting:
						Widget.LoadWidget("MAINMENU_BG", Widget.RootWidget, new WidgetArgs());
						break;
					case ConnectionState.Connecting:
						Widget.OpenWindow("CONNECTING_BG",
							new WidgetArgs() { { "host", orderManager.Host }, { "port", orderManager.Port } });
						break;
					case ConnectionState.NotConnected:
						Widget.OpenWindow("CONNECTION_FAILED_BG",
							new WidgetArgs() { { "orderManager", orderManager } });
						break;
					case ConnectionState.Connected:
						var lobby = Game.OpenWindow("SERVER_LOBBY", new WidgetArgs {});
						lobby.GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").ClearChat();
						lobby.GetWidget("CHANGEMAP_BUTTON").Visible = true;
						lobby.GetWidget("ALLOWCHEATS_CHECKBOX").Visible = true;
						lobby.GetWidget("DISCONNECT_BUTTON").Visible = true;
						break;
				}
			};

			TestAndContinue();
			Game.JoinExternalGame();
		}

		void TestAndContinue()
		{
			Widget.ResetAll();
			if (!FileSystem.Exists(Info["TestFile"]))
			{
				var args = new WidgetArgs()
				{
					{ "continueLoading", () => TestAndContinue() },
					{ "installData", Info }
				};
				Widget.OpenWindow(Info["InstallerMenuWidget"], args);
			}
			else
			{
				Game.LoadShellMap();
				Widget.ResetAll();
				Widget.OpenWindow("MAINMENU_BG");
			}
		}
	}
}

