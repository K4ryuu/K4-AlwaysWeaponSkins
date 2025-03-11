using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

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
		public override string ModuleVersion => "1.1.4";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public void OnConfigParsed(PluginConfig config)
			=> this.Config = config;

		public const double RETRY_BLOCK_DELAY = 0.75f;
		public const double PICKUP_COOLDOWN = 0.5f;

		public MemoryFunctionVoid<int> GetTeamNumber { get; } = new(GameData.GetSignature("GetTeamNumber"));
		public Dictionary<CCSPlayerController, Queue<(string weapon, CsTeam team)>> TryTeam { get; } = [];
		public Dictionary<CCSPlayerController, Dictionary<string, DateTime>> PreviousRetries { get; } = [];
		public Dictionary<CCSPlayerController, Dictionary<string, Tuple<int, int, int, int>>> SavedWeapons = [];
		public Dictionary<CCSPlayerController, Dictionary<string, DateTime>> PickupCooldowns = [];

		public List<string> IgnoredItems = [
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

			AddTimer(5, () =>
			{
				CleanupOldBlocks();
			}, TimerFlags.REPEAT);

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

			if (!PickupCooldowns.TryGetValue(player, out var cooldowns))
			{
				cooldowns = [];
				PickupCooldowns[player] = cooldowns;
			}
			else if (cooldowns.TryGetValue(@event.Item, out var lastPickup) && DateTime.Now - lastPickup < TimeSpan.FromSeconds(PICKUP_COOLDOWN))
			{
				return HookResult.Continue;
			}

			cooldowns[@event.Item] = DateTime.Now;

			List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];

			foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
			{
				if (weapon.IsValid && weapon.Value != null)
				{
					CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();

					if (ccsWeaponBase != null && ccsWeaponBase.IsValid)
					{
						if (ccsWeaponBase.AttributeManager.Item.ItemDefinitionIndex != @event.Defindex)
							continue;

						if (ccsWeaponBase.PrevOwner.Index == player.PlayerPawn.Index)
							continue;

						bool shouldApply = ccsWeaponBase.PrevOwner != null && Config.ApplyOnPreviousOwner || ccsWeaponBase.PrevOwner == null && Config.ApplyOnNoPreviousOwner;
						if (!shouldApply)
							continue;

						if (!SavedWeapons.TryGetValue(player, out var playerWeapons))
						{
							playerWeapons = [];
							SavedWeapons[player] = playerWeapons;
						}

						playerWeapons[ccsWeaponBase.DesignerName] = new Tuple<int, int, int, int>(ccsWeaponBase.Clip1, ccsWeaponBase.Clip2, ccsWeaponBase.ReserveAmmo[0], ccsWeaponBase.ReserveAmmo[1]);

						Server.NextFrame(() =>
						{
							if (!ccsWeaponBase.IsValid)
								return;

							string weaponName = ccsWeaponBase.DesignerName;
							ccsWeaponBase.AddEntityIOEvent("Kill", ccsWeaponBase, null, "", 0f);

							Server.NextFrame(() =>
							{
								player.GiveNamedItem(weaponName);
							});
						});
					}
				}
			}

			return HookResult.Continue;
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
			if (string.IsNullOrEmpty(weapon) || !weapon.Contains("weapon") || IgnoredItems.Contains(weapon))
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
					CleanupOldBlocks(player);
					TryApplySavedAmmo(player, item);
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
			else
				TryApplySavedAmmo(player, item);

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

		public void CleanupOldBlocks(CCSPlayerController? player = null)
		{
			if (player != null)
			{
				CleanupDictionary(player, PreviousRetries, RETRY_BLOCK_DELAY);
				CleanupDictionary(player, PickupCooldowns, PICKUP_COOLDOWN);
			}
			else
			{
				foreach (var p in PreviousRetries.Keys.ToList())
				{
					CleanupDictionary(p, PreviousRetries, RETRY_BLOCK_DELAY);
				}

				foreach (var p in PickupCooldowns.Keys.ToList())
				{
					CleanupDictionary(p, PickupCooldowns, PICKUP_COOLDOWN);
				}
			}
		}

		private static void CleanupDictionary(CCSPlayerController player, Dictionary<CCSPlayerController, Dictionary<string, DateTime>> dict, double timeoutSeconds)
		{
			if (dict.TryGetValue(player, out var entries))
			{
				dict[player] = entries
					.Where(x => DateTime.Now - x.Value < TimeSpan.FromSeconds(timeoutSeconds))
					.ToDictionary(x => x.Key, x => x.Value);
			}
		}

		public void TryApplySavedAmmo(CCSPlayerController player, CBasePlayerWeapon weapon)
		{
			if (SavedWeapons.TryGetValue(player, out var playerWeapons) && playerWeapons.TryGetValue(weapon.DesignerName, out var ammo))
			{
				weapon.Clip1 = ammo.Item1;
				weapon.Clip2 = ammo.Item2;
				weapon.ReserveAmmo[0] = ammo.Item3;
				weapon.ReserveAmmo[1] = ammo.Item4;

				playerWeapons.Remove(weapon.DesignerName);
			}
		}
	}
}
