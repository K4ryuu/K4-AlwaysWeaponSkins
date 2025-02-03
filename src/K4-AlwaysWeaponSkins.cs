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
		public override string ModuleVersion => "1.1.2";

		public const double RETRY_BLOCK_DELAY = 0.1f;

		public MemoryFunctionVoid<int> GetTeamNumber { get; } = new(GameData.GetSignature("GetTeamNumber"));
		public Dictionary<CCSPlayerController, Queue<(string weapon, CsTeam team)>> TryTeam { get; } = [];
		public Dictionary<CCSPlayerController, Dictionary<string, DateTime>> PreviousRetries { get; } = [];

		public override void Load(bool hotReload)
		{
			GetTeamNumber.Hook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Hook(OverrideGiveNamedItemPost, HookMode.Post);

			AddTimer(5, () =>
			{
				foreach (var player in PreviousRetries.Keys)
				{
					PreviousRetries[player] = PreviousRetries[player].Where(x => DateTime.Now - x.Value < TimeSpan.FromSeconds(RETRY_BLOCK_DELAY)).ToDictionary(x => x.Key, x => x.Value);
				}
			});
		}

		private HookResult OverrideHook(DynamicHook h)
		{
			var itemServices = h.GetParam<CCSPlayer_ItemServices>(0);
			var player = GetPlayerFromItemServices(itemServices);

			if (player is null || !player.IsValid)
				return HookResult.Continue;

			if (!TryTeam.TryGetValue(player, out var tryTeam))
				return HookResult.Continue;

			if (!tryTeam.TryDequeue(out var entry))
				return HookResult.Continue;

			h.SetReturn((int)entry.team);
			return HookResult.Handled;
		}

		private HookResult OverrideGiveNamedItemPost(DynamicHook h)
		{
			string weapon = h.GetParam<string>(1);
			if (string.IsNullOrEmpty(weapon) || !weapon.Contains("weapon"))
				return HookResult.Continue;

			CCSPlayerController? player = GetPlayerFromItemServices(h.GetParam<CCSPlayer_ItemServices>(0));
			CBasePlayerWeapon item = h.GetReturn<CBasePlayerWeapon>();
			if (player == null || !player.IsValid || !item.IsValid)
				return HookResult.Continue;

			CUtlVector<CEconItemAttribute> attributes = item.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.As<CUtlVector<CEconItemAttribute>>();
			bool skinFound = attributes.Count > 0;

			if (!skinFound)
			{
				if (!PreviousRetries.TryGetValue(player, out var retries))
				{
					retries = [];
					PreviousRetries[player] = retries;
				}
				else if (retries.TryGetValue(weapon, out var lastRetry) && DateTime.Now - lastRetry < TimeSpan.FromSeconds(RETRY_BLOCK_DELAY))
				{
					// ? No skins found after the first retry, block further retries for a short period.
					return HookResult.Continue;
				}

				item.AddEntityIOEvent("Kill", item, null, "", 0f);

				if (!TryTeam.TryGetValue(player, out var tryTeam))
				{
					tryTeam = new Queue<(string weapon, CsTeam team)>();
					TryTeam[player] = tryTeam;
				}

				if (!retries.ContainsKey(weapon))
				{
					retries[weapon] = DateTime.Now;
				}

				CsTeam nextTeam = player.Team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

				// ? Running retry with the other team to see the other loadout.

				tryTeam.Enqueue((weapon, nextTeam));

				Server.NextWorldUpdate(() => player.GiveNamedItem(weapon));
			}

			return HookResult.Continue;
		}

		public override void Unload(bool hotReload)
		{
			GetTeamNumber.Unhook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Unhook(OverrideGiveNamedItemPost, HookMode.Post);
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
