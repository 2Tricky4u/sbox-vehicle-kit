using Sandbox;
using System;

namespace Vehicles.Maintenance;

public sealed partial class VehicleBase
{
	[Property, Group( "Damage" ), Range( 0, 1000 )]
	public float ImpactDamageThreshold { get; set; } = 200f;

	[Property, Group( "Damage" )]
	public float ImpactDamageMultiplier { get; set; } = 0.05f;

	/// <summary>Minimum seconds between impact-damage applications. Grinding
	/// along a wall is one sustained contact, not a crash per tick.</summary>
	[Property, Group( "Damage" ), Range( 0.05f, 2f )]
	public float ImpactDamageCooldown { get; set; } = 0.3f;

	/// <summary>Vertical landing speed (inches/sec) at/above which touching down
	/// counts as an impact. ~350 in/s ≈ a 32 km/h vertical hit.</summary>
	[Property, Group( "Damage" ), Range( 100, 2000 )]
	public float LandingDamageThreshold { get; set; } = 350f;

	TimeSince _lastImpactDamage;

	/// <summary>Tyre wear added per second per m/s of lateral slip. Default
	/// tuned so a hard drift (~10 m/s sideways) wears a tyre 0→1 in ~30 s,
	/// while gentle cornering (~0.5 m/s) takes many minutes.</summary>
	[Property, Group( "Damage" ), Range( 0.0005f, 0.02f )]
	public float TireWearPerSlipMs { get; set; } = 0.0035f;

	/// <summary>Max distance (inches) from a collision point to a wheel anchor
	/// for the hit to count as that wheel's. Hits farther than this only do
	/// body/engine damage.</summary>
	[Property, Group( "Damage" ), Range( 4, 80 )]
	public float WheelImpactRadius { get; set; } = 24f;

	/// <summary>TireWear added per unit of (impact − ImpactDamageThreshold)
	/// when a collision lands near a wheel.</summary>
	[Property, Group( "Damage" ), Range( 0.0001f, 0.005f )]
	public float WheelImpactWearMultiplier { get; set; } = 0.0006f;

	/// <summary>Impact speed (inches/sec) at/above which a near-wheel hit
	/// punctures that tyre outright instead of just wearing it.</summary>
	[Property, Group( "Damage" ), Range( 200, 4000 )]
	public float WheelPunctureImpact { get; set; } = 1200f;

	/// <summary>Litres per second burned while the engine idles (no throttle).
	/// Default ≈ 1 L/h — leaving the engine running has a cost.</summary>
	[Property, Group( "Damage" ), Range( 0f, 0.005f )]
	public float IdleBurnLps { get; set; } = 0.0003f;

	void TickWear( float dt )
	{
		if ( Config == null ) return;

		// inches/sec → km/h is ×0.0254 (→m/s) ×3.6 (→km/h) = ×0.09144.
		// KinematicVelocity is the authoritative sim velocity; Body.Velocity is
		// only an end-of-tick mirror and lags (or is skipped) — never read it here.
		var speedKmh = KinematicVelocity.Length * 0.09144f;
		var engineRunning = IsEngineRunning;

		// Fuel burn — distance-based under throttle (forward OR reverse), plus
		// a small idle burn whenever the engine is running.
		if ( MathF.Abs( ThrottleInput ) > 0.1f && engineRunning )
		{
			var distanceKm = (speedKmh * dt) / 3600f;
			var litres = distanceKm * (Config.FuelConsumptionLPer100Km / 100f);
			Fuel = MathF.Max( 0f, Fuel - litres );
		}
		else if ( engineRunning )
		{
			Fuel = MathF.Max( 0f, Fuel - dt * IdleBurnLps );
		}

		// Tyre wear from real lateral slip (sliding scrubs rubber). Body-level
		// slip from the kinematic solver, applied to all tyres (the arcade
		// model has no per-wheel slip — same basis as skid detection).
		var slipMs = LateralSlipMs;
		if ( slipMs > 0.5f && speedKmh > 5f )
		{
			var wear = dt * slipMs * TireWearPerSlipMs;
			for ( int i = 0; i < TireWear.Count; i++ )
				TireWear[i] = MathF.Min( 1f, TireWear[i] + wear );
		}

		// Battery: the alternator recharges above idle (driving keeps it topped
		// up, ~10%/min), while idling slowly drains it (~5%/min). Sitting with
		// the engine idling forever eventually strands you; driving never does.
		// Empty battery → engine won't crank (fix: drive, or RepairRpc(Battery)).
		if ( engineRunning )
		{
			if ( EngineRpm > IdleRpm * 1.15f )
				BatteryCharge = MathF.Min( BatteryMaxCharge, BatteryCharge + dt * (BatteryMaxCharge / 600f) );
			else
				BatteryCharge = MathF.Max( 0f, BatteryCharge - dt * (BatteryMaxCharge / 1200f) );
		}

		// Oil drains with mileage. Drains ~10% per real-time minute at top speed
		// (much slower at low speed). Critical (<20%) accelerates engine wear 5×.
		if ( engineRunning && speedKmh > 1f )
		{
			var maxKmh = Config.MaxSpeedKmh > 0 ? Config.MaxSpeedKmh : 140f;
			var loadFactor = MathF.Min( 1f, speedKmh / maxKmh );
			OilLevel = MathF.Max( 0f, OilLevel - dt * loadFactor * (OilMaxLevel / 600f) );
		}

		// Low oil chews up engine health — mechanic gameplay reason to keep oil topped up.
		if ( engineRunning && IsLowOil && ThrottleInput > 0.1f )
		{
			var wearRate = dt * 0.5f * (1f - OilLevel / OilMaxLevel); // ramps as oil approaches 0
			EngineHealth = MathF.Max( 0f, EngineHealth - wearRate );
		}
	}

