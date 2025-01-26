using System.Collections.ObjectModel;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace K4AlwaysWeaponSkins
{
	[MinimumApiVersion(300)]
	public class Plugin : BasePlugin
	{
		public override string ModuleName => "CS2 Always Weapon Skins";
		public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
		public override string ModuleDescription => "Apply inventory skins to opposing teams aswell.";
		public override string ModuleVersion => "1.0.1";

		public Dictionary<CCSPlayerController, CsTeam> HandledPlayers = [];
		public Dictionary<CCSPlayerController, List<string>> TeamWeapons = [];

		public static readonly ReadOnlyDictionary<string, CsTeam> TeamSkins = new ReadOnlyDictionary<string, CsTeam>(new Dictionary<string, CsTeam>
		{
			// Counter-Terrorists (CT)
			{ "weapon_usp_silencer", CsTeam.CounterTerrorist },
			{ "weapon_m4a1_silencer", CsTeam.CounterTerrorist },
			{ "weapon_m4a1", CsTeam.CounterTerrorist },
			{ "weapon_famas", CsTeam.CounterTerrorist },
			{ "weapon_aug", CsTeam.CounterTerrorist },
			{ "weapon_mp9", CsTeam.CounterTerrorist },
			{ "weapon_mp5sd", CsTeam.CounterTerrorist },
			{ "weapon_hkp2000", CsTeam.CounterTerrorist },
			{ "weapon_fiveseven", CsTeam.CounterTerrorist },
			{ "weapon_scar20", CsTeam.CounterTerrorist },

			// Terrorists (T)
			{ "weapon_glock", CsTeam.Terrorist },
			{ "weapon_tec9", CsTeam.Terrorist },
			{ "weapon_ak47", CsTeam.Terrorist },
			{ "weapon_galilar", CsTeam.Terrorist },
			{ "weapon_sg556", CsTeam.Terrorist },
			{ "weapon_mac10", CsTeam.Terrorist },
			{ "weapon_sawedoff", CsTeam.Terrorist }
		});

		public override void Load(bool hotReload)
		{
			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Pre);

			RegisterEventHandler((EventPlayerTeam @event, GameEventInfo info) =>
			{
				var player = @event.Userid;
				if (player is null || !player.IsValid || player.IsBot || player.IsHLTV || !HandledPlayers.ContainsKey(player))
					return HookResult.Continue;

				info.DontBroadcast = true;
				return HookResult.Changed;
			}, HookMode.Pre);
		}

		public override void Unload(bool hotReload)
		{
			VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Pre);
		}

		public HookResult OnGiveNamedItemPost(DynamicHook hook)
		{
			var weaponClass = hook.GetParam<string>(1);
			if (string.IsNullOrEmpty(weaponClass) || !weaponClass.StartsWith("weapon_"))
				return HookResult.Continue;

			if (!TeamSkins.TryGetValue(weaponClass, out CsTeam requiredTeam))
				return HookResult.Continue;

			var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
			var player = GetPlayerFromItemServices(itemServices);
			if (player == null)
				return HookResult.Continue;

			if (HandledPlayers.ContainsKey(player))
			{
				if (requiredTeam != player.Team) // ? This section handles that, when multiple items given at once and the teams are mixed, its gonna process them separately
				{
					if (!TeamWeapons.ContainsKey(player))
						TeamWeapons[player] = [];

					if (!TeamWeapons[player].Contains(weaponClass))
						TeamWeapons[player].Add(weaponClass);
					return HookResult.Handled;
				}

				return HookResult.Continue;
			}

			if (player.Team != requiredTeam)
			{
				HandleTeamSwitch(player, requiredTeam);
			}

			return HookResult.Continue;
		}

		private void HandleTeamSwitch(CCSPlayerController player, CsTeam requiredTeam)
		{
			var originalTeam = player.Team;
			HandledPlayers[player] = originalTeam;
			player.SwitchTeam(requiredTeam);

			Server.RunOnTick(32, () => // ? We give some time for other actions aswell
			{
				if (IsValidPlayer(player))
				{
					player.SwitchTeam(originalTeam);
					HandledPlayers.Remove(player);

					Server.NextWorldUpdate(() =>
					{
						if (TeamWeapons.TryGetValue(player, out List<string>? value))
						{
							foreach (var weapon in value)
							{
								player.GiveNamedItem(weapon);
							}

							TeamWeapons.Remove(player);
						}
					});
				}
				else
					HandledPlayers.Remove(player);
			});
		}

		public static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			if (itemServices.Pawn?.Value is not CBasePlayerPawn pawn || !pawn.IsValid)
				return null;

			if (pawn.Controller?.Value == null || !pawn.Controller.IsValid)
				return null;

			var player = new CCSPlayerController(pawn.Controller.Value.Handle);

			return IsValidPlayer(player) ? player : null;
		}

		private static bool IsValidPlayer(CCSPlayerController player)
		{
			return player.IsValid
				&& !player.IsBot
				&& !player.IsHLTV
				&& player.Connected == PlayerConnectedState.PlayerConnected;
		}
	}
}