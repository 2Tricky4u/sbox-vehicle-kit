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

	/// <summary>Bypass the Network.IsOwner check so the vehicle simulates locally.
	/// Leave true for solo editor testing; flip to false when adding multiplayer.</summary>
	[Property, Group( "Debug" )] public bool LocalSimulation { get; set; } = true;

	bool ShouldSimulate => LocalSimulation || Network.IsOwner;

	protected override void OnAwake()
	{
		if ( Config == null )
		{
			Log.Warning( $"VehicleBase on {GameObject.Name} has no Config — disabling." );
			Enabled = false;
			return;
		}

		Body.MassOverride = Config.MassKg;
		Fuel = Config.FuelCapacityLitres;
		EngineHealth = Config.EngineMaxHealth;
		BodyHealth = Config.BodyMaxHealth;
		BatteryCharge = BatteryMaxCharge;
		OilLevel = OilMaxLevel;
		EnsureTireWearList();

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
		DebugUnsubscribe();
		SoundUnsubscribe();
		VehicleEvents.RaiseDestroyed( this );
	}

	protected override void OnUpdate()
	{
		if ( !ShouldSimulate ) return;
		TickInput();
		TickWear( Time.Delta );
		TickSystems();
		TickSound( Time.Delta );
	}

	protected override void OnFixedUpdate()
	{
		if ( !ShouldSimulate ) return;
		SimulateWheels();
	}
}
