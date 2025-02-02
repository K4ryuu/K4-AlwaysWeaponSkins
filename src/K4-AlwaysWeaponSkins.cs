using System.Collections.ObjectModel;
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
		public override string ModuleVersion => "1.1.0";

		public MemoryFunctionVoid<int> GetTeamNumber { get; } = new(GameData.GetSignature("GetTeamNumber"));
		public Dictionary<CCSPlayerController, Queue<string>> PlayerSkins { get; } = [];

		public static readonly ReadOnlyDictionary<string, CsTeam> TeamSkins = new(new Dictionary<string, CsTeam>
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
