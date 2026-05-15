using Sandbox;
using System;
using System.Text;

namespace Vehicles.Maintenance;

// Debug logging — all output gated on the DebugLog property. Two streams:
//   • Per-second tick: multi-line state snapshot of driving / maintenance / tune / systems.
//   • Event transitions: shift, redline, skid start/stop, puncture, repair, etc.
//     Each transition fires once via VehicleEvents and is logged immediately.
//
// Subscribe in OnStart, unsubscribe in OnDestroy. Handlers filter by (v == this)
// because VehicleEvents is a static bus shared across all vehicles.
public sealed partial class VehicleBase
{
	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	float _debugLogTimer;
	bool _debugSubscribed;

	void DebugSubscribe()
	{
		if ( _debugSubscribed ) return;
		_debugSubscribed = true;

		VehicleEvents.OnShifted += DbgShifted;
		VehicleEvents.OnEngineRpmRedlined += DbgRedlined;
		VehicleEvents.OnEngineStarted += DbgEngineStarted;
		VehicleEvents.OnEngineStopped += DbgEngineStopped;
		VehicleEvents.OnDoorOpened += DbgDoorOpened;
		VehicleEvents.OnDoorClosed += DbgDoorClosed;
		VehicleEvents.OnTirePunctured += DbgTirePunctured;
		VehicleEvents.OnWheelSkidStarted += DbgSkidStarted;
		VehicleEvents.OnWheelSkidStopped += DbgSkidStopped;
		VehicleEvents.OnHorn += DbgHorn;
		VehicleEvents.OnHeadlightsToggled += DbgHeadlights;
		VehicleEvents.OnRefuel += DbgRefuel;
		VehicleEvents.OnRepair += DbgRepair;
		VehicleEvents.OnDamage += DbgDamage;
	}

	void DebugUnsubscribe()
	{
		if ( !_debugSubscribed ) return;
		_debugSubscribed = false;

		VehicleEvents.OnShifted -= DbgShifted;
		VehicleEvents.OnEngineRpmRedlined -= DbgRedlined;
		VehicleEvents.OnEngineStarted -= DbgEngineStarted;
		VehicleEvents.OnEngineStopped -= DbgEngineStopped;
		VehicleEvents.OnDoorOpened -= DbgDoorOpened;
		VehicleEvents.OnDoorClosed -= DbgDoorClosed;
		VehicleEvents.OnTirePunctured -= DbgTirePunctured;
		VehicleEvents.OnWheelSkidStarted -= DbgSkidStarted;
		VehicleEvents.OnWheelSkidStopped -= DbgSkidStopped;
		VehicleEvents.OnHorn -= DbgHorn;
		VehicleEvents.OnHeadlightsToggled -= DbgHeadlights;
		VehicleEvents.OnRefuel -= DbgRefuel;
		VehicleEvents.OnRepair -= DbgRepair;
		VehicleEvents.OnDamage -= DbgDamage;
	}

	bool DbgMine( VehicleBase v ) => v == this && DebugLog;

	void DbgShifted( VehicleBase v, int oldG, int newG )
	{
		if ( !DbgMine( v ) ) return;
		Log.Info( $"[Vehicle] ▸ SHIFT {GearName( oldG )} → {GearName( newG )} @ {EngineRpm:F0}rpm" );
	}

	void DbgRedlined( VehicleBase v )
	{
		if ( !DbgMine( v ) ) return;
		Log.Info( $"[Vehicle] ▸ REDLINE hit ({EngineRpm:F0}rpm)" );
	}

	void DbgEngineStarted( VehicleBase v )
	{
		if ( !DbgMine( v ) ) return;
		Log.Info( "[Vehicle] ▸ ENGINE STARTED" );
	}

	void DbgEngineStopped( VehicleBase v )
	{
		if ( !DbgMine( v ) ) return;
		var why = CanStartEngine ? "by player" : (Fuel <= 0.1f ? "out of fuel" : "engine destroyed");
		Log.Info( $"[Vehicle] ▸ ENGINE STOPPED ({why})" );
	}

