using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace K4AlwaysWeaponSkins
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("ApplyToMapWeapons")]
		public bool ApplyToMapWeapons { get; set; } = true;

		[JsonPropertyName("ApplyOnNoPreviousOwner")]
		public bool ApplyOnNoPreviousOwner { get; set; } = true;

		[JsonPropertyName("ApplyOnPreviousOwner")]
		public bool ApplyOnPreviousOwner { get; set; } = true;


		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 2;
	}

	[MinimumApiVersion(300)]
	public class Plugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "CS2 Always Weapon Skins";
		public override string ModuleAuthor => "K4ryuu @ KitsuneLab (Final)";
		public override string ModuleDescription => "Apply inventory skins to opposing teams as well.";
		public override string ModuleVersion => "2.0.0";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public void OnConfigParsed(PluginConfig config)
			=> this.Config = config;

		private readonly Dictionary<CCSPlayerController, Dictionary<string, Tuple<int, int, int, int>>> SavedWeapons = [];
		private readonly HashSet<string> PickupLocks = [];

		private MemoryFunctionVoid<IntPtr, string, int, bool, IntPtr>? FindMatchingWeaponsForTeamLoadout = null;

		public override void Load(bool hotReload)
		{
			try
			{
				FindMatchingWeaponsForTeamLoadout = new MemoryFunctionVoid<IntPtr, string, int, bool, IntPtr>(GameData.GetSignature("CCSPlayer_FindMatchingWeaponsForTeamLoadout"));
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to initialize FindMatchingWeaponsForTeamLoadout: {ex.Message}");
			}

			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPre, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);

			RegisterEventHandler<EventItemPickup>(OnItemPickup);

			Logger.LogInformation("Plugin loaded successfully.");
		}

		private HookResult OnGiveNamedItemPre(DynamicHook hook)
		{
			try
			{
				CCSPlayerController? player = GetPlayerFromItemServices(hook.GetParam<CCSPlayer_ItemServices>(0));
				if (player == null || !player.IsValid)
					return HookResult.Continue;

				string classname = hook.GetParam<string>(1);
				if (string.IsNullOrEmpty(classname) || !classname.StartsWith("weapon_"))
					return HookResult.Continue;

				if (IsWeaponKnife(classname))
					return HookResult.Continue;

				CsTeam playerTeam = player.Team;
				CsTeam oppositeTeam = (playerTeam == CsTeam.Terrorist) ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

				bool hasSkinInCurrentTeam = HasPlayerSkinForWeapon(player, classname, playerTeam);

				if (hasSkinInCurrentTeam)
					return HookResult.Continue;

				bool hasSkinInOppositeTeam = HasPlayerSkinForWeapon(player, classname, oppositeTeam);

				if (!hasSkinInOppositeTeam)
					return HookResult.Continue;

				SetPlayerTeam(player, oppositeTeam);

				Server.NextFrame(() =>
				{
					if (player != null && player.IsValid)
						SetPlayerTeam(player, playerTeam);
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in OnGiveNamedItemPre: {ex.Message}");
			}

			return HookResult.Continue;
		}

		private HookResult OnGiveNamedItemPost(DynamicHook hook)
		{
			try
			{
				CCSPlayerController? player = GetPlayerFromItemServices(hook.GetParam<CCSPlayer_ItemServices>(0));
				if (player == null || !player.IsValid || player.IsBot)
					return HookResult.Continue;

				string classname = hook.GetParam<string>(1);
				if (string.IsNullOrEmpty(classname) || !classname.StartsWith("weapon_"))
					return HookResult.Continue;

				string lockKey = $"{player.SteamID}_{classname}";
				PickupLocks.Remove(lockKey);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in OnGiveNamedItemPost: {ex.Message}");
			}

			return HookResult.Continue;
		}

		private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
		{
			try
			{
				if (!Config.ApplyToMapWeapons)
					return HookResult.Continue;

				CCSPlayerController? player = @event.Userid;
				if (player == null || !player.IsValid || player.IsBot || player.PlayerPawn.Value?.WeaponServices == null)
					return HookResult.Continue;

				if (string.IsNullOrEmpty(@event.Item))
					return HookResult.Continue;

				string lockKey = $"{player.SteamID}_{@event.Item}";
				if (PickupLocks.Contains(lockKey))
					return HookResult.Continue;

				PickupLocks.Add(lockKey);

				List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];
				foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
				{
					if (!weapon.IsValid || weapon.Value == null)
						continue;

					CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();
					if (ccsWeaponBase == null || !ccsWeaponBase.IsValid)
						continue;

					if (ccsWeaponBase.AttributeManager.Item.ItemDefinitionIndex != @event.Defindex)
						continue;

					var prevOwner = ccsWeaponBase.PrevOwner.Value?.OriginalController?.Value;
					if (prevOwner == player)
						continue;

					bool shouldApply = (prevOwner != null && Config.ApplyOnPreviousOwner) || (prevOwner == null && Config.ApplyOnNoPreviousOwner);
					if (!shouldApply)
						continue;

					string weaponName = ccsWeaponBase.DesignerName;

					var playerWeapons = GetOrCreateDictionary(SavedWeapons, player);
					playerWeapons[weaponName] = new Tuple<int, int, int, int>(ccsWeaponBase.Clip1, ccsWeaponBase.Clip2, ccsWeaponBase.ReserveAmmo[0], ccsWeaponBase.ReserveAmmo[1]);

					Server.NextFrame(() =>
					{
						if (!player.IsValid)
							return;

						weapon.Value?.AddEntityIOEvent("Kill", weapon.Value, null, "", 0.0f);
						player.GiveNamedItem(weaponName);

						Server.NextFrame(() =>
						{
							if (player.IsValid)
							{
								RestoreWeaponAmmo(player, weaponName);
							}
						});
					});

					break;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in OnItemPickup: {ex.Message}");
			}

			return HookResult.Continue;
		}

		private void RestoreWeaponAmmo(CCSPlayerController player, string weaponName)
		{
			try
			{
				if (!player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
					return;

				var savedWeapons = GetOrCreateDictionary(SavedWeapons, player);
				if (!savedWeapons.TryGetValue(weaponName, out var ammoData))
					return;

				List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];
				foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
				{
					if (!weapon.IsValid || weapon.Value == null)
						continue;

					CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();
					if (ccsWeaponBase == null || !ccsWeaponBase.IsValid || ccsWeaponBase.DesignerName != weaponName)
						continue;

					ccsWeaponBase.Clip1 = ammoData.Item1;
					ccsWeaponBase.Clip2 = ammoData.Item2;
					ccsWeaponBase.ReserveAmmo[0] = ammoData.Item3;
					ccsWeaponBase.ReserveAmmo[1] = ammoData.Item4;
					break;
				}

				savedWeapons.Remove(weaponName);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error restoring weapon ammo: {ex.Message}");
			}
		}

		private static Dictionary<TKey, TValue> GetOrCreateDictionary<TKey, TValue>(Dictionary<CCSPlayerController, Dictionary<TKey, TValue>> dict, CCSPlayerController player) where TKey : notnull
		{
			if (!dict.TryGetValue(player, out var innerDict))
			{
				innerDict = [];
				dict[player] = innerDict;
			}

			return innerDict;
		}

		private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			if (itemServices?.Pawn?.Value is not CBasePlayerPawn pawn || !pawn.IsValid || pawn.Controller == null || !pawn.Controller.IsValid || pawn.Controller.Value == null)
				return null;

			var player = new CCSPlayerController(pawn.Controller.Value.Handle);
			return player.IsValid && !player.IsBot && !player.IsHLTV && player.Connected == PlayerConnectedState.PlayerConnected ? player : null;
		}

		public override void Unload(bool hotReload)
		{
			VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPre, HookMode.Pre);
			VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
			DeregisterEventHandler<EventItemPickup>(OnItemPickup);
		}

		private unsafe bool HasPlayerSkinForWeapon(CCSPlayerController player, string weaponName, CsTeam team)
		{
			try
			{
				if (FindMatchingWeaponsForTeamLoadout != null && player.PlayerPawn.Value != null)
				{
					CsTeam originalTeam = player.Team;
					bool teamChanged = false;

					try
					{
						if (originalTeam != team)
						{
							SetPlayerTeam(player, team);
							teamChanged = true;
						}

						nint vectorPtr = CUtlVector<CEconItemView>.CreateVector(16);

						try
						{
							FindMatchingWeaponsForTeamLoadout.Invoke(player.PlayerPawn.Value.Handle, weaponName, (int)team, false, vectorPtr);

							int count = CUtlVector<CEconItemView>.GetVectorCount(vectorPtr);

							bool hasValidSkin = false;
							if (count > 0)
							{
								nint firstElement = CUtlVector<CEconItemView>.GetVectorElement(vectorPtr, 0);

								if (firstElement != IntPtr.Zero)
								{
									try
									{
										var econItem = new CEconItemView(firstElement);

										ulong itemId = econItem.ItemID;
										hasValidSkin = itemId > 0;
									}
									catch (Exception ex)
									{
										hasValidSkin = false;
										Logger.LogError($"Error processing CEconItemView: {ex.Message}");
									}
								}

								return hasValidSkin;
							}
						}
						finally
						{
							CUtlVector<CEconItemView>.FreeVector(vectorPtr);
						}
					}
					finally
					{
						if (teamChanged)
						{
							SetPlayerTeam(player, originalTeam);
						}
					}
				}

				return false;
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error checking skins for weapon: {ex.Message}");
				return false;
			}
		}

		private static bool IsWeaponKnife(string classname)
			=> classname.Contains("knife") || classname.Contains("bayonet");

		private static void SetPlayerTeam(CCSPlayerController player, CsTeam team)
		{
			if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
				return;

			player.TeamNum = (byte)team;

			if (player.PlayerPawn.Value != null && player.PlayerPawn.Value.IsValid)
			{
				player.PlayerPawn.Value.TeamNum = (byte)team;
			}
		}
	}
}