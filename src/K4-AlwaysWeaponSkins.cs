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
		public override string ModuleDescription => "Apply inventory skins to opposing teams as well.";
		public override string ModuleVersion => "1.1.1";

		public MemoryFunctionVoid<int> GetTeamNumber { get; } = new(GameData.GetSignature("GetTeamNumber"));
		public Dictionary<CCSPlayerController, Queue<string>> PlayerSkins { get; } = [];

		public static readonly ReadOnlyDictionary<string, CsTeam> TeamSkins = new(new Dictionary<string, CsTeam>
		{
			// Counter-Terrorists (CT Only)
			{ "weapon_usp_silencer", CsTeam.CounterTerrorist },  // USP-S
			{ "weapon_hkp2000", CsTeam.CounterTerrorist },  // P2000
			{ "weapon_fiveseven", CsTeam.CounterTerrorist },  // Five-SeveN
			{ "weapon_m4a1_silencer", CsTeam.CounterTerrorist },  // M4A1-S
			{ "weapon_m4a1", CsTeam.CounterTerrorist },  // M4A4
			{ "weapon_famas", CsTeam.CounterTerrorist },  // FAMAS
			{ "weapon_aug", CsTeam.CounterTerrorist },  // AUG
			{ "weapon_mp9", CsTeam.CounterTerrorist },  // MP9
			{ "weapon_scar20", CsTeam.CounterTerrorist },  // SCAR-20 (Auto Sniper)

			// Terrorists (T Only)
			{ "weapon_glock", CsTeam.Terrorist },  // Glock-18
			{ "weapon_tec9", CsTeam.Terrorist },  // Tec-9
			{ "weapon_ak47", CsTeam.Terrorist },  // AK-47
			{ "weapon_galilar", CsTeam.Terrorist },  // Galil AR
			{ "weapon_sg556", CsTeam.Terrorist },  // SG 553
			{ "weapon_mac10", CsTeam.Terrorist },  // MAC-10
			{ "weapon_sawedoff", CsTeam.Terrorist },  // Sawed-Off Shotgun
			{ "weapon_g3sg1", CsTeam.Terrorist }  // G3SG1 (Auto Sniper)
		});

		public override void Load(bool hotReload)
		{
			GetTeamNumber.Hook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Hook(OverrideGiveNamedItem, HookMode.Pre);
		}

		private HookResult OverrideHook(DynamicHook h)
		{
			var itemServices = h.GetParam<CCSPlayer_ItemServices>(0);
			var player = GetPlayerFromItemServices(itemServices);

			if (player != null && player.IsValid && PlayerSkins.TryGetValue(player, out Queue<string>? queue) && queue.TryDequeue(out string? weapon) && TeamSkins.TryGetValue(weapon, out CsTeam team))
			{
				h.SetReturn((int)team);
				return HookResult.Handled;
			}

			return HookResult.Continue;
		}

		private HookResult OverrideGiveNamedItem(DynamicHook h)
		{
			string weapon = h.GetParam<string>(1);
			if (string.IsNullOrEmpty(weapon) || !weapon.Contains("weapon"))
				return HookResult.Continue;

			var itemServices = h.GetParam<CCSPlayer_ItemServices>(0);
			var player = GetPlayerFromItemServices(itemServices);
			if (player == null || !player.IsValid || player.IsBot)
				return HookResult.Continue;

			Server.PrintToChatAll($"Detected {weapon} giving for {player.PlayerName}");

			if (!PlayerSkins.TryGetValue(player, out Queue<string>? queue))
			{
				queue = new Queue<string>();
				PlayerSkins[player] = queue;
			}

			queue.Enqueue(weapon);

			return HookResult.Continue;
		}

		public override void Unload(bool hotReload)
		{
			GetTeamNumber.Unhook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Unhook(OverrideGiveNamedItem, HookMode.Pre);
		}

		public static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			if (itemServices?.Pawn?.Value is CBasePlayerPawn pawn && pawn.IsValid && pawn.Controller?.IsValid == true && pawn.Controller.Value != null)
			{
				var player = new CCSPlayerController(pawn.Controller.Value.Handle);
				if (player.IsValid && !player.IsBot && !player.IsHLTV && player.Connected == PlayerConnectedState.PlayerConnected)
				{
					return player;
				}
			}

			return null;
		}
	}
}