	// Impact damage entry point. The kinematic body (MotionEnabled=false) never
	// receives Source 2 contact events, so the wall sweep and landing detection
	// in Wheels.cs call this directly with the closing speed they measured.
	internal void ApplyImpactDamage( Vector3 hitPoint, float impactSpeedInches )
	{
		// Gate on ShouldSimulate to match the rest of the sim — only the
		// simulating machine computes damage; [Sync] carries it to everyone.
		if ( !ShouldSimulate ) return;
		if ( Config == null ) return;
		if ( impactSpeedInches < ImpactDamageThreshold ) return;
		if ( _lastImpactDamage < ImpactDamageCooldown ) return;
		_lastImpactDamage = 0f;

		var damage = (impactSpeedInches - ImpactDamageThreshold) * ImpactDamageMultiplier;
		BodyHealth = MathF.Max( 0f, BodyHealth - damage );

		// High-speed crashes also nudge engine health
		if ( impactSpeedInches > ImpactDamageThreshold * 3f )
		{
			var engineDmg = damage * 0.3f;
			EngineHealth = MathF.Max( 0f, EngineHealth - engineDmg );
			VehicleEvents.RaiseDamage( this, PartKind.Engine, engineDmg );
		}

		VehicleEvents.RaiseDamage( this, PartKind.Body, damage );

		// Per-wheel: a hit landing near a wheel scrubs / blows that tyre.
		ApplyWheelImpact( hitPoint, impactSpeedInches );
	}

	// Maps a collision point to the nearest wheel and damages that tyre only.
	void ApplyWheelImpact( Vector3 hitPoint, float impact )
	{
		if ( WheelAnchors == null || WheelAnchors.Count == 0 ) return;

		int nearest = -1;
		float bestSq = float.MaxValue;
		for ( int i = 0; i < WheelAnchors.Count; i++ )
		{
			var anchor = WheelAnchors[i];
			if ( anchor?.IsValid() != true ) continue;
			var dSq = (anchor.WorldPosition - hitPoint).LengthSquared;
			if ( dSq < bestSq ) { bestSq = dSq; nearest = i; }
		}

		if ( nearest < 0 || nearest >= TireWear.Count ) return;
		if ( bestSq > WheelImpactRadius * WheelImpactRadius ) return; // hit nowhere near a wheel

		// Very hard near-wheel hits blow the tyre outright.
		if ( impact >= WheelPunctureImpact )
		{
			TireWear[nearest] = 1f;
			PunctureTireRpc( nearest );
			VehicleEvents.RaiseDamage( this, PartKind.Tire, 1f );
			return;
		}

		var add = (impact - ImpactDamageThreshold) * WheelImpactWearMultiplier;
		if ( add <= 0f ) return;
		TireWear[nearest] = MathF.Min( 1f, TireWear[nearest] + add );
		VehicleEvents.RaiseDamage( this, PartKind.Tire, add );
	}
}
