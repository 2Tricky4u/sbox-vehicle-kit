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

	/// <summary>Body health hit zero — the vehicle is a wreck: engine dead,
	/// undrivable until the body is repaired above the recovery threshold.</summary>
	public static event Action<VehicleBase> OnVehicleWrecked;

	/// <summary>Detected upside-down and stationary for a few seconds.
	/// Gamemodes prompt the player (e.g. "hold X to flip") and call
	/// <c>RecoverUprightRpc</c>; the library only detects.</summary>
	public static event Action<VehicleBase> OnVehicleStuck;

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

	// ── Seats ─────────────────────────────────────────────────────────
	public static event Action<VehicleBase, VehicleSeat> OnSeatEntered;
	public static event Action<VehicleBase, VehicleSeat> OnSeatExited;

	// ── Internal raisers ──────────────────────────────────────────────
	// Each subscriber is invoked INDIVIDUALLY and guarded: one throwing
	// subscriber (a gamemode bug, or a stale lambda left behind by editor
	// hotload) must neither break the game code that raised the event nor
	// starve the subscribers registered after it.
	static void Warn( string eventName, Exception e ) =>
		Log.Warning( $"[VehicleEvents] {eventName} subscriber threw: {e.Message}" );

	static void Fire<T>( string name, Action<T> evt, T a )
	{
		if ( evt is null ) return;
		foreach ( var d in evt.GetInvocationList() )
			try { ((Action<T>)d)( a ); } catch ( Exception e ) { Warn( name, e ); }
	}

	static void Fire<T1, T2>( string name, Action<T1, T2> evt, T1 a, T2 b )
	{
		if ( evt is null ) return;
		foreach ( var d in evt.GetInvocationList() )
			try { ((Action<T1, T2>)d)( a, b ); } catch ( Exception e ) { Warn( name, e ); }
	}

	static void Fire<T1, T2, T3>( string name, Action<T1, T2, T3> evt, T1 a, T2 b, T3 c )
	{
		if ( evt is null ) return;
		foreach ( var d in evt.GetInvocationList() )
			try { ((Action<T1, T2, T3>)d)( a, b, c ); } catch ( Exception e ) { Warn( name, e ); }
	}

	internal static void RaiseSpawned( VehicleBase v ) => Fire( nameof( OnVehicleSpawned ), OnVehicleSpawned, v );
	internal static void RaiseDestroyed( VehicleBase v ) => Fire( nameof( OnVehicleDestroyed ), OnVehicleDestroyed, v );
	internal static void RaiseWrecked( VehicleBase v ) => Fire( nameof( OnVehicleWrecked ), OnVehicleWrecked, v );
	internal static void RaiseStuck( VehicleBase v ) => Fire( nameof( OnVehicleStuck ), OnVehicleStuck, v );
	internal static void RaiseRefuel( VehicleBase v, float litres ) => Fire( nameof( OnRefuel ), OnRefuel, v, litres );
	internal static void RaiseRepair( VehicleBase v, PartKind p, float amt ) => Fire( nameof( OnRepair ), OnRepair, v, p, amt );
	internal static void RaiseDamage( VehicleBase v, PartKind p, float amt ) => Fire( nameof( OnDamage ), OnDamage, v, p, amt );
	internal static void RaiseShifted( VehicleBase v, int oldGear, int newGear ) => Fire( nameof( OnShifted ), OnShifted, v, oldGear, newGear );
	internal static void RaiseEngineRpmRedlined( VehicleBase v ) => Fire( nameof( OnEngineRpmRedlined ), OnEngineRpmRedlined, v );
	internal static void RaiseEngineStarted( VehicleBase v ) => Fire( nameof( OnEngineStarted ), OnEngineStarted, v );
	internal static void RaiseEngineStopped( VehicleBase v ) => Fire( nameof( OnEngineStopped ), OnEngineStopped, v );
	internal static void RaiseDoorOpened( VehicleBase v, int idx ) => Fire( nameof( OnDoorOpened ), OnDoorOpened, v, idx );
	internal static void RaiseDoorClosed( VehicleBase v, int idx ) => Fire( nameof( OnDoorClosed ), OnDoorClosed, v, idx );
	internal static void RaiseTirePunctured( VehicleBase v, int wheelIdx ) => Fire( nameof( OnTirePunctured ), OnTirePunctured, v, wheelIdx );
	internal static void RaiseWheelSkidStarted( VehicleBase v, int wheelIdx ) => Fire( nameof( OnWheelSkidStarted ), OnWheelSkidStarted, v, wheelIdx );
	internal static void RaiseWheelSkidStopped( VehicleBase v, int wheelIdx ) => Fire( nameof( OnWheelSkidStopped ), OnWheelSkidStopped, v, wheelIdx );
	internal static void RaiseHorn( VehicleBase v ) => Fire( nameof( OnHorn ), OnHorn, v );
	internal static void RaiseHeadlightsToggled( VehicleBase v, bool on ) => Fire( nameof( OnHeadlightsToggled ), OnHeadlightsToggled, v, on );
	internal static void RaiseSeatEntered( VehicleBase v, VehicleSeat seat ) => Fire( nameof( OnSeatEntered ), OnSeatEntered, v, seat );
	internal static void RaiseSeatExited( VehicleBase v, VehicleSeat seat ) => Fire( nameof( OnSeatExited ), OnSeatExited, v, seat );
}
