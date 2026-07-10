using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Flip/stuck recovery. The library DETECTS a stuck vehicle (upside-down and
// stationary for a few seconds → OnVehicleStuck) and PROVIDES the recovery
// action (RecoverUprightRpc); whether recovery is free, costs money, or needs
// a tow-truck job is gamemode policy, wired via the event.
public sealed partial class VehicleBase
{
	/// <summary>How long (seconds) the vehicle must be upside-down and
	/// near-stationary before <c>OnVehicleStuck</c> fires.</summary>
	[Property, Group( "Recovery" ), Range( 1f, 15f )]
	public float StuckDetectSeconds { get; set; } = 3f;

	/// <summary>Body-up Z below which the vehicle counts as flipped
	/// (0.3 ≈ leaning past ~72° from upright).</summary>
	[Property, Group( "Recovery" ), Range( -1f, 0.9f )]
	public float StuckUpThreshold { get; set; } = 0.3f;

	float _stuckTimer;
	bool _stuckRaised;

	/// <summary>True while the vehicle is currently flipped + stationary
	/// (before/after the event fires — UI can poll this).</summary>
	public bool IsStuck => _stuckRaised;

	/// <summary>Right the vehicle in place: preserve heading, level pitch/roll,
	/// lift clear of the ground, and zero the sim velocity.</summary>
	[Rpc.Owner]
	public void RecoverUprightRpc()
	{
		WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );
		WorldPosition += Vector3.Up * 24f;
		SetKinematicVelocity( Vector3.Zero );
		try { Body.Velocity = Vector3.Zero; Body.AngularVelocity = Vector3.Zero; } catch { }
		_stuckTimer = 0f;
		_stuckRaised = false;
	}

	// Called from OnUpdate on the simulating machine.
	void TickRecovery( float dt )
	{
		var flipped = WorldRotation.Up.z < StuckUpThreshold;
		var stationary = KinematicVelocity.Length < 20f;

		if ( flipped && stationary )
		{
			_stuckTimer += dt;
			if ( !_stuckRaised && _stuckTimer >= StuckDetectSeconds )
			{
				_stuckRaised = true;
				VehicleEvents.RaiseStuck( this );
			}
		}
		else
		{
			_stuckTimer = 0f;
			_stuckRaised = false;
		}
	}

	/// <summary>Vacate every seat on this vehicle (destroy/despawn safety so
	/// occupants aren't left frozen inside a vanishing object).</summary>
	public void EjectAllSeats()
	{
		try
		{
			foreach ( var seat in GameObject.GetComponentsInChildren<VehicleSeat>() )
				if ( seat?.IsValid() == true && seat.IsOccupied )
					seat.Exit();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Vehicles.Maintenance] EjectAllSeats: {e.Message}" );
		}
	}
}
