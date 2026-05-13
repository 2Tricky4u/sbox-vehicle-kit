using Sandbox;

namespace Vehicles.Maintenance;

/// <summary>
/// Optional extension point: a gamemode can register custom repair actions for
/// specific PartKinds (e.g. require a minigame, or restore extra state). The
/// default RepairTool flow uses PartDefinition directly; IRepairAction is for
/// gamemodes that want to override behaviour per-part.
/// </summary>
public interface IRepairAction
{
	PartKind Part { get; }
	bool CanRepair( VehicleBase vehicle, Connection mechanic );
	void Apply( VehicleBase vehicle, Connection mechanic );
}
