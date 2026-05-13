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

		var speedKmh = Body.Velocity.Length * 0.036f;

		// Fuel burn — only when throttle is engaged (idle consumption ignored for v1)
		if ( ThrottleInput > 0.1f && CanStartEngine )
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
	}

	public void OnCollisionStart( Collision collision )
	{
		if ( !Network.IsOwner ) return;
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
