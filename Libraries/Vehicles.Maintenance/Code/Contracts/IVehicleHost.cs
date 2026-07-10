using Sandbox;
using System;

namespace Vehicles.Maintenance;

/// <summary>
/// Per-gamemode adapter. Each host gamemode (sousou63 DarkRP, dxura, etc.) implements
/// this once with their own currency/job/inventory/persistence systems and registers
/// the instance via VehicleHost.Register.
/// </summary>
public interface IVehicleHost
{
	// ─── Currency ─────────────────────────────────────────────────────
	bool TryCharge( Connection player, int amount, string reason );
	void Pay( Connection player, int amount, string reason );

	// ─── Jobs ─────────────────────────────────────────────────────────
	bool IsMechanic( Connection player );

	// ─── Inventory ────────────────────────────────────────────────────
	IPartInventory GetInventory( Connection player );

	// ─── Persistence (optional — gamemodes can no-op these) ───────────
	// The library WRITES via SaveVehicleOwnership on every Spawn(owner: …).
	// The library READS via VehiclePersistence.RestoreOwnershipFor(conn) —
	// call that from your player-join hook so reconnecting players regain
	// control of their still-alive vehicles. Respawning vehicles across map
	// restarts is the gamemode's job (persist here, VehicleBase.Spawn at boot).
	void SaveVehicleOwnership( Guid vehicleId, ulong steamId, VehicleConfig cfg );
	bool TryLoadVehicleOwnership( Guid vehicleId, out ulong steamId, out VehicleConfig cfg );
}

/// <summary>Static registration point. The library calls VehicleHost.Current
/// from runtime code; the gamemode calls Register exactly once at startup.</summary>
public static class VehicleHost
{
	public static IVehicleHost Current { get; private set; }

	public static void Register( IVehicleHost host )
	{
		Current = host;
		Log.Info( $"[Vehicles.Maintenance] Host registered: {host?.GetType().Name ?? "null"}" );
	}
}
