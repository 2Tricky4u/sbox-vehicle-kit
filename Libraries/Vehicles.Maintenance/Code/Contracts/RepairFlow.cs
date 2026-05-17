using Sandbox;

namespace Vehicles.Maintenance;

/// <summary>Outcome of a <see cref="RepairFlow.TryRepair(VehicleBase, Connection, PartKind, int, int)"/> attempt.</summary>
public enum RepairOutcome
{
	NoVehicle,
	NoHost,
	NoConnection,
	NotMechanic,
	NoPartDef,
	NoInventory,
	OutOfParts,
	CustomActionRan,
	Repaired,
}

/// <summary>Result of a repair attempt — callers map this to UI/feedback.</summary>
public readonly struct RepairResult
{
	public RepairOutcome Outcome { get; init; }
	public PartKind Part { get; init; }
	public PartDefinition Def { get; init; }
	public int WheelIndex { get; init; }   // -1 = not a tyre
	public int Payout { get; init; }

	public bool Success => Outcome is RepairOutcome.Repaired or RepairOutcome.CustomActionRan;
}

/// <summary>
/// THE repair pipeline. Single source of truth for: precheck (host / mechanic)
/// → gamemode <see cref="RepairActionRegistry"/> override → consume part from
/// inventory → <c>VehicleBase.RepairRpc</c> → pay the mechanic. Both the
/// RepairTool and the DiagnosticPanel call this so the sequence exists once.
/// Behaviour is identical to the previous inlined copies — only consolidated.
/// </summary>
public static class RepairFlow
{
	/// <summary>Repair by <see cref="PartKind"/>; the <see cref="PartDefinition"/>
	/// is resolved via <see cref="PartDefinition.Find"/>.</summary>
	public static RepairResult TryRepair( VehicleBase vehicle, Connection mechanic, PartKind part, int payout, int wheelIndex = -1 )
		=> Run( vehicle, mechanic, PartDefinition.Find( part ), part, payout, wheelIndex );

	/// <summary>Repair using an explicit <see cref="PartDefinition"/> (RepairTool path).</summary>
	public static RepairResult TryRepair( VehicleBase vehicle, Connection mechanic, PartDefinition def, int payout, int wheelIndex = -1 )
		=> Run( vehicle, mechanic, def, def?.RepairsPart ?? PartKind.Engine, payout, wheelIndex );

	static RepairResult Run( VehicleBase vehicle, Connection mechanic, PartDefinition def, PartKind part, int payout, int wheelIndex )
	{
		RepairResult R( RepairOutcome o ) => new()
		{
			Outcome = o,
			Part = part,
			Def = def,
			WheelIndex = wheelIndex,
			Payout = payout,
		};

		if ( vehicle is null ) return R( RepairOutcome.NoVehicle );
		if ( VehicleHost.Current is null ) return R( RepairOutcome.NoHost );

		// Default to the local connection if the caller didn't specify one.
		mechanic ??= Connection.Local;
		if ( mechanic is null ) return R( RepairOutcome.NoConnection );
		if ( !VehicleHost.Current.IsMechanic( mechanic ) ) return R( RepairOutcome.NotMechanic );

		if ( def is null ) return R( RepairOutcome.NoPartDef );

		// Gamemode-registered custom action takes precedence over the default
		// flow (e.g. a minigame or a different pricing model).
		if ( RepairActionRegistry.TryInvoke( part, vehicle, mechanic ) )
			return R( RepairOutcome.CustomActionRan );

		var inv = VehicleHost.Current.GetInventory( mechanic );
		if ( inv is null ) return R( RepairOutcome.NoInventory );
		if ( !inv.TryConsume( def, 1 ) ) return R( RepairOutcome.OutOfParts );

		vehicle.RepairRpc( part, def.RepairAmount, wheelIndex );
		VehicleHost.Current.Pay( mechanic, payout,
			wheelIndex >= 0 ? $"Repair tire {wheelIndex}" : $"Repair {part}" );

		return R( RepairOutcome.Repaired );
	}
}
