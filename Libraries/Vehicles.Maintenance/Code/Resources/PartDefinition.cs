using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Vehicles.Maintenance;

/// <summary>A purchasable repair part SKU. Authors create one .partdef per part type.</summary>
[AssetType( Name = "Part Definition", Extension = "partdef", Category = "Vehicles" )]
[Icon( "build" )]
public sealed class PartDefinition : GameResource
{
	[Property] public string DisplayName { get; set; } = "Engine Kit";

	[Property] public PartKind RepairsPart { get; set; } = PartKind.Engine;

	[Property, Range( 1, 200 )]
	public float RepairAmount { get; set; } = 50f;

	[Property] public int Price { get; set; } = 200;

	[Property, ResourceType( "vmdl" )]
	public string ModelPath { get; set; }

	[Property, ResourceType( "png" )]
	public string IconPath { get; set; }

	/// <summary>All real .partdef assets — defensive filter against engine
	/// `core/cfg/*.cfg` false-positives, same pattern as VehicleConfig.All.
	/// `.partdef` doesn't currently suffix-collide with anything, but the
	/// filter is cheap and future-proofs against new engine resource types.</summary>
	public static IEnumerable<PartDefinition> All =>
		ResourceLibrary.GetAll<PartDefinition>()
			.Where( p => p is not null
				&& !string.IsNullOrEmpty( p.ResourcePath )
				&& !p.ResourcePath.StartsWith( "cfg/", System.StringComparison.OrdinalIgnoreCase ) );

	/// <summary>Find the first part definition that repairs the given PartKind.
	/// Returns null if none exists.</summary>
	public static PartDefinition Find( PartKind part ) =>
		All.FirstOrDefault( p => p.RepairsPart == part );

	public static PartDefinition FindByIdent( string ident ) =>
		All.FirstOrDefault( p => p.ResourceName == ident || p.DisplayName == ident );
}
