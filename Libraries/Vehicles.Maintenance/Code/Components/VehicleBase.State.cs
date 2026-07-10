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
	[Sync] public float BatteryCharge { get; set; }   // 0..BatteryMaxCharge
	[Sync] public float OilLevel { get; set; }        // 0..OilMaxLevel

	/// <summary>Battery scale max (0..N). Hardcoded for v1; promote to VehicleConfig
	/// once the schema unlocks for v1.1.</summary>
	public const float BatteryMaxCharge = 100f;

	/// <summary>Oil level scale max (0..N). Same v1 hardcoding rationale.</summary>
	public const float OilMaxLevel = 100f;

	// ─── Derived (no sync needed — compute from synced primitives) ────
	public float FuelPct => Config is null ? 0f : Fuel / Config.FuelCapacityLitres;
	public float EngineHealthPct => Config is null ? 0f : EngineHealth / Config.EngineMaxHealth;
	public float BodyHealthPct => Config is null ? 0f : BodyHealth / Config.BodyMaxHealth;
	public float BatteryPct => BatteryCharge / BatteryMaxCharge;
	public float OilPct => OilLevel / OilMaxLevel;

	/// <summary>Behaviour gate: can the engine even crank right now?
	/// Requires fuel, engine health AND battery charge.</summary>
	public bool CanStartEngine => Fuel > 0.1f && EngineHealth > 5f && BatteryCharge > 5f;

	/// <summary>True when oil is critically low — engine wear accelerates.
	/// See Damage.cs TickWear for the scaling.</summary>
	public bool IsLowOil => OilLevel < OilMaxLevel * 0.2f;

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
		// Size to whichever is larger: the sim iterates WheelAnchors while wear
		// indices come from Config.WheelCount — undersizing either silently
		// desyncs damage from the visual wheels.
		var cfgN = Config?.WheelCount ?? 4;
		var anchorN = WheelAnchors?.Count ?? 0;
		if ( anchorN > 0 && cfgN != anchorN )
			Log.Warning( $"[Vehicles.Maintenance] {GameObject.Name}: Config.WheelCount={cfgN} but {anchorN} WheelAnchors are assigned — using {MathF.Max( cfgN, anchorN )} tire slots." );
		var n = Math.Max( cfgN, Math.Max( anchorN, 1 ) );
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
			case PartKind.Battery:
				BatteryCharge = MathF.Max( 0f, BatteryCharge - amount );
				break;
			case PartKind.Oil:
				OilLevel = MathF.Max( 0f, OilLevel - amount );
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
			case PartKind.Battery:
				BatteryCharge = MathF.Min( BatteryCharge + amount, BatteryMaxCharge );
				break;
			case PartKind.Oil:
				OilLevel = MathF.Min( OilLevel + amount, OilMaxLevel );
				break;
		}

		VehicleEvents.RaiseRepair( this, part, amount );
	}
}