	void DbgDoorOpened( VehicleBase v, int idx )  { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ DOOR {idx} opened" ); }
	void DbgDoorClosed( VehicleBase v, int idx )  { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ DOOR {idx} closed" ); }
	void DbgTirePunctured( VehicleBase v, int i ) { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ TIRE {i} PUNCTURED" ); }
	void DbgSkidStarted( VehicleBase v, int i )   { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ wheel {i} SKID start" ); }
	void DbgSkidStopped( VehicleBase v, int i )   { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ wheel {i} skid stop" ); }
	void DbgHorn( VehicleBase v )                 { if ( DbgMine( v ) ) Log.Info( "[Vehicle] ▸ HORN" ); }
	void DbgHeadlights( VehicleBase v, bool on )  { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ HEADLIGHTS {(on ? "on" : "off")}" ); }
	void DbgRefuel( VehicleBase v, float litres ) { if ( DbgMine( v ) ) Log.Info( $"[Vehicle] ▸ REFUEL +{litres:F1}L → {Fuel:F1}L" ); }
	void DbgRepair( VehicleBase v, PartKind p, float a )
	{
		if ( !DbgMine( v ) ) return;
		Log.Info( $"[Vehicle] ▸ REPAIR {p} +{a:F1} (engine={EngineHealth:F0} body={BodyHealth:F0})" );
	}
	void DbgDamage( VehicleBase v, PartKind p, float a )
	{
		if ( !DbgMine( v ) ) return;
		Log.Info( $"[Vehicle] ▸ DAMAGE {p} -{a:F1} (engine={EngineHealth:F0} body={BodyHealth:F0})" );
	}

	static string GearName( int g ) => g switch { -1 => "R", 0 => "N", _ => g.ToString() };

	/// <summary>Per-second state snapshot — driving + maintenance + tune + systems.</summary>
	void DebugTick( float dt, int groundedCount, float engineForceMag )
	{
		if ( !DebugLog ) return;
		_debugLogTimer += dt;
		if ( _debugLogTimer < 1f ) return;
		_debugLogTimer = 0f;

		var spdKmh = MathF.Abs( ForwardSpeedMs() * 3.6f );
		var totalKmh = (Body?.Velocity.Length ?? 0) * 0.0254f * 3.6f;
		var angVel = Body?.AngularVelocity.Length ?? 0;
		// Diagnostic: if Rigidbody has its own LinearDamping set in the inspector,
		// it'll cap velocity independently of our AirDrag. Surface the value here.
		float linDamp = 0f;
		try { linDamp = Body?.LinearDamping ?? 0f; } catch { }

		// Line 1: driving state — including TOTAL speed (Body.Velocity.Length) so
		// we can spot when forward-projected km/h plateaus while total still climbs
		// (means the car is pitching/yawing and the "forward" axis has rotated).
		Log.Info( $"[Vehicle] ── tick ── fwd={spdKmh:F1}km/h  total={totalKmh:F1}km/h  angVel={angVel:F2}  rbDamp={linDamp:F2}  " +
			$"gear={GearName( CurrentGear )} {EngineRpm:F0}rpm  throttle={ThrottleInput:F2} (raw={_rawThrottle:F2})  " +
			$"steer={_currentSteerAngle:F1}° (in={SteerInput:F2})  engine={engineForceMag:F0}N  grounded={groundedCount}/{WheelAnchors.Count}" );

		// Line 2: maintenance state
		var minTireHealth = 1f;
		for ( int i = 0; i < TireWear.Count; i++ )
			minTireHealth = MathF.Min( minTireHealth, 1f - TireWear[i] );
		Log.Info( $"[Vehicle]   maint: fuel={Fuel:F1}L ({FuelPct * 100:F0}%)  engine={EngineHealth:F0}/{Config?.EngineMaxHealth:F0} ({EngineHealthPct * 100:F0}%)  " +
			$"body={BodyHealth:F0}/{Config?.BodyMaxHealth:F0} ({BodyHealthPct * 100:F0}%)  minTire={minTireHealth * 100:F0}%  " +
			$"battery={BatteryPct * 100:F0}%  oil={OilPct * 100:F0}%{(IsLowOil ? " ⚠LOW" : "")}  punctures=0x{TirePunctureMask:X}" );

		// Line 3: tune + effective values (what the wheel sim actually consumes)
		var tuneName = Tune?.PresetName ?? "(none)";
		Log.Info( $"[Vehicle]   tune={tuneName}  effEngine={EffectiveEnginePower:F0}N  effSteer={EffectiveSteerAngleMax:F1}°  " +
			$"effBrake={EffectiveBrake:F0}  effDownforce={EffectiveDownforce:F0}  effGripF={EffectiveWheelGrip( 0 ):F2}  effGripR={EffectiveWheelGrip( WheelAnchors.Count - 1 ):F2}" );

		// Line 4: systems state
		Log.Info( $"[Vehicle]   sys: running={IsEngineRunning}  engineOn={EngineOn}  lights={HeadlightsOn}  doors=0x{DoorMask:X}  " +
			$"factors(engine={EngineHealthFactor:F2} body={BodyHealthFactor:F2})" );
	}
}
