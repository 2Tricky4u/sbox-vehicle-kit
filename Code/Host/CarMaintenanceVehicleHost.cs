using System;
using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Stub IVehicleHost for in-editor testing: every player is a mechanic with
/// infinite money and a per-player in-memory parts inventory. Replace with a
/// real adapter (e.g. SouSou63DarkRPVehicleHost) once a gamemode currency/job
/// system is wired up.
/// </summary>
public sealed class CarMaintenanceVehicleHost : IVehicleHost
{
	readonly Dictionary<SteamId, InMemoryPartInventory> _inventories = new();
	readonly Dictionary<Guid, (ulong steamId, VehicleConfig cfg)> _ownership = new();

	public bool TryCharge( Connection player, int amount, string reason )
	{
		Log.Info( $"[Host] Charge {Name( player )} ${amount} ({reason}) — stub: always succeeds" );
		return true;
	}

	public void Pay( Connection player, int amount, string reason )
	{
		Log.Info( $"[Host] Paid {Name( player )} ${amount} ({reason})" );
	}

	public bool IsMechanic( Connection player ) => true;

	public IPartInventory GetInventory( Connection player )
	{
		if ( player is null ) return null;
		if ( !_inventories.TryGetValue( player.SteamId, out var inv ) )
		{
			inv = new InMemoryPartInventory();
			_inventories[player.SteamId] = inv;
		}
		return inv;
	}

	public void SaveVehicleOwnership( Guid vehicleId, ulong steamId, VehicleConfig cfg )
	{
		_ownership[vehicleId] = (steamId, cfg);
	}

	public bool TryLoadVehicleOwnership( Guid vehicleId, out ulong steamId, out VehicleConfig cfg )
	{
		if ( _ownership.TryGetValue( vehicleId, out var entry ) )
		{
			steamId = entry.steamId;
			cfg = entry.cfg;
			return true;
		}
		steamId = 0;
		cfg = null;
		return false;
	}

	static string Name( Connection p ) => p?.DisplayName ?? "(unknown)";
}
