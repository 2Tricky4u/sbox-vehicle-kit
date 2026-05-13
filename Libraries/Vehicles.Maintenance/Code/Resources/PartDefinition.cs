using Sandbox;

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
}
