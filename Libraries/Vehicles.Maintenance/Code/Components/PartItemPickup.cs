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

	/// <summary>Fallback: resolve the part by .partdef resource name when the
	/// Part reference isn't assigned (lets tools/spawners configure pickups
	/// with a plain string).</summary>
	[Property] public string PartIdent { get; set; }

	[Property] public int Count { get; set; } = 1;

	PartDefinition ResolvePart() =>
		Part ?? (string.IsNullOrWhiteSpace( PartIdent ) ? null : PartDefinition.FindByIdent( PartIdent ));

	public void OnTriggerEnter( GameObject other )
	{
		var part = ResolvePart();
		if ( VehicleHost.Current is null || part is null ) return;

		// The collider that touched us may be a child of the player rig; walk
		// to the root. Networked play resolves the owner; solo/editor play has
		// no owner on the pawn, so fall back to the local connection — but only
		// for objects that are actually a player (props shouldn't loot parts).
		var root = other?.Root ?? other;
		if ( root is null ) return;

		var conn = root.Network?.Owner;
		if ( conn is null && root.Components.GetInDescendantsOrSelf<PlayerController>() is not null )
			conn = Connection.Local;
		if ( conn is null ) return;

		var inv = VehicleHost.Current.GetInventory( conn );
		if ( inv is null ) return;

		inv.Add( part, Count );
		GameObject.Destroy();
	}

	public void OnTriggerExit( GameObject other ) { }
}
