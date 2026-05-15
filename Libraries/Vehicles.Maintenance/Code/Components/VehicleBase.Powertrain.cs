using Sandbox;
using System;

namespace Vehicles.Maintenance;

// "Fake-but-convincing" powertrain — no real clutch, no real differential,
// no per-wheel torque solving. Just a synced RPM + gear, auto-shift logic,
// and per-gear torque multiplier that scales engine force in Wheels.cs.
//
// Gives the player gear numbers, RPM-driven engine sound pitch, shift
// kicks, and acceleration variation between gears — without writing
// a real powertrain simulator.
public sealed partial class VehicleBase
{
	[Sync] public int CurrentGear { get; set; } = 0;     // -1 reverse, 0 neutral, 1..N forward
	[Sync] public float EngineRpm { get; set; } = 0f;

	[Property, Group( "Powertrain" ), Range( 400, 2000 )]
	public float IdleRpm { get; set; } = 800f;

	[Property, Group( "Powertrain" ), Range( 4000, 12000 )]
	public float RedlineRpm { get; set; } = 7000f;

	[Property, Group( "Powertrain" ), Range( 3000, 11000 )]
	public float ShiftUpRpm { get; set; } = 6000f;

	[Property, Group( "Powertrain" ), Range( 500, 5000 )]
	public float ShiftDownRpm { get; set; } = 1500f;

	/// <summary>Auto-applied lock duration (seconds) after every gear change.
	/// Prevents oscillation when RPM smoothing transients cross thresholds.</summary>
	[Property, Group( "Powertrain" ), Range( 0.1f, 2f )]
	public float ShiftCooldown { get; set; } = 0.4f;

	/// <summary>Per-gear torque multipliers (1st gear strongest, last gear weakest).
	/// Engine force in Wheels.cs scales by ForwardGearTorqueMultipliers[CurrentGear-1].</summary>
	[Property, Group( "Powertrain" )]
	public float[] ForwardGearTorqueMultipliers { get; set; } = { 2.5f, 1.8f, 1.3f, 1.0f, 0.8f };

	[Property, Group( "Powertrain" ), Range( 0.5f, 5f )]
	public float ReverseTorqueMultiplier { get; set; } = 2.0f;

	/// <summary>Reverse force in Newtons applied when throttle is released and gear engaged.
	/// Simulates engine braking — car slows naturally when off the gas.</summary>
	[Property, Group( "Powertrain" ), Range( 0, 5000 )]
	public float EngineBrakingForce { get; set; } = 800f;

	float _prevRpm;

	/// <summary>Real-time-tracked timer (auto-advances with sandbox real time
	/// regardless of dt source). When negative, lock has expired.
	/// Replaces the previous `_shiftLockTimer -= dt` approach which decremented
	/// inconsistently in OnFixedUpdate across different framerates.</summary>
	TimeUntil _shiftAllowedAt = 0f;

	/// <summary>Suppress auto-up/down shifts for the given duration. Used by
	/// dev console (vh.shift) and after every SetGear so back-to-back shifts
	/// can't oscillate when RPM smoothing transients cross thresholds.</summary>
	public void LockShifts( float seconds = 5f )
	{
		// Only extend the lock — don't shorten it if a longer one is already pending.
		var remaining = (float)_shiftAllowedAt;
		if ( seconds > remaining ) _shiftAllowedAt = seconds;
	}

	/// <summary>Multiplier applied to engine force in Wheels.cs.
	/// Returns 0 in neutral, scaled values in forward/reverse gears.</summary>
	public float GearTorqueMultiplier
	{
		get
		{
			if ( CurrentGear == 0 ) return 0f;
			if ( CurrentGear < 0 ) return ReverseTorqueMultiplier;
			var idx = CurrentGear - 1;
			if ( ForwardGearTorqueMultipliers == null || idx >= ForwardGearTorqueMultipliers.Length )
				return 1f;
			return ForwardGearTorqueMultipliers[idx];
		}
	}

	int ForwardGearCount => ForwardGearTorqueMultipliers?.Length ?? 5;

