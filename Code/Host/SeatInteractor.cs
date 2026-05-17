using Sandbox;
using System.Linq;
using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Dev/test glue between s&amp;box's built-in <see cref="PlayerController"/>
/// and the library <see cref="VehicleSeat"/> system. A real gamemode replaces
/// this with its own interaction code; the library never depends on it.
///
/// SETUP: put this component on the SAME GameObject as your s&amp;box
/// <c>PlayerController</c> (the standard Player object). Walk up to a vehicle,
/// press <c>use</c> (E) to take the nearest free seat, press it again to get
/// out. While seated the PlayerController is disabled (no walking / no player
/// camera), the player rides the seat, and this drives a chase camera. On exit
/// the player is placed beside the car and the controller is restored.
///
/// Supersedes <see cref="TestDriverComponent"/> (camera-only, no real pawn).
/// </summary>
[Title( "Seat Interactor (dev)" )]
[Category( "Vehicles" )]
[Icon( "directions_walk" )]
public sealed class SeatInteractor : Component
{
	[Property, Group( "Interaction" ), Range( 50, 500 )]
	public float UseRange { get; set; } = 200f;

	[Property, Group( "Camera" )]
	public bool ControlCamera { get; set; } = true;

	[Property, Group( "Camera" ), Range( 100, 1000 )]
	public float CameraDistance { get; set; } = 350f;

	[Property, Group( "Camera" ), Range( 0, 500 )]
	public float CameraHeight { get; set; } = 150f;

	[Property, Group( "Camera" ), Range( 0, 200 )]
	public float CameraLookHeight { get; set; } = 50f;

	/// <summary>The s&box player controller on this GameObject. Auto-found.</summary>
	[Property] public PlayerController Player { get; set; }

	VehicleSeat _seat;
	GameObject _prevParent;

	public bool IsSeated => _seat?.IsValid() == true && _seat.OccupantId == GameObject.Id;

	protected override void OnAwake()
	{
		Player ??= GetComponent<PlayerController>()
			?? Scene.GetAllComponents<PlayerController>().FirstOrDefault();
	}

	protected override void OnUpdate()
	{
		// Seat may vanish under us (vehicle destroyed / kicked elsewhere).
		if ( _seat?.IsValid() != true || _seat.OccupantId != GameObject.Id )
		{
			if ( _seat is not null ) RestorePlayer( null );
			_seat = null;
		}

		if ( Input.Pressed( "use" ) )
		{
			if ( _seat is not null ) ExitSeat();
			else TryEnterLookedAtSeat();
		}

		if ( ControlCamera && _seat is not null && _seat.IsDriverSeat )
			DriveChaseCamera( _seat.Vehicle );
	}

	void TryEnterLookedAtSeat()
	{
		var cam = Scene.Camera ?? Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( cam is null ) return;

		var from = cam.WorldPosition;
		var to = from + cam.WorldRotation.Forward * UseRange;
		var tr = Scene.Trace.Ray( from, to ).IgnoreGameObject( GameObject ).Run();
		if ( !tr.Hit || tr.GameObject is null ) return;

		var vehicle = tr.GameObject.Components.GetInAncestorsOrSelf<VehicleBase>();
		if ( vehicle is null ) return;

		VehicleSeat best = null;
		float bestDist = float.MaxValue;
		foreach ( var seat in vehicle.GetComponentsInChildren<VehicleSeat>() )
		{
			if ( seat.IsOccupied ) continue;
			var d = (seat.WorldPosition - tr.HitPosition).LengthSquared;
			if ( d < bestDist ) { bestDist = d; best = seat; }
		}

		if ( best is null )
		{
			Log.Info( "[SeatInteractor] No free seat on that vehicle." );
			return;
		}

		if ( best.TryEnter( GameObject ) )
		{
			_seat = best;
			FreezePlayerInto( best );
			Log.Info( $"[SeatInteractor] Entered {(best.IsDriverSeat ? "driver" : "passenger")} seat of {vehicle.Config?.DisplayName ?? vehicle.GameObject.Name}." );
		}
	}

	void ExitSeat()
	{
		var seat = _seat;
		var v = seat?.Vehicle;
		seat?.Exit();
		_seat = null;
		RestorePlayer( seat );
		Log.Info( $"[SeatInteractor] Exited seat of {v?.Config?.DisplayName ?? v?.GameObject.Name ?? "vehicle"}." );
	}

	// Disable the controller so it stops walking/aiming the camera, and ride
	// the seat anchor so the body moves with the car.
	void FreezePlayerInto( VehicleSeat seat )
	{
		try
		{
			if ( Player is not null ) Player.Enabled = false;
			_prevParent = GameObject.Parent;
			GameObject.SetParent( seat.GameObject, false );
			LocalPosition = Vector3.Zero;
			LocalRotation = Rotation.Identity;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[SeatInteractor] freeze failed: {e.Message}" );
		}
	}

	// Put the player back on its feet beside the car and hand control back to
	// the s&box PlayerController (it resumes camera + movement on re-enable).
	void RestorePlayer( VehicleSeat seat )
	{
		try
		{
			GameObject.SetParent( _prevParent, false );
			_prevParent = null;

			if ( seat?.Vehicle?.IsValid() == true )
			{
				var v = seat.Vehicle;
				var dropPos = seat.WorldPosition + v.WorldRotation.Left * 70f + Vector3.Up * 10f;
				WorldPosition = dropPos;
				WorldRotation = Rotation.FromYaw( v.WorldRotation.Yaw() );
			}

			if ( Player is not null ) Player.Enabled = true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[SeatInteractor] restore failed: {e.Message}" );
		}
	}

	void DriveChaseCamera( VehicleBase vehicle )
	{
		if ( vehicle?.IsValid() != true ) return;
		var cam = Scene.Camera ?? Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( cam is null ) return;

		var carPos = vehicle.WorldPosition;
		var carFwd = vehicle.WorldRotation.Forward;
		var camPos = carPos - carFwd * CameraDistance + Vector3.Up * CameraHeight;
		var lookAt = carPos + Vector3.Up * CameraLookHeight;

		cam.WorldPosition = camPos;
		cam.WorldRotation = Rotation.LookAt( lookAt - camPos );
	}

	protected override void OnDestroy()
	{
		if ( _seat?.IsValid() == true && _seat.OccupantId == GameObject.Id )
		{
			_seat.Exit();
			RestorePlayer( _seat );
		}
	}
}
