using Sandbox;
using System.Collections.Generic;

namespace Vehicles.Maintenance;

/// <summary>
/// Core vehicle component. Every drivable vehicle prefab has one of these.
/// All per-vehicle stats live on the referenced VehicleConfig — no subclassing required.
/// </summary>
[Title( "Vehicle Base" )]
[Category( "Vehicles" )]
[Icon( "directions_car" )]
public sealed partial class VehicleBase : Component
{
	[Property] public VehicleConfig Config { get; set; }

	[Property, Group( "Setup" )]
	public List<GameObject> WheelAnchors { get; set; } = new();

	[Property, Group( "Setup" )]
	public List<GameObject> SeatAnchors { get; set; } = new();

	[RequireComponent] public Rigidbody Body { get; set; }

	/// <summary>Solo/offline only: simulate even without network ownership so
	/// editor testing works. Ignored the moment the object is networked —
	/// networked vehicles are ALWAYS owner-authoritative (proxies never
	/// simulate or write [Sync] state), so this is safe to leave true.</summary>
	[Property, Group( "Debug" )] public bool LocalSimulation { get; set; } = true;

	// Networked: only the owning client (or the host for unowned objects)
	// runs the sim. Every proxy simulating too was the old failure mode —
	// all clients integrated positions and fought over [Sync] state.
	bool ShouldSimulate => Network.Active ? !IsProxy : LocalSimulation;

	protected override void OnAwake()
	{
		if ( Config == null )
		{
			Log.Warning( $"VehicleBase on {GameObject.Name} has no Config — disabling." );
			Enabled = false;
			return;
		}

		Body.MassOverride = Config.MassKg;

		// [Sync] state is initialised by the simulating side only — a proxy
		// writing these in its own OnAwake would fight the owner's replication
		// (the old bug: every client re-seeded fuel/health/TireWear on join).
		if ( !Network.Active || !IsProxy )
		{
			Fuel = Config.FuelCapacityLitres;
			EngineHealth = Config.EngineMaxHealth;
			BodyHealth = Config.BodyMaxHealth;
			BatteryCharge = BatteryMaxCharge;
			OilLevel = OilMaxLevel;
			EnsureTireWearList();
		}

		// Schema sanity: Config.SeatCount is the data-side seat contract; the
		// prefab's SeatAnchors are the physical seats. Only checked when anchors
		// are assigned — legacy scenes place VehicleSeat children directly.
		if ( SeatAnchors is { Count: > 0 } && SeatAnchors.Count != Config.SeatCount )
			Log.Warning( $"[Vehicles.Maintenance] {GameObject.Name}: Config.SeatCount={Config.SeatCount} but {SeatAnchors.Count} SeatAnchors are assigned." );

		// Kinematic mode: take movement off Source 2's hands so its contact
		// damping never caps our velocity. We integrate manually in
		// VehicleBase.Wheels.cs (SetupKinematicIfNeeded is a lazy fallback
		// for the networked-proxy / hotload case where OnAwake didn't run).
		SetupKinematicIfNeeded();
	}

	protected override void OnStart()
	{
		DebugSubscribe();
		SoundSubscribe();
		VehicleEvents.RaiseSpawned( this );
	}

	protected override void OnDestroy()
	{
		EjectAllSeats();
		DebugUnsubscribe();
		SoundUnsubscribe();
		VehicleEvents.RaiseDestroyed( this );
	}

	protected override void OnUpdate()
	{
		// Audio runs on EVERY machine — it only reads synced state (EngineOn,
		// EngineRpm, IsWrecked), so proxies hear the engine without simulating.
		TickSound( Time.Delta );

		if ( !ShouldSimulate ) return;
		TickInput();
		TickWear( Time.Delta );
		TickSystems();
		TickRecovery( Time.Delta );
	}

	protected override void OnFixedUpdate()
	{
		if ( !ShouldSimulate ) return;
		SimulateWheels();
	}
}
