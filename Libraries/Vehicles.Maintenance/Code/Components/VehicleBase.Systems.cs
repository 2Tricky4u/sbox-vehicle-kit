using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Light vehicle systems — engine on/off, doors, headlights, tire punctures, horn.
// All event-driven (gamemode subscribes to VehicleEvents.OnDoorOpened etc. — the
// library never polls these). Maintenance state writes into TirePunctureMask
// automatically when a tire wears out; repair RPC clears the bit.
public sealed partial class VehicleBase
{
	[Sync] public bool EngineOn { get; set; } = true;
	[Sync] public bool HeadlightsOn { get; set; } = false;
	[Sync] public uint DoorMask { get; set; } = 0;
	[Sync] public uint TirePunctureMask { get; set; } = 0;

	bool _prevEngineOn = true;

	/// <summary>The unified gate: engine is mechanically OK AND switched on.
	/// Use this everywhere instead of CanStartEngine when checking "should the engine produce power right now".</summary>
	public bool IsEngineRunning => CanStartEngine && EngineOn;

	// ── Engine on/off ─────────────────────────────────────────────────
	[Rpc.Owner]
	public void ToggleEngineRpc()
	{
		EngineOn = !EngineOn;
		if ( EngineOn ) VehicleEvents.RaiseEngineStarted( this );
		else VehicleEvents.RaiseEngineStopped( this );
	}

	// ── Doors ─────────────────────────────────────────────────────────
	[Rpc.Owner]
	public void ToggleDoorRpc( int doorIdx )
	{
		if ( doorIdx < 0 || doorIdx >= 32 ) return;
		var bit = 1u << doorIdx;
		var wasOpen = (DoorMask & bit) != 0;
		DoorMask = wasOpen ? (DoorMask & ~bit) : (DoorMask | bit);
		if ( wasOpen ) VehicleEvents.RaiseDoorClosed( this, doorIdx );
		else VehicleEvents.RaiseDoorOpened( this, doorIdx );
	}

	public bool IsDoorOpen( int doorIdx ) => doorIdx >= 0 && doorIdx < 32 && (DoorMask & (1u << doorIdx)) != 0;

	// ── Headlights ────────────────────────────────────────────────────
	[Rpc.Owner]
	public void ToggleHeadlightsRpc()
	{
		HeadlightsOn = !HeadlightsOn;
		VehicleEvents.RaiseHeadlightsToggled( this, HeadlightsOn );
	}

	// ── Horn ──────────────────────────────────────────────────────────
	[Rpc.Broadcast]
	public void HornRpc()
	{
		VehicleEvents.RaiseHorn( this );
	}

	// ── Tire punctures ────────────────────────────────────────────────
	[Rpc.Owner]
	public void PunctureTireRpc( int wheelIdx )
	{
		if ( wheelIdx < 0 || wheelIdx >= 32 ) return;
		var bit = 1u << wheelIdx;
		if ( (TirePunctureMask & bit) != 0 ) return; // already punctured
		TirePunctureMask |= bit;
		VehicleEvents.RaiseTirePunctured( this, wheelIdx );
	}

	public bool IsTirePunctured( int wheelIdx ) =>
		wheelIdx >= 0 && wheelIdx < 32 && (TirePunctureMask & (1u << wheelIdx)) != 0;

	// ── Per-tick: keep system state in sync with maintenance state ────
	void TickSystems()
	{
		// Engine off when health crashes.
		if ( EngineOn && !CanStartEngine )
		{
			EngineOn = false;
			VehicleEvents.RaiseEngineStopped( this );
		}
		else if ( !EngineOn && _prevEngineOn != EngineOn )
		{
			VehicleEvents.RaiseEngineStarted( this );
		}
		_prevEngineOn = EngineOn;

		// Auto-puncture tire when wear hits 100% (matches existing TickWear in Damage.cs).
		for ( int i = 0; i < TireWear.Count && i < 32; i++ )
		{
			var bit = 1u << i;
			var wornOut = TireWear[i] >= 1.0f;
			var wasPunctured = (TirePunctureMask & bit) != 0;
			if ( wornOut && !wasPunctured )
			{
				TirePunctureMask |= bit;
				VehicleEvents.RaiseTirePunctured( this, i );
			}
		}
	}
}
