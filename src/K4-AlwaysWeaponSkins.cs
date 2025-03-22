using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace K4AlwaysWeaponSkins
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("ApplyOnNoPreviousOwner")]
		public bool ApplyOnNoPreviousOwner { get; set; } = true;

		[JsonPropertyName("ApplyOnPreviousOwner")]
		public bool ApplyOnPreviousOwner { get; set; } = true;

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 1;
	}

	[MinimumApiVersion(300)]
	public class Plugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "CS2 Always Weapon Skins";
		public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
		public override string ModuleDescription => "Apply inventory skins to opposing teams as well.";
		public override string ModuleVersion => "1.1.5";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public void OnConfigParsed(PluginConfig config)
			=> this.Config = config;

		private const double RETRY_BLOCK_DELAY = 1.0f;
		private const double PICKUP_COOLDOWN = 0.75f;
		private const int MAX_RETRY_COUNT = 2;

		private MemoryFunctionVoid<int> GetTeamNumber { get; } = new(GameData.GetSignature("GetTeamNumber"));
		private Dictionary<CCSPlayerController, Queue<(int weapon, CsTeam team)>> TryTeam { get; } = [];
		private Dictionary<CCSPlayerController, Dictionary<string, DateTime>> PreviousRetries { get; } = [];
		private Dictionary<CCSPlayerController, Dictionary<string, Tuple<int, int, int, int>>> SavedWeapons = [];
		private Dictionary<CCSPlayerController, Dictionary<string, DateTime>> PickupCooldowns = [];
		private Dictionary<CCSPlayerController, Dictionary<string, int>> RetryCounter { get; } = [];
		private Dictionary<CCSPlayerController, Dictionary<uint, DateTime>> RecentlyGivenWeapons { get; } = [];

		private readonly List<string> IgnoredItems = [
			"weapon_decoy",
			"weapon_flashbang",
			"weapon_smokegrenade",
			"weapon_hegrenade",
			"weapon_molotov",
			"weapon_incgrenade",
			"weapon_healthshot",
			"weapon_tagrenade",
			"weapon_breachcharge",
			"weapon_diversion",
			"weapon_firebomb",
			"weapon_frag",
			"weapon_snowball",
			"weapon_tablet",
			"weapon_bumpmine",
			"weapon_shield",
			"weapon_c4"
		];

		public override void Load(bool hotReload)
		{
			GetTeamNumber.Hook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Hook(OverrideGiveNamedItemPost, HookMode.Post);

			AddTimer(5, CleanupOldBlocks, TimerFlags.REPEAT);

			RegisterEventHandler<EventItemPickup>(OnItemPickup);
		}

		private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
		{
			if (!Config.ApplyOnPreviousOwner && !Config.ApplyOnNoPreviousOwner)
				return HookResult.Continue;

			CCSPlayerController? player = @event.Userid;
			if (player == null || !player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
				return HookResult.Continue;

			if (IgnoredItems.Any(x => x.Contains(@event.Item)))
				return HookResult.Continue;

			var cooldowns = GetOrCreateDictionary(PickupCooldowns, player);

			if (cooldowns.TryGetValue(@event.Item, out var lastPickup) && DateTime.Now - lastPickup < TimeSpan.FromSeconds(PICKUP_COOLDOWN))
				return HookResult.Continue;

			cooldowns[@event.Item] = DateTime.Now;

			var previousRetries = GetOrCreateDictionary(PreviousRetries, player);
			if (previousRetries.TryGetValue(@event.Item, out var lastRetry) && DateTime.Now - lastRetry < TimeSpan.FromSeconds(RETRY_BLOCK_DELAY * 2))
				return HookResult.Continue;

			List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];

			foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
			{
				if (!weapon.IsValid || weapon.Value == null)
					continue;

				CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();

				if (ccsWeaponBase == null || !ccsWeaponBase.IsValid || ccsWeaponBase.AttributeManager.Item.ItemDefinitionIndex != @event.Defindex)
					continue;

				var prevOwner = ccsWeaponBase.PrevOwner.Value?.OriginalController?.Value;

				if (prevOwner == player)
					continue;

				bool shouldApply = (prevOwner != null && Config.ApplyOnPreviousOwner) ||
								 (prevOwner == null && Config.ApplyOnNoPreviousOwner);

				if (!shouldApply)
					continue;

				var playerWeapons = GetOrCreateDictionary(SavedWeapons, player);
				playerWeapons[ccsWeaponBase.DesignerName] = new Tuple<int, int, int, int>(
					ccsWeaponBase.Clip1,
					ccsWeaponBase.Clip2,
					ccsWeaponBase.ReserveAmmo[0],
					ccsWeaponBase.ReserveAmmo[1]
				);

				Server.NextFrame(() =>
				{
					if (!ccsWeaponBase.IsValid)
						return;

					var recentWeapons = GetOrCreateDictionary(RecentlyGivenWeapons, player);
					if (recentWeapons.TryGetValue(weapon.Index, out var givenTime) &&
						DateTime.Now - givenTime < TimeSpan.FromSeconds(0.5))
						return;

					string weaponName = ccsWeaponBase.DesignerName;
					ccsWeaponBase.AddEntityIOEvent("Kill", ccsWeaponBase, null, "", 0f);
					Logger.LogInformation($"Player {player.PlayerName} is retrying to give {weaponName}.");
					player.GiveNamedItem(weaponName);
				});
			}

			return HookResult.Continue;
		}

		private HookResult OverrideHook(DynamicHook h)
		{
			var itemServices = h.GetParam<CCSPlayer_ItemServices>(0);
			var player = GetPlayerFromItemServices(itemServices);

			if (player is null || !player.IsValid)
				return HookResult.Continue;

			if (!TryTeam.TryGetValue(player, out var tryTeam) || !tryTeam.TryDequeue(out var entry))
				return HookResult.Continue;

			h.SetReturn((int)entry.team);
			return HookResult.Handled;
		}

		private HookResult OverrideGiveNamedItemPost(DynamicHook h)
		{
			string weapon = h.GetParam<string>(1);
			if (string.IsNullOrEmpty(weapon) || !weapon.Contains("weapon") || IgnoredItems.Contains(weapon))
				return HookResult.Continue;

			CCSPlayerController? player = GetPlayerFromItemServices(h.GetParam<CCSPlayer_ItemServices>(0));
			CBasePlayerWeapon item = h.GetReturn<CBasePlayerWeapon>();
			if (player == null || !player.IsValid || !item.IsValid)
				return HookResult.Continue;

			Logger.LogInformation($"Player {player.PlayerName} received {weapon} - ID: {item.AttributeManager.Item.ItemDefinitionIndex}, SerialNum: {item.Index}");

			var playerWeapons = GetOrCreateDictionary(RecentlyGivenWeapons, player);
			playerWeapons[item.Index] = DateTime.Now;

			CUtlVector<CEconItemAttribute> attributes = item.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.As<CUtlVector<CEconItemAttribute>>();
			bool skinFound = attributes.Count > 0;

			if (!skinFound)
			{
				var weaponRetries = GetOrCreateDictionary(RetryCounter, player);
				int retryCount = weaponRetries.TryGetValue(weapon, out int count) ? count : 0;

				if (retryCount >= MAX_RETRY_COUNT)
				{
					weaponRetries.Remove(weapon);
					TryApplySavedAmmo(player, item);
					return HookResult.Continue;
				}

				weaponRetries[weapon] = retryCount + 1;

				var retries = GetOrCreateDictionary(PreviousRetries, player);
				if (retries.TryGetValue(weapon, out var lastRetry) &&
					DateTime.Now - lastRetry < TimeSpan.FromSeconds(RETRY_BLOCK_DELAY))
				{
					CleanupOldBlocks(player);
					TryApplySavedAmmo(player, item);
					return HookResult.Continue;
				}

				item.AddEntityIOEvent("Kill", item, null, "", 0f);

				var tryTeam = GetOrCreateQueue(TryTeam, player);
				retries[weapon] = DateTime.Now;

				CsTeam nextTeam = player.Team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
				tryTeam.Enqueue((item.AttributeManager.Item.ItemDefinitionIndex, nextTeam));

				AddTimer(0.1f, () =>
				{
					if (player.IsValid)
					{
						Logger.LogInformation($"Player {player.PlayerName} is retrying to give {weapon}.");
						player.GiveNamedItem(weapon);
					}
				});
			}
			else
			{
				if (RetryCounter.TryGetValue(player, out var weaponRetries))
				{
					weaponRetries.Remove(weapon);
				}

				TryApplySavedAmmo(player, item);
			}

			return HookResult.Continue;
		}

		public override void Unload(bool hotReload)
		{
			GetTeamNumber.Unhook(OverrideHook, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Unhook(OverrideGiveNamedItemPost, HookMode.Post);
		}

		private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			if (itemServices?.Pawn?.Value is not CBasePlayerPawn pawn || !pawn.IsValid ||
				pawn.Controller == null || !pawn.Controller.IsValid || pawn.Controller.Value == null)
				return null;

			var player = new CCSPlayerController(pawn.Controller.Value.Handle);
			return player.IsValid && !player.IsBot && !player.IsHLTV &&
				   player.Connected == PlayerConnectedState.PlayerConnected ? player : null;
		}

		private void CleanupOldBlocks()
		{
			foreach (var p in PreviousRetries.Keys.ToList())
				CleanupDictionary(p, PreviousRetries, RETRY_BLOCK_DELAY);

			foreach (var p in PickupCooldowns.Keys.ToList())
				CleanupDictionary(p, PickupCooldowns, PICKUP_COOLDOWN);

			foreach (var p in RecentlyGivenWeapons.Keys.ToList())
				CleanupDictionary(p, RecentlyGivenWeapons, 1.0);

			RetryCounter.Clear();
		}

		private void CleanupOldBlocks(CCSPlayerController player)
		{
			CleanupDictionary(player, PreviousRetries, RETRY_BLOCK_DELAY);
			CleanupDictionary(player, PickupCooldowns, PICKUP_COOLDOWN);
			CleanupDictionary(player, RecentlyGivenWeapons, 1.0);

			RetryCounter.Remove(player);
		}

		private static void CleanupDictionary<TKey>(CCSPlayerController player, Dictionary<CCSPlayerController, Dictionary<TKey, DateTime>> dict, double timeoutSeconds)
			where TKey : notnull
		{
			if (dict.TryGetValue(player, out var entries))
			{
				dict[player] = entries
					.Where(x => DateTime.Now - x.Value < TimeSpan.FromSeconds(timeoutSeconds))
					.ToDictionary(x => x.Key, x => x.Value);
			}
		}

		private void TryApplySavedAmmo(CCSPlayerController player, CBasePlayerWeapon weapon)
		{
			if (SavedWeapons.TryGetValue(player, out var playerWeapons) &&
				playerWeapons.TryGetValue(weapon.DesignerName, out var ammo))
			{
				weapon.Clip1 = ammo.Item1;
				weapon.Clip2 = ammo.Item2;
				weapon.ReserveAmmo[0] = ammo.Item3;
				weapon.ReserveAmmo[1] = ammo.Item4;

				playerWeapons.Remove(weapon.DesignerName);
			}
		}

		private static Dictionary<TKey, TValue> GetOrCreateDictionary<TKey, TValue>(
			Dictionary<CCSPlayerController, Dictionary<TKey, TValue>> dict,
			CCSPlayerController player)
			where TKey : notnull
		{
			if (!dict.TryGetValue(player, out var result))
			{
				result = [];
				dict[player] = result;
			}
			return result;
		}

		private static Queue<TValue> GetOrCreateQueue<TValue>(
			Dictionary<CCSPlayerController, Queue<TValue>> dict,
			CCSPlayerController player)
		{
			if (!dict.TryGetValue(player, out var result))
			{
				result = new Queue<TValue>();
				dict[player] = result;
			}
			return result;
		}
	}
}
