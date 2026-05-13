using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>Per-player in-memory parts bag. Used by the test host adapter.</summary>
public sealed class InMemoryPartInventory : IPartInventory
{
	readonly Dictionary<PartDefinition, int> _counts = new();

	public int CountOf( PartDefinition part )
	{
		if ( part is null ) return 0;
		return _counts.TryGetValue( part, out var n ) ? n : 0;
	}

	public bool TryConsume( PartDefinition part, int count )
	{
		if ( part is null || count <= 0 ) return false;
		var have = CountOf( part );
		if ( have < count ) return false;
		_counts[part] = have - count;
		return true;
	}

	public void Add( PartDefinition part, int count )
	{
		if ( part is null || count <= 0 ) return;
		_counts[part] = CountOf( part ) + count;
	}
}
