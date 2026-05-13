using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Vehicles.Maintenance;

/// <summary>
/// Declarative definition for a maintainable vehicle. Schema is locked at v1 —
/// any extra per-gamemode fields go via ResourceExtension&lt;VehicleConfig, ...&gt;.
/// </summary>
[AssetType( Name = "Vehicle Config", Extension = "vcfg", Category = "Vehicles" )]
[Icon( "directions_car" )]
public sealed class VehicleConfig : GameResource
{
	// ─── Identity ─────────────────────────────────────────────────────
	[Property] public string DisplayName { get; set; } = "Sedan";
	[Property, ResourceType( "vmdl" )]   public string ModelPath  { get; set; }
	[Property, ResourceType( "prefab" )] public string PrefabPath { get; set; }

	// ─── Performance ──────────────────────────────────────────────────
	[Group( "Performance" ), Property, Range( 20, 400 )]
	public float MaxSpeedKmh { get; set; } = 140;

	[Group( "Performance" ), Property]
	public float MassKg { get; set; } = 1400;

	[Group( "Performance" ), Property, Range( 0, 1 )]
	public float Grip { get; set; } = 0.85f;

	[Group( "Performance" ), Property]
	public float EngineTorqueNm { get; set; } = 250;

	[Group( "Performance" ), Property, Range( 0, 5 )]
	public float BrakeStrength { get; set; } = 1.0f;

	[Group( "Performance" ), Property]
	public Curve AccelerationCurve { get; set; } = new Curve( new[]
	{
		new Curve.Frame( 0f, 1.0f ),
		new Curve.Frame( 0.5f, 0.7f ),
		new Curve.Frame( 1f, 0.0f ),
	} );

	// ─── Capacity ─────────────────────────────────────────────────────
	[Group( "Capacity" ), Property, Range( 2, 8 )]
	public int WheelCount { get; set; } = 4;

	[Group( "Capacity" ), Property, Range( 1, 16 )]
	public int SeatCount { get; set; } = 4;

	[Group( "Capacity" ), Property]
	public float CargoCapacityKg { get; set; } = 0;

	// ─── Maintenance ──────────────────────────────────────────────────
	[Group( "Maintenance" ), Property]
	public float FuelCapacityLitres { get; set; } = 50;

	[Group( "Maintenance" ), Property]
	public float FuelConsumptionLPer100Km { get; set; } = 7.5f;

	[Group( "Maintenance" ), Property]
	public float EngineMaxHealth { get; set; } = 100;

	[Group( "Maintenance" ), Property]
	public float BodyMaxHealth { get; set; } = 100;

	// ─── Audio ────────────────────────────────────────────────────────
	[Group( "Audio" ), Property, ResourceType( "sound" )]
	public string EngineSoundPath { get; set; }

	[Group( "Audio" ), Property, ResourceType( "sound" )]
	public string HornSoundPath { get; set; }

	// ─── Cosmetics ────────────────────────────────────────────────────
	[Group( "Cosmetics" ), Property]
	public bool PaintTintable { get; set; } = true;

	/// <summary>Free-form labels — gamemodes filter on these
	/// (e.g. "civilian", "police", "luxury", "starter").</summary>
	[Group( "Cosmetics" ), Property]
	public List<string> Tags { get; set; } = new();

	// ─── Economy ──────────────────────────────────────────────────────
	[Group( "Economy" ), Property]
	public int PurchasePrice { get; set; } = 5000;

	[Group( "Economy" ), Property]
	public int RepairBaseCost { get; set; } = 50;

	// ─── Lookup ───────────────────────────────────────────────────────
	public static VehicleConfig Find( string ident ) =>
		ResourceLibrary.GetAll<VehicleConfig>()
			.FirstOrDefault( v => v.ResourceName == ident );

	public static IEnumerable<VehicleConfig> WithTag( string tag ) =>
		ResourceLibrary.GetAll<VehicleConfig>()
			.Where( v => v.Tags != null && v.Tags.Contains( tag ) );
}
