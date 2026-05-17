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

		// Part-select UI hotkey (separate from the repair action).
		if ( Input.Pressed( "flashlight" ) )
		{
			PartSelectPanel.Toggle( this );
			return;
		}

		if ( !Input.Pressed( "attack1" ) ) return;

		var cam = Scene.Camera;
		if ( cam is null ) return;

		var tr = Scene.Trace
			.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * UseRange )
			.IgnoreGameObject( GameObject )
			.Run();

		if ( !tr.Hit ) return;

		var vehicle = tr.GameObject?.Components.GetInAncestorsOrSelf<VehicleBase>();
		if ( vehicle is null ) return;

		// No part chosen yet → open the selector instead of doing nothing.
		if ( CurrentPart is null )
		{
			PartSelectPanel.Toggle( this );
			return;
		}

		// Single shared pipeline (also used by DiagnosticPanel).
		var conn = GameObject.Network.Owner;
		var res = RepairFlow.TryRepair( vehicle, conn, CurrentPart, LabourPayoutPerRepair );

		switch ( res.Outcome )
		{
			case RepairOutcome.Repaired:
				Log.Info( $"[RepairTool] Repaired {res.Part} (+{res.Def.RepairAmount:F0}, paid ${res.Payout})" );
				Toast.Show( $"Repaired {res.Part} +{res.Def.RepairAmount:F0} (+${res.Payout})" );
				break;
			case RepairOutcome.CustomActionRan:
				Log.Info( $"[RepairTool] Custom {res.Part} repair invoked" );
				Toast.Show( $"Custom {res.Part} repair" );
				break;
			case RepairOutcome.OutOfParts:
				Log.Warning( $"[RepairTool] Out of {res.Def?.DisplayName ?? res.Part.ToString()}" );
				Toast.Show( $"Out of {res.Def?.DisplayName ?? res.Part.ToString()}" );
				break;
			case RepairOutcome.NotMechanic:
				Log.Warning( "[RepairTool] Mechanic job required" );
				Toast.Show( "Mechanic job required" );
				break;
			default:
				Log.Info( $"[RepairTool] Repair failed: {res.Outcome}" );
				Toast.Show( $"Can't repair: {res.Outcome}" );
				break;
		}
	}
}
