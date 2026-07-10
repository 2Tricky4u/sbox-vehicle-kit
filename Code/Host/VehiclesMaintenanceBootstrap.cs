using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Drop on a GameObject in your startup scene. Registers a stub IVehicleHost
/// so the library can run end-to-end without a real DarkRP backend wired up,
/// and (optionally) logs all VehicleEvents to the console for sanity checks.
/// </summary>
[Title( "Vehicles.Maintenance Bootstrap" )]
[Category( "Vehicles" )]
[Icon( "play_arrow" )]
public sealed class VehiclesMaintenanceBootstrap : Component
{
	[Property] public bool LogEvents { get; set; } = true;

	protected override void OnAwake()
	{
		if ( VehicleHost.Current is null )
			VehicleHost.Register( new CarMaintenanceVehicleHost() );
		else
			Log.Info( "[Bootstrap] VehicleHost already registered — skipping registration." );
	}

	// Instance handlers subscribed per-enable and unsubscribed per-disable:
	// the VehicleEvents subscriber list is static and outlives play sessions,
	// so lambda subscriptions accumulate and hotload turns them into throwing
	// error stubs. Named instance methods paired with -= stay clean.
	protected override void OnEnabled()
	{
		if ( !LogEvents ) return;
		VehicleEvents.OnVehicleSpawned += LogSpawned;
		VehicleEvents.OnVehicleDestroyed += LogDestroyed;
		VehicleEvents.OnRefuel += LogRefuel;
		VehicleEvents.OnRepair += LogRepair;
		VehicleEvents.OnDamage += LogDamage;
		VehicleEvents.OnVehicleWrecked += LogWrecked;
		VehicleEvents.OnVehicleStuck += LogStuck;
	}

	protected override void OnDisabled()
	{
		VehicleEvents.OnVehicleSpawned -= LogSpawned;
		VehicleEvents.OnVehicleDestroyed -= LogDestroyed;
		VehicleEvents.OnRefuel -= LogRefuel;
		VehicleEvents.OnRepair -= LogRepair;
		VehicleEvents.OnDamage -= LogDamage;
		VehicleEvents.OnVehicleWrecked -= LogWrecked;
		VehicleEvents.OnVehicleStuck -= LogStuck;
	}

	void LogSpawned( VehicleBase v ) => Log.Info( $"[Vehicles] Spawned   {NameOf( v )}" );
	void LogDestroyed( VehicleBase v ) => Log.Info( $"[Vehicles] Destroyed {NameOf( v )}" );
	void LogRefuel( VehicleBase v, float l ) => Log.Info( $"[Vehicles] Refuel +{l:F1}L  on {NameOf( v )}" );
	void LogRepair( VehicleBase v, PartKind p, float a ) => Log.Info( $"[Vehicles] Repair {p,-7} +{a:F1}  on {NameOf( v )}" );
	void LogDamage( VehicleBase v, PartKind p, float a ) => Log.Info( $"[Vehicles] Damage {p,-7} -{a:F1}  on {NameOf( v )}" );
	void LogWrecked( VehicleBase v ) => Log.Info( $"[Vehicles] WRECKED   {NameOf( v )} — repair body above {VehicleBase.WreckRecoveryPct:P0} to recover" );

	void LogStuck( VehicleBase v )
	{
		Log.Info( $"[Vehicles] STUCK     {NameOf( v )} — flipped; vh.unstuck (or RecoverUprightRpc) rights it" );
		Toast.Show( "Vehicle stuck — use vh.unstuck to flip it back" );
	}

	static string NameOf( VehicleBase v ) => v?.Config?.DisplayName ?? "(no config)";
}
