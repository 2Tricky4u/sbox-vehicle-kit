using Sandbox;
using System;

namespace Vehicles.Maintenance;

public sealed partial class VehicleBase : Component.ICollisionListener
{
	[Property, Group( "Damage" ), Range( 0, 1000 )]
	public float ImpactDamageThreshold { get; set; } = 200f;

	[Property, Group( "Damage" )]
	public float ImpactDamageMultiplier { get; set; } = 0.05f;

	void TickWear( float dt )
	{
		if ( Config == null ) return;

		// inches/sec → km/h is ×0.0254 (→m/s) ×3.6 (→km/h) = ×0.09144.
		// (Was ×0.036 — a meters-based assumption; wrong now that velocity is
		// consistently in sandbox inch units via the kinematic controller.)
		var speedKmh = Body.Velocity.Length * 0.09144f;
		var engineRunning = IsEngineRunning;

		// Fuel burn — only when throttle is engaged (idle consumption ignored for v1)
		if ( ThrottleInput > 0.1f && engineRunning )
		{
			var distanceKm = (speedKmh * dt) / 3600f;
			var litres = distanceKm * (Config.FuelConsumptionLPer100Km / 100f);
			Fuel = MathF.Max( 0f, Fuel - litres );
		}

		// Tire wear under hard cornering (TODO: refine using actual lateral slip from wheel sim)
		if ( MathF.Abs( SteerInput ) > 0.5f && speedKmh > 30f )
		{
			for ( int i = 0; i < TireWear.Count; i++ )
				TireWear[i] = MathF.Min( 1f, TireWear[i] + dt * 0.001f );
		}

		// Battery slowly drains while engine runs (alternator ≈ break-even but not perfect).
		// Drains ~5% per minute of engine-on time. Empty battery → engine won't crank.
		if ( engineRunning )
			BatteryCharge = MathF.Max( 0f, BatteryCharge - dt * (BatteryMaxCharge / 1200f) );

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

	public void OnCollisionStart( Collision collision )
	{
		// Gate on ShouldSimulate (LocalSimulation || Network.IsOwner) to match
		// the rest of the sim. Gating on Network.IsOwner alone meant crash
		// damage silently never fired in solo/local play (no network owner),
		// breaking the crash→repair half of the maintenance loop.
		if ( !ShouldSimulate ) return;
		if ( Config == null ) return;

		var impact = collision.Contact.Speed.Length;
		if ( impact < ImpactDamageThreshold ) return;

		var damage = (impact - ImpactDamageThreshold) * ImpactDamageMultiplier;
		BodyHealth = MathF.Max( 0f, BodyHealth - damage );

		// High-speed crashes also nudge engine health
		if ( impact > ImpactDamageThreshold * 3f )
		{
			var engineDmg = damage * 0.3f;
			EngineHealth = MathF.Max( 0f, EngineHealth - engineDmg );
			VehicleEvents.RaiseDamage( this, PartKind.Engine, engineDmg );
		}

		VehicleEvents.RaiseDamage( this, PartKind.Body, damage );
	}

	public void OnCollisionUpdate( Collision collision ) { }
	public void OnCollisionStop( CollisionStop collision ) { }
}
