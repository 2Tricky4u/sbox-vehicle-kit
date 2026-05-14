using Sandbox;
using System.Collections.Generic;

namespace Vehicles.Maintenance;

/// <summary>
/// Optional extension point: a gamemode can register custom repair actions for
/// specific PartKinds (e.g. require a minigame, restore extra state, charge
/// differently). The default RepairTool flow consumes a PartDefinition from
/// inventory and calls RepairRpc; IRepairAction lets a gamemode replace that
/// per-part flow without modifying library code.
///
/// Register at gamemode startup:
///   RepairActionRegistry.Register( new MyEngineMinigameAction() );
///
/// RepairTool / DiagnosticPanel consult the registry first; if an action is
/// found and CanRepair returns true, Apply runs and the default flow is skipped.
/// </summary>
public interface IRepairAction
{
	PartKind Part { get; }
	bool CanRepair( VehicleBase vehicle, Connection mechanic );
	void Apply( VehicleBase vehicle, Connection mechanic );
}

/// <summary>
/// Static registry for custom IRepairAction implementations. One action per
/// PartKind — latest Register() call wins. Use TryInvoke from consumer code:
///   if ( RepairActionRegistry.TryInvoke( PartKind.Engine, vehicle, mechanic ) ) return;
///   // ... default flow
/// </summary>
public static class RepairActionRegistry
{
	static readonly Dictionary<PartKind, IRepairAction> _map = new();

	public static void Register( IRepairAction action )
	{
		if ( action is null ) return;
		_map[action.Part] = action;
	}

	public static void Unregister( PartKind part ) => _map.Remove( part );

	public static IRepairAction Get( PartKind part ) =>
		_map.TryGetValue( part, out var a ) ? a : null;

	/// <summary>Convenience: if a gamemode action is registered for this part
	/// AND its CanRepair check passes, run it and return true. Caller falls
	/// back to default flow on false.</summary>
	public static bool TryInvoke( PartKind part, VehicleBase vehicle, Connection mechanic )
	{
		var action = Get( part );
		if ( action is null || !action.CanRepair( vehicle, mechanic ) ) return false;
		action.Apply( vehicle, mechanic );
		return true;
	}
}
