using Sandbox;
using System;

namespace Vehicles.Maintenance;

/// <summary>
/// A seat on a vehicle. Place one on each seat-anchor child GameObject of a
/// vehicle prefab. This component owns ONLY the seat mechanics — occupancy
/// state, driver-input gating, networked ownership handoff. It deliberately
/// knows nothing about players, input, or cameras: that is the gamemode's job
/// (the dev/test harness is <c>SeatInteractor</c> in the host project; a real
/// gamemode wires its own pawn + interaction and calls <see cref="TryEnter"/>
/// / <see cref="Exit"/>). This keeps the library gamemode-agnostic.
///
/// Occupancy is replicated via <see cref="OccupantId"/> (the occupant
/// GameObject's Id; <c>Guid.Empty</c> = free). Entering a seat flagged
/// <see cref="IsDriverSeat"/> sets <c>Vehicle.HasDriver = true</c> (the gate
/// VehicleBase.Input uses) and, when networked, hands vehicle ownership to the
/// occupant's connection so their input drives the car.
/// </summary>
[Title( "Vehicle Seat" )]
[Category( "Vehicles" )]
[Icon( "airline_seat_recline_normal" )]
public sealed class VehicleSeat : Component
{
	[Property] public VehicleBase Vehicle { get; set; }

	/// <summary>Driver seat controls the vehicle. Exactly one per vehicle for
	/// v1 (multiple driver seats would fight over HasDriver).</summary>
	[Property] public bool IsDriverSeat { get; set; } = false;

	/// <summary>Parent the occupant GameObject to this seat while seated so it
	/// rides along (passengers stay with a moving car). The occupant's original
	/// parent is restored on exit. Disable for gamemodes that position pawns
	/// themselves.</summary>
	[Property] public bool ParentOccupantToSeat { get; set; } = true;

	/// <summary>Id of the occupying GameObject. <c>Guid.Empty</c> = empty.
	/// Synced so every client agrees on occupancy.</summary>
	[Sync] public Guid OccupantId { get; set; } = Guid.Empty;

	/// <summary>Local convenience handle to whoever entered (not networked —
	/// GameObject refs don't replicate; <see cref="OccupantId"/> is the truth).</summary>
	public GameObject Occupant { get; private set; }

	public bool IsOccupied => OccupantId != Guid.Empty;

	protected override void OnAwake()
	{
		Vehicle ??= Components.GetInAncestorsOrSelf<VehicleBase>();
	}

	/// <summary>Attempt to seat <paramref name="occupant"/>. Returns false if
	/// the seat is already taken by someone else.</summary>
	public bool TryEnter( GameObject occupant )
	{
		if ( occupant?.IsValid() != true ) return false;
		if ( IsOccupied && OccupantId != occupant.Id ) return false;

		Occupant = occupant;
		EnterRpc( occupant.Id );
		return true;
	}

	/// <summary>Vacate the seat. No-op if empty.</summary>
	public void Exit()
	{
		if ( !IsOccupied ) return;
		ExitRpc();
	}

	[Rpc.Owner]
	void EnterRpc( Guid occupantId )
	{
		OccupantId = occupantId;

		if ( IsDriverSeat && Vehicle is not null )
		{
			Vehicle.HasDriver = true;
			TryAssignVehicleOwnership( occupantId );
		}

		AttachOccupant( occupantId );

		if ( Vehicle is not null )
			VehicleEvents.RaiseSeatEntered( Vehicle, this );
	}

	[Rpc.Owner]
	void ExitRpc()
	{
		DetachOccupant( OccupantId );

		OccupantId = Guid.Empty;
		Occupant = null;

		if ( IsDriverSeat && Vehicle is not null )
			Vehicle.HasDriver = false;

		if ( Vehicle is not null )
			VehicleEvents.RaiseSeatExited( Vehicle, this );
	}

	// Ride-along: parent the occupant under the seat (world position kept) so
	// passengers move with the car; restore on exit and place them beside the
	// vehicle. Best-effort — pawn setups vary, so failures only warn.
	void AttachOccupant( Guid occupantId )
	{
		if ( !ParentOccupantToSeat ) return;
		try
		{
			var occ = Scene.Directory.FindByGuid( occupantId );
			if ( occ?.IsValid() != true ) return;
			occ.SetParent( GameObject, keepWorldPosition: true );
			occ.WorldPosition = WorldPosition;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[VehicleSeat] attach skipped: {e.Message}" );
		}
	}

	void DetachOccupant( Guid occupantId )
	{
		if ( !ParentOccupantToSeat ) return;
		try
		{
			var occ = Scene.Directory.FindByGuid( occupantId );
			if ( occ?.IsValid() != true ) return;
			if ( occ.Parent == GameObject )
			{
				occ.SetParent( null, keepWorldPosition: true );
				var v = Vehicle;
				if ( v?.IsValid() == true )
				{
					occ.WorldPosition = WorldPosition + v.WorldRotation.Left * 70f + Vector3.Up * 10f;
					occ.WorldRotation = Rotation.FromYaw( v.WorldRotation.Yaw() );
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[VehicleSeat] detach skipped: {e.Message}" );
		}
	}

	// In a networked session the entering player must own the vehicle so their
	// input drives it. Best-effort + guarded: solo/LocalSimulation play needs
	// none of this, and the network API surface varies across s&box builds.
	void TryAssignVehicleOwnership( Guid occupantId )
	{
		try
		{
			if ( Vehicle?.GameObject is null ) return;
			if ( !Vehicle.Network.Active ) return; // not networked → nothing to do

			var occ = Scene.Directory.FindByGuid( occupantId );
			var conn = occ?.Network?.Owner;
			if ( conn is not null )
				Vehicle.Network.AssignOwnership( conn );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[VehicleSeat] ownership handoff skipped: {e.Message}" );
		}
	}

	// If the seat is torn down while occupied (vehicle destroyed, hotload),
	// free the occupant and don't leave the driver gate stuck on.
	protected override void OnDestroy()
	{
		if ( !IsOccupied ) return;
		DetachOccupant( OccupantId );
		if ( IsDriverSeat && Vehicle is not null )
			Vehicle.HasDriver = false;
	}
}
