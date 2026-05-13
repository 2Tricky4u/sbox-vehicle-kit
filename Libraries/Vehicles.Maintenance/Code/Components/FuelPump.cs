using Sandbox;
using System;

namespace Vehicles.Maintenance;

/// <summary>Static prop component. Players use it on a nearby vehicle they own
/// to refuel — the gamemode's currency adapter is charged.</summary>
[Title( "Fuel Pump" )]
[Category( "Vehicles" )]
[Icon( "local_gas_station" )]
public sealed class FuelPump : Component
{
	[Property, Range( 20, 300 )]
	public float UseRange { get; set; } = 80f;

	[Property] public int PricePerLitre { get; set; } = 2;

	[Property] public float LitresPerUse { get; set; } = 5f;

	/// <summary>Call from your gamemode's "use" interaction handler.
	/// Returns false if the player can't afford it or no vehicle in range.</summary>
	public bool TryUse( VehicleBase vehicle, Connection player )
	{
		if ( vehicle is null || vehicle.Config is null ) return false;
		if ( VehicleHost.Current is null ) return false;

		var distance = Vector3.DistanceBetween( WorldPosition, vehicle.WorldPosition );
		if ( distance > UseRange ) return false;

		var cost = (int)MathF.Ceiling( LitresPerUse * PricePerLitre );
		if ( !VehicleHost.Current.TryCharge( player, cost, "Fuel" ) ) return false;

		vehicle.RefuelRpc( LitresPerUse );
		return true;
	}
}
