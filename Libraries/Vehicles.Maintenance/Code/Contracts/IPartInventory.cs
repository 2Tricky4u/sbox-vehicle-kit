namespace Vehicles.Maintenance;

/// <summary>Per-player part inventory adapter. The library asks the gamemode for
/// the player's inventory via IVehicleHost.GetInventory, then queries / consumes.</summary>
public interface IPartInventory
{
	int CountOf( PartDefinition part );
	bool TryConsume( PartDefinition part, int count );
	void Add( PartDefinition part, int count );
}
