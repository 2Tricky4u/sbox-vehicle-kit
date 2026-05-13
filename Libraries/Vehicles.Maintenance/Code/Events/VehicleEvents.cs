using System;

namespace Vehicles.Maintenance;

/// <summary>Static event bus the library fires on lifecycle transitions.
/// Gamemodes (and other libraries) subscribe to react — e.g. award mechanic XP,
/// log to a leaderboard, play a global sound, persist state.</summary>
public static class VehicleEvents
{
	// ── Lifecycle ─────────────────────────────────────────────────────
	public static event Action<VehicleBase> OnVehicleSpawned;
	public static event Action<VehicleBase> OnVehicleDestroyed;

	// ── Maintenance ───────────────────────────────────────────────────
	public static event Action<VehicleBase, float> OnRefuel;
	public static event Action<VehicleBase, PartKind, float> OnRepair;
	public static event Action<VehicleBase, PartKind, float> OnDamage;

	// ── Powertrain ────────────────────────────────────────────────────
	public static event Action<VehicleBase, int, int> OnShifted;        // (vehicle, oldGear, newGear)
	public static event Action<VehicleBase> OnEngineRpmRedlined;
	public static event Action<VehicleBase> OnEngineStarted;
	public static event Action<VehicleBase> OnEngineStopped;

	// ── Vehicle systems (Phase D) ─────────────────────────────────────
	public static event Action<VehicleBase, int> OnDoorOpened;          // (vehicle, doorIdx)
	public static event Action<VehicleBase, int> OnDoorClosed;
	public static event Action<VehicleBase, int> OnTirePunctured;       // (vehicle, wheelIdx)
	public static event Action<VehicleBase, int> OnWheelSkidStarted;
	public static event Action<VehicleBase, int> OnWheelSkidStopped;
	public static event Action<VehicleBase> OnHorn;
	public static event Action<VehicleBase, bool> OnHeadlightsToggled;

	// ── Internal raisers ──────────────────────────────────────────────
	internal static void RaiseSpawned( VehicleBase v ) => OnVehicleSpawned?.Invoke( v );
	internal static void RaiseDestroyed( VehicleBase v ) => OnVehicleDestroyed?.Invoke( v );
	internal static void RaiseRefuel( VehicleBase v, float litres ) => OnRefuel?.Invoke( v, litres );
	internal static void RaiseRepair( VehicleBase v, PartKind p, float amt ) => OnRepair?.Invoke( v, p, amt );
	internal static void RaiseDamage( VehicleBase v, PartKind p, float amt ) => OnDamage?.Invoke( v, p, amt );
	internal static void RaiseShifted( VehicleBase v, int oldGear, int newGear ) => OnShifted?.Invoke( v, oldGear, newGear );
	internal static void RaiseEngineRpmRedlined( VehicleBase v ) => OnEngineRpmRedlined?.Invoke( v );
	internal static void RaiseEngineStarted( VehicleBase v ) => OnEngineStarted?.Invoke( v );
	internal static void RaiseEngineStopped( VehicleBase v ) => OnEngineStopped?.Invoke( v );
	internal static void RaiseDoorOpened( VehicleBase v, int idx ) => OnDoorOpened?.Invoke( v, idx );
	internal static void RaiseDoorClosed( VehicleBase v, int idx ) => OnDoorClosed?.Invoke( v, idx );
	internal static void RaiseTirePunctured( VehicleBase v, int wheelIdx ) => OnTirePunctured?.Invoke( v, wheelIdx );
	internal static void RaiseWheelSkidStarted( VehicleBase v, int wheelIdx ) => OnWheelSkidStarted?.Invoke( v, wheelIdx );
	internal static void RaiseWheelSkidStopped( VehicleBase v, int wheelIdx ) => OnWheelSkidStopped?.Invoke( v, wheelIdx );
	internal static void RaiseHorn( VehicleBase v ) => OnHorn?.Invoke( v );
	internal static void RaiseHeadlightsToggled( VehicleBase v, bool on ) => OnHeadlightsToggled?.Invoke( v, on );
}
