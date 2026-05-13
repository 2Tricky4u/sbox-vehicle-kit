using Sandbox;

namespace Vehicles.Maintenance;

/// <summary>World-pickup for parts. Walk into the trigger → part is added to
/// the picker's inventory via IVehicleHost. Useful for delivery missions or
/// shop-floor pickups; gamemodes may also instantiate parts from a shop UI directly.</summary>
[Title( "Part Item Pickup" )]
[Category( "Vehicles" )]
[Icon( "inventory_2" )]
public sealed class PartItemPickup : Component, Component.ITriggerListener
{
	[Property] public PartDefinition Part { get; set; }
	[Property] public int Count { get; set; } = 1;

	public void OnTriggerEnter( GameObject other )
	{
		if ( VehicleHost.Current is null || Part is null ) return;

		var conn = other.Network?.Owner;
		if ( conn is null ) return;

		var inv = VehicleHost.Current.GetInventory( conn );
		if ( inv is null ) return;

		inv.Add( Part, Count );
		GameObject.Destroy();
	}

	public void OnTriggerExit( GameObject other ) { }
}
