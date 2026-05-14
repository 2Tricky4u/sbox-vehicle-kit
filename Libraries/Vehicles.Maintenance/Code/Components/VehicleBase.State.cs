using Sandbox;
using System;

namespace Vehicles.Maintenance;

public sealed partial class VehicleBase
{
	// ─── Networked maintenance state ───────────────────────────────────
	[Sync] public float Fuel { get; set; }
	[Sync] public float EngineHealth { get; set; }
	[Sync] public float BodyHealth { get; set; }
	[Sync] public NetList<float> TireWear { get; set; } = new();

	// ─── Derived (no sync needed — compute from synced primitives) ────
	public float FuelPct => Config is null ? 0f : Fuel / Config.FuelCapacityLitres;
	public float EngineHealthPct => Config is null ? 0f : EngineHealth / Config.EngineMaxHealth;
	public float BodyHealthPct => Config is null ? 0f : BodyHealth / Config.BodyMaxHealth;

	/// <summary>Behaviour gate: can the engine even crank right now?</summary>
	public bool CanStartEngine => Fuel > 0.1f && EngineHealth > 5f;

	/// <summary>Effective torque after maintenance penalties.</summary>
	public float EffectiveTorque
	{
		get
		{
			if ( Config is null || !CanStartEngine ) return 0f;
			var health01 = MathF.Min( 1f, EngineHealth / MathF.Max( 1f, Config.EngineMaxHealth * 0.5f ) );
			return Config.EngineTorqueNm * health01;
		}
	}

	void EnsureTireWearList()
	{
		var n = Config?.WheelCount ?? 4;
		while ( TireWear.Count < n ) TireWear.Add( 0f );
		while ( TireWear.Count > n ) TireWear.RemoveAt( TireWear.Count - 1 );
	}

	// ─── RPCs (owner-authoritative — only the vehicle's owner mutates) ─
	[Rpc.Owner]
	public void RefuelRpc( float litres )
	{
		if ( Config == null ) return;
		Fuel = MathF.Min( Fuel + litres, Config.FuelCapacityLitres );
		VehicleEvents.RaiseRefuel( this, litres );
	}

	/// <summary>Apply damage directly (outside of physics collisions). Used by
	/// dev console commands, scripted events, gamemode admin tools.
	/// Collision damage in <see cref="VehicleBase.Damage"/> remains separate.</summary>
	[Rpc.Owner]
	public void DamageRpc( PartKind part, float amount, int wheelIndex = -1 )
	{
		if ( Config == null ) return;

		switch ( part )
		{
			case PartKind.Engine:
				EngineHealth = MathF.Max( 0f, EngineHealth - amount );
				break;
			case PartKind.Body:
				BodyHealth = MathF.Max( 0f, BodyHealth - amount );
				break;
			case PartKind.Tire:
				if ( wheelIndex >= 0 && wheelIndex < TireWear.Count )
					TireWear[wheelIndex] = MathF.Min( 1f, TireWear[wheelIndex] + amount / 100f );
				break;
		}

		VehicleEvents.RaiseDamage( this, part, amount );
	}

	[Rpc.Owner]
	public void RepairRpc( PartKind part, float amount, int wheelIndex = -1 )
	{
		if ( Config == null ) return;

		switch ( part )
		{
			case PartKind.Engine:
				EngineHealth = MathF.Min( EngineHealth + amount, Config.EngineMaxHealth );
				break;
			case PartKind.Body:
				BodyHealth = MathF.Min( BodyHealth + amount, Config.BodyMaxHealth );
				break;
			case PartKind.Tire:
				if ( wheelIndex >= 0 && wheelIndex < TireWear.Count )
				{
					TireWear[wheelIndex] = MathF.Max( 0f, TireWear[wheelIndex] - amount / 100f );
					// Clear puncture bit if the tire is now repaired below 100% wear.
					if ( wheelIndex < 32 && TireWear[wheelIndex] < 0.95f )
						TirePunctureMask &= ~(1u << wheelIndex);
				}
				break;
			// TODO: Battery, Oil
		}

		VehicleEvents.RaiseRepair( this, part, amount );
	}
}
