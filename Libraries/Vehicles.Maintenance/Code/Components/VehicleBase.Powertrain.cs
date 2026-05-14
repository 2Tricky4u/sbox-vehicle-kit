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

	[Property, Group( "Powertrain" ), Range( 800, 5000 )]
	public float ShiftDownRpm { get; set; } = 2000f;

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
			var gearN = MathF.Max( 1f, ForwardGearCount );
			var gearMaxKmh = maxKmh * (CurrentGear / gearN);
			var gearMinKmh = (CurrentGear > 1) ? maxKmh * ((CurrentGear - 1) / gearN) * 0.7f : 0f;
			var range = MathF.Max( 1f, gearMaxKmh - gearMinKmh );
			var t = MathX.Clamp( (speedKmh - gearMinKmh) / range, 0f, 1.05f );
			targetRpm = MathX.Lerp( IdleRpm, RedlineRpm, t );
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

		// ── Auto-shift up/down based on RPM ──
		if ( CurrentGear > 0 && CurrentGear < ForwardGearCount && EngineRpm > ShiftUpRpm )
			SetGear( CurrentGear + 1 );
		else if ( CurrentGear > 1 && EngineRpm < ShiftDownRpm )
			SetGear( CurrentGear - 1 );

		// ── Redline event (one-shot when crossing) ──
		if ( EngineRpm >= RedlineRpm && _prevRpm < RedlineRpm )
			VehicleEvents.RaiseEngineRpmRedlined( this );
		_prevRpm = EngineRpm;
	}

	/// <summary>Public so dev console / admin tools can force a gear. Auto-shift
	/// will compete with this if RPM differs from the new gear's expected range —
	/// for testing purposes, set ThrottleInput and the powertrain will harmonize.</summary>
	public void SetGear( int g )
	{
		if ( g == CurrentGear ) return;
		var old = CurrentGear;
		CurrentGear = g;
		VehicleEvents.RaiseShifted( this, old, g );
	}
}
