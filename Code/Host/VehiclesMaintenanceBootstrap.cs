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
		if ( VehicleHost.Current is not null )
		{
			Log.Info( "[Bootstrap] VehicleHost already registered — skipping." );
			return;
		}

		VehicleHost.Register( new CarMaintenanceVehicleHost() );

		if ( LogEvents )
		{
			VehicleEvents.OnVehicleSpawned   += v => Log.Info( $"[Vehicles] Spawned   {NameOf( v )}" );
			VehicleEvents.OnVehicleDestroyed += v => Log.Info( $"[Vehicles] Destroyed {NameOf( v )}" );
			VehicleEvents.OnRefuel           += ( v, l )    => Log.Info( $"[Vehicles] Refuel +{l:F1}L  on {NameOf( v )}" );
			VehicleEvents.OnRepair           += ( v, p, a ) => Log.Info( $"[Vehicles] Repair {p,-7} +{a:F1}  on {NameOf( v )}" );
			VehicleEvents.OnDamage           += ( v, p, a ) => Log.Info( $"[Vehicles] Damage {p,-7} -{a:F1}  on {NameOf( v )}" );
		}
	}

	static string NameOf( VehicleBase v ) => v?.Config?.DisplayName ?? "(no config)";
}
