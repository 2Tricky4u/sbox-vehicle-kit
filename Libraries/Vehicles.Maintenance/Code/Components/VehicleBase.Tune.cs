using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Tuning + maintenance modulation layer. Computes the effective values
// the wheel sim consumes by multiplying:
//   base property (Wheels.cs)  ×  Tune.* multiplier  ×  maintenance health factor
// Maintenance is what makes the gameplay loop matter — a damaged engine
// literally reduces EffectiveEnginePower; worn tires reduce EffectiveWheelGrip.
public sealed partial class VehicleBase
{
	[Property, Group( "Tune" )]
	public VehicleTuneProfile Tune { get; set; }

	// ── Multiplier accessors (default 1.0 when no Tune assigned) ──────
	float MEnginePower => Tune?.EnginePowerMultiplier ?? 1f;
	float MBrake => Tune?.BrakeMultiplier ?? 1f;
	float MSteering => Tune?.SteeringMultiplier ?? 1f;
	float MSuspensionStiffness => Tune?.SuspensionStiffnessMultiplier ?? 1f;
	float MSuspensionDamping => Tune?.SuspensionDampingMultiplier ?? 1f;
	float MFrontGrip => Tune?.FrontGripMultiplier ?? 1f;
	float MRearGrip => Tune?.RearGripMultiplier ?? 1f;
	float MDownforce => Tune?.DownforceMultiplier ?? 1f;

	// ── Maintenance health factors (clamped so player can always limp home) ──
	/// <summary>0.1..1.0 — engine output scales with health.
	/// Health below 50% of max linearly drops power; below 5 the engine doesn't crank at all (CanStartEngine gate).</summary>
	public float EngineHealthFactor
	{
		get
		{
			if ( Config is null ) return 1f;
			var halfMax = MathF.Max( 1f, Config.EngineMaxHealth * 0.5f );
			return MathX.Clamp( EngineHealth / halfMax, 0.1f, 1f );
		}
	}

	/// <summary>0.5..1.0 — body damage barely affects driving (mostly cosmetic);
	/// below ~20% body health, steering response drops slightly.</summary>
	public float BodyHealthFactor
	{
		get
		{
			if ( Config is null ) return 1f;
			return MathX.Clamp( 0.5f + 0.5f * BodyHealth / Config.BodyMaxHealth, 0.5f, 1f );
		}
	}

	/// <summary>Per-wheel grip factor including tire wear.
	/// Returns 1.0 for fresh tire, drops toward 0.3 at full wear.</summary>
	public float TireHealthFactor( int wheelIndex )
	{
		if ( wheelIndex < 0 || wheelIndex >= TireWear.Count ) return 1f;
		return MathX.Clamp( 1f - TireWear[wheelIndex] * 0.7f, 0.3f, 1f );
	}

	// ── Effective values consumed by Wheels.cs ────────────────────────
	/// <summary>Engine force in Newtons, after tune + engine health.</summary>
	public float EffectiveEnginePower => MaxEngineForce * MEnginePower * EngineHealthFactor;

	/// <summary>Brake force coefficient, after tune.</summary>
	public float EffectiveBrake => BrakeForce * MBrake;

	/// <summary>Max steering angle in degrees, after tune + body health.</summary>
	public float EffectiveSteerAngleMax => SteerAngleMax * MSteering * BodyHealthFactor;

	public float EffectiveSuspensionStiffness => SuspensionStiffness * MSuspensionStiffness;
	public float EffectiveSuspensionDamping => SuspensionDamping * MSuspensionDamping;

	public float EffectiveDownforce => Downforce * MDownforce;

	/// <summary>Per-wheel lateral grip after tune (front/rear), tire wear, and puncture state.
	/// Multiplied by Config.Grip in the wheel sim like before.</summary>
	public float EffectiveWheelGrip( int wheelIndex )
	{
		var tuneMul = IsFrontWheel( wheelIndex ) ? MFrontGrip : MRearGrip;
		var grip = LateralFriction * tuneMul * TireHealthFactor( wheelIndex );
		// Punctured tire: drops to ~20% grip — car visibly pulls toward the flat side.
		if ( IsTirePunctured( wheelIndex ) ) grip *= 0.2f;
		return MathX.Clamp( grip, 0f, 2f );
	}
}