	void TickPowertrain( float dt )
	{
		if ( !IsEngineRunning )
		{
			// Engine off — RPM falls to zero.
			EngineRpm = MathX.Lerp( EngineRpm, 0f, MathX.Clamp( dt * 4f, 0f, 1f ) );
			if ( EngineRpm < 50f && CurrentGear != 0 ) SetGear( 0 );
			return;
		}

		var speedMs = ForwardSpeedMs();
		var speedKmh = MathF.Abs( speedMs * 3.6f );
		var maxKmh = Config?.MaxSpeedKmh ?? 140f;

		// ── Auto-shift between forward / reverse / neutral based on speed + intent ──
		if ( CurrentGear == 0 )
		{
			if ( ThrottleInput > 0.1f ) SetGear( 1 );
			else if ( ThrottleInput < -0.1f && speedMs <= 0.5f ) SetGear( -1 );
		}
		else if ( CurrentGear > 0 && speedMs < -2f && ThrottleInput < -0.1f )
		{
			SetGear( -1 );
		}
		else if ( CurrentGear == -1 && speedMs > 2f && ThrottleInput > 0.1f )
		{
			SetGear( 1 );
		}

		// ── Compute target RPM from speed within current gear ──
		float targetRpm;
		if ( CurrentGear > 0 )
		{
			targetRpm = TargetRpmForGear( CurrentGear, speedKmh );
			// Stationary throttle revving (player can rev at the line)
			if ( speedKmh < 5f && ThrottleInput > 0.1f )
				targetRpm = MathX.Lerp( targetRpm, RedlineRpm * 0.7f, ThrottleInput );
		}
		else if ( CurrentGear < 0 )
		{
			var revMaxKmh = maxKmh * 0.5f;
			var t = MathX.Clamp( speedKmh / MathF.Max( 1f, revMaxKmh ), 0f, 1f );
			targetRpm = MathX.Lerp( IdleRpm, RedlineRpm * 0.85f, t );
			if ( speedKmh < 5f && ThrottleInput < -0.1f )
				targetRpm = MathX.Lerp( targetRpm, RedlineRpm * 0.6f, -ThrottleInput );
		}
		else // neutral
		{
			targetRpm = IdleRpm + MathF.Abs( ThrottleInput ) * (RedlineRpm * 0.5f - IdleRpm);
		}

		// Smooth RPM toward target (faster on rise than fall — engines spool quick)
		var rpmRate = (targetRpm > EngineRpm) ? 12f : 6f;
		EngineRpm = MathX.Lerp( EngineRpm, targetRpm, MathX.Clamp( rpmRate * dt, 0f, 1f ) );

		// ── Auto-shift up/down based on RPM (skipped while locked) ──
		// _shiftAllowedAt < 0 means the cooldown set by the previous SetGear has expired.
		if ( _shiftAllowedAt < 0f )
		{
			if ( CurrentGear > 0 && CurrentGear < ForwardGearCount && EngineRpm > ShiftUpRpm )
				SetGear( CurrentGear + 1 );
			else if ( CurrentGear > 1 && EngineRpm < ShiftDownRpm )
				SetGear( CurrentGear - 1 );
		}

		// ── Redline event (one-shot when crossing) ──
		if ( EngineRpm >= RedlineRpm && _prevRpm < RedlineRpm )
			VehicleEvents.RaiseEngineRpmRedlined( this );
		_prevRpm = EngineRpm;
	}

	/// <summary>Public so dev console / admin tools can force a gear. Each shift
	/// auto-locks for <see cref="ShiftCooldown"/> seconds AND snaps RPM to the
	/// new gear's natural value at current speed — avoids the transient lerp
	/// dipping across ShiftDown/ShiftUp thresholds and re-triggering a shift.</summary>
	public void SetGear( int g )
	{
		if ( g == CurrentGear ) return;
		var old = CurrentGear;
		CurrentGear = g;
		// Snap RPM so the smoothing doesn't dip below ShiftDown right after upshift.
		var speedKmh = MathF.Abs( ForwardSpeedMs() * 3.6f );
		EngineRpm = TargetRpmForGear( g, speedKmh );
		LockShifts( ShiftCooldown );
		VehicleEvents.RaiseShifted( this, old, g );
	}

	/// <summary>RPM the engine should naturally show in the given gear at the
	/// given speed. Pulled out of TickPowertrain so SetGear can pre-snap to it.
	/// Wider overlap (0.5 multiplier) than before to avoid borderline cases.</summary>
	float TargetRpmForGear( int gear, float speedKmh )
	{
		if ( gear <= 0 || Config is null ) return IdleRpm;
		var maxKmh = Config.MaxSpeedKmh > 0 ? Config.MaxSpeedKmh : 140f;
		var gearN = MathF.Max( 1f, ForwardGearCount );
		var gearMaxKmh = maxKmh * (gear / gearN);
		// 0.5 (was 0.7) — more overlap means each gear has more room before
		// crossing the next gear's territory; less likely to oscillate.
		var gearMinKmh = (gear > 1) ? maxKmh * ((gear - 1) / gearN) * 0.5f : 0f;
		var range = MathF.Max( 1f, gearMaxKmh - gearMinKmh );
		var t = MathX.Clamp( (speedKmh - gearMinKmh) / range, 0f, 1.05f );
		return MathX.Lerp( IdleRpm, RedlineRpm, t );
	}
}
