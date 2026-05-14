using Sandbox;

namespace Vehicles.Maintenance;

/// <summary>Held tool. Aim at a vehicle, primary fire applies the currently
/// selected PartDefinition. Mechanic-job-gated via IVehicleHost.</summary>
[Title( "Repair Tool" )]
[Category( "Vehicles" )]
[Icon( "build" )]
public sealed class RepairTool : Component
{
	[Property, Range( 20, 300 )]
	public float UseRange { get; set; } = 100f;

	[Property] public PartDefinition CurrentPart { get; set; }

	[Property] public int LabourPayoutPerRepair { get; set; } = 50;

	protected override void OnUpdate()
	{
		if ( !Network.IsOwner ) return;
		if ( !Input.Pressed( "attack1" ) ) return;

		if ( VehicleHost.Current is null )
		{
			Log.Warning( "RepairTool used but no IVehicleHost is registered." );
			return;
		}

		var conn = GameObject.Network.Owner;
		if ( conn is null ) return;

		if ( !VehicleHost.Current.IsMechanic( conn ) )
		{
			// TODO: surface "Mechanic job required" hint in UI
			return;
		}

		var cam = Scene.Camera;
		if ( cam is null ) return;

		var tr = Scene.Trace
			.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * UseRange )
			.IgnoreGameObject( GameObject )
			.Run();

		if ( !tr.Hit ) return;

		var vehicle = tr.GameObject?.Components.GetInAncestorsOrSelf<VehicleBase>();
		if ( vehicle is null ) return;

		if ( CurrentPart is null )
		{
			// TODO: open part-select UI; for v1 require CurrentPart to be set in inspector
			return;
		}

		// Gamemode-registered custom action takes precedence over the default flow.
		// Example: gamemode wants a minigame for engine repair, or a different
		// pricing model. Registry returns true if it ran the alternative.
		if ( RepairActionRegistry.TryInvoke( CurrentPart.RepairsPart, vehicle, conn ) )
			return;

		var inv = VehicleHost.Current.GetInventory( conn );
		if ( inv is null || !inv.TryConsume( CurrentPart, 1 ) )
		{
			// TODO: surface "out of parts" hint
			return;
		}

		vehicle.RepairRpc( CurrentPart.RepairsPart, CurrentPart.RepairAmount );
		VehicleHost.Current.Pay( conn, LabourPayoutPerRepair, "Repair labour" );
	}
}
