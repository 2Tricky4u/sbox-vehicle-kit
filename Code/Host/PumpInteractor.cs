using Sandbox;
using System.Linq;
using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Dev/test glue for <see cref="FuelPump"/>: put this on the Player GameObject
/// (next to <see cref="SeatInteractor"/>). Pressing <c>use</c> (E) near a pump
/// refuels the nearest vehicle in the pump's range and charges the player via
/// the host. Skips entirely while seated so it never fights seat enter/exit;
/// keep pumps a car-length from parking spots so E stays unambiguous.
/// A real gamemode replaces this with its own interaction system.
/// </summary>
[Title( "Pump Interactor (dev)" )]
[Category( "Vehicles" )]
[Icon( "local_gas_station" )]
public sealed class PumpInteractor : Component
{
	protected override void OnUpdate()
	{
		if ( !Input.Pressed( "use" ) ) return;

		// Seated players are exiting a seat, not refueling.
		var seatInteractor = GetComponent<SeatInteractor>();
		if ( seatInteractor?.IsSeated == true ) return;

		var origin = WorldPosition;
		var pump = Scene.GetAllComponents<FuelPump>()
			.Where( p => p.IsValid() )
			.OrderBy( p => Vector3.DistanceBetween( p.WorldPosition, origin ) )
			.FirstOrDefault();
		if ( pump is null ) return;
		if ( Vector3.DistanceBetween( pump.WorldPosition, origin ) > pump.UseRange ) return;

		var vehicle = Scene.GetAllComponents<VehicleBase>()
			.Where( v => v.IsValid() && v.Config is not null )
			.OrderBy( v => Vector3.DistanceBetween( v.WorldPosition, pump.WorldPosition ) )
			.FirstOrDefault();
		if ( vehicle is null )
		{
			Toast.Show( "No vehicle at the pump" );
			return;
		}

		if ( pump.TryUse( vehicle, GameObject.Network?.Owner ?? Connection.Local ) )
			Toast.Show( $"Refueled +{pump.LitresPerUse:F0}L (${(int)(pump.LitresPerUse * pump.PricePerLitre)})" );
		else
			Toast.Show( "Can't refuel (out of range, full, or can't afford)" );
	}
}
