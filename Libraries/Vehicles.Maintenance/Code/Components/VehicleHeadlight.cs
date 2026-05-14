using Sandbox;

namespace Vehicles.Maintenance;

/// <summary>
/// Drives a SpotLight (or any toggleable light component) from the parent
/// vehicle's <c>HeadlightsOn</c> synced flag. Place on a child GameObject
/// of the vehicle, drop a SpotLight component on the same GameObject (or
/// reference it via <c>Light</c> property), and it'll toggle automatically
/// when the player runs <c>vh.lights</c> or calls <c>ToggleHeadlightsRpc()</c>.
/// </summary>
[Title( "Vehicle Headlight" )]
[Category( "Vehicles" )]
[Icon( "wb_incandescent" )]
public sealed class VehicleHeadlight : Component
{
	/// <summary>The vehicle this light belongs to. Auto-found in OnAwake by
	/// walking ancestors if not set.</summary>
	[Property] public VehicleBase Vehicle { get; set; }

	/// <summary>The light to toggle. Defaults to a SpotLight on the same GameObject.</summary>
	[Property] public Component Light { get; set; }

	protected override void OnAwake()
	{
		Vehicle ??= Components.GetInAncestorsOrSelf<VehicleBase>();
		Light ??= Components.Get<SpotLight>();
	}

	protected override void OnUpdate()
	{
		// Drive light state directly from the synced flag — no event subscription
		// needed, simpler and recovers cleanly from hotload / network proxy churn.
		if ( Vehicle is null || Light is null ) return;
		if ( Light.Enabled != Vehicle.HeadlightsOn )
			Light.Enabled = Vehicle.HeadlightsOn;
	}
}
