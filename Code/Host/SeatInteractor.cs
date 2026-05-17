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

	/// <summary>Logs each step of the E-press flow so you can see exactly
	/// where seat entry fails. On by default until the flow is verified.</summary>
	[Property, Group( "Interaction" )]
	public bool DebugLog { get; set; } = true;

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

	protected override void OnStart()
	{
		var onPlayerGo = GetComponent<PlayerController>() is not null;
		var camCount = Scene.GetAllComponents<CameraComponent>().Count();
		Dbg( $"ready. Player={(Player is null ? "NULL (no PlayerController found!)" : Player.GameObject.Name)} · " +
			$"on-player-GO={onPlayerGo} · cameras={camCount} · use-key=press E (action \"use\")" );
		if ( Player is null )
			Log.Warning( "[SeatInteractor] No PlayerController in scene — add an s&box Player object and put this on it." );
		if ( !onPlayerGo )
			Log.Warning( "[SeatInteractor] Not on the same GameObject as a PlayerController — freeze/restore on enter/exit won't target the player correctly." );
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
			Dbg( $"'use' pressed (seated={_seat is not null})" );
			if ( _seat is not null ) ExitSeat();
			else TryEnterNearestSeat();
		}

		if ( ControlCamera && _seat is not null && _seat.IsDriverSeat )
			DriveChaseCamera( _seat.Vehicle );
	}

	// Proximity-based, NOT aim-based. Camera aiming proved fragile (multiple
	// cameras: editor_camera vs the PlayerController's; Scene.Camera grabbed
	// the wrong one and rays missed). For a dev harness "press E near the car"
	// is robust in any camera mode and needs no vehicle collider.
	void TryEnterNearestSeat()
	{
		// Player position if we know it, else this component's position.
		var origin = Player?.IsValid() == true ? Player.WorldPosition : WorldPosition;

		var allSeats = Scene.GetAllComponents<VehicleSeat>().ToList();
		Dbg( $"{allSeats.Count} VehicleSeat(s) in scene; player at {origin}" );
		if ( allSeats.Count == 0 )
		{
			Log.Warning( "[SeatInteractor] No VehicleSeat anywhere. Add a child GameObject with a VehicleSeat (tick IsDriverSeat) to the car." );
			return;
		}

		VehicleSeat best = null;
		float bestDist = float.MaxValue;
		foreach ( var seat in allSeats )
		{
			if ( seat?.IsValid() != true || seat.IsOccupied ) continue;
			var dist = Vector3.DistanceBetween( seat.WorldPosition, origin );
			if ( dist < bestDist ) { bestDist = dist; best = seat; }
		}

		if ( best is null )
		{
			Log.Info( "[SeatInteractor] All seats are occupied." );
			return;
		}

		if ( bestDist > UseRange )
		{
			Dbg( $"nearest free seat '{best.GameObject.Name}' is {bestDist:F0}u away (> UseRange {UseRange:F0}). Walk closer or raise UseRange." );
			return;
		}

		if ( best.TryEnter( GameObject ) )
		{
			_seat = best;
			FreezePlayerInto( best );
			var vn = best.Vehicle?.Config?.DisplayName ?? best.Vehicle?.GameObject.Name ?? "vehicle";
			Log.Info( $"[SeatInteractor] Entered {(best.IsDriverSeat ? "driver" : "passenger")} seat of {vn} ({bestDist:F0}u)." );
		}
		else
		{
			Dbg( "VehicleSeat.TryEnter returned false (taken between check and enter?)" );
		}
	}

	void Dbg( string msg )
	{
		if ( DebugLog ) Log.Info( $"[SeatInteractor] {msg}" );
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
