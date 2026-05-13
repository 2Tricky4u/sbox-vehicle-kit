using Sandbox;
using System.Collections.Generic;

namespace Vehicles.Maintenance;

/// <summary>
/// Player-facing tuning preset. Multiplies the per-vehicle base constants
/// (engine power, brake, grip, suspension, downforce, steering) without
/// touching physics math. Ship presets like Sport / Drift / Rally / Heavy
/// for instantly different vehicle feel.
///
/// VehicleBase.Tune.cs reads these multipliers and exposes "Effective*" props
/// that the wheel sim consumes — so a worn-out engine or torn-up tires
/// further scale these values via maintenance state.
/// </summary>
[AssetType( Name = "Vehicle Tune", Extension = "vtune", Category = "Vehicles" )]
[Icon( "tune" )]
public sealed class VehicleTuneProfile : GameResource
{
	[Property] public string PresetName { get; set; } = "Default";

	[Group( "Engine" ), Property, Range( 0.1f, 3f )]
	public float EnginePowerMultiplier { get; set; } = 1.0f;

	[Group( "Engine" ), Property, Range( 0.1f, 3f )]
	public float BrakeMultiplier { get; set; } = 1.0f;

	[Group( "Steering" ), Property, Range( 0.1f, 3f )]
	public float SteeringMultiplier { get; set; } = 1.0f;

	[Group( "Suspension" ), Property, Range( 0.1f, 3f )]
	public float SuspensionStiffnessMultiplier { get; set; } = 1.0f;

	[Group( "Suspension" ), Property, Range( 0.1f, 3f )]
	public float SuspensionDampingMultiplier { get; set; } = 1.0f;

	[Group( "Grip" ), Property, Range( 0.1f, 3f )]
	public float FrontGripMultiplier { get; set; } = 1.0f;

	[Group( "Grip" ), Property, Range( 0.1f, 3f )]
	public float RearGripMultiplier { get; set; } = 1.0f;

	[Group( "Aero" ), Property, Range( 0.1f, 3f )]
	public float DownforceMultiplier { get; set; } = 1.0f;

	/// <summary>Free-form labels — gamemodes filter on these (e.g. "rally", "sport").</summary>
	[Group( "Cosmetics" ), Property]
	public List<string> Tags { get; set; } = new();
}
