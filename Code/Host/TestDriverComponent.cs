using Sandbox;
using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Drop on a vehicle GameObject (or assign Vehicle property) to immediately
/// occupy the driver seat for testing — sets HasDriver=true each frame and
/// follows the vehicle with a chase camera. Replace with real seat enter/exit
/// logic once the gamemode has a player + interaction system.
///
/// SUPERSEDED by <see cref="SeatInteractor"/> + the library
/// <c>VehicleSeat</c> component (real press-E get-in/get-out). Kept so older
/// scenes keep loading; migrate by: remove this, add an s&box Player object
/// (with PlayerController) and put SeatInteractor on it, and add a VehicleSeat
/// on each seat-anchor child of the vehicle (tick IsDriverSeat on the driver).
/// </summary>
[Title( "Test Driver" )]
[Category( "Vehicles" )]
[Icon( "person" )]
public sealed class TestDriverComponent : Component
{
	[Property] public VehicleBase Vehicle { get; set; }

	[Property, Group( "Camera" )]
	public bool ControlCamera { get; set; } = true;

	[Property, Group( "Camera" ), Range( 100, 1000 )]
	public float CameraDistance { get; set; } = 350f;

	[Property, Group( "Camera" ), Range( 0, 500 )]
	public float CameraHeight { get; set; } = 150f;

	[Property, Group( "Camera" ), Range( 0, 200 )]
	public float CameraLookHeight { get; set; } = 50f;

	protected override void OnAwake()
	{
		Vehicle ??= GetComponent<VehicleBase>();
	}

	protected override void OnUpdate()
	{
		if ( Vehicle is null ) return;
		Vehicle.HasDriver = true;

		if ( !ControlCamera ) return;

		var cam = Scene.GetAll<CameraComponent>().FirstOrDefault();
		if ( cam is null ) return;

		var carPos = Vehicle.WorldPosition;
		var carFwd = Vehicle.WorldRotation.Forward;

		var camPos = carPos - carFwd * CameraDistance + Vector3.Up * CameraHeight;
		var lookAt = carPos + Vector3.Up * CameraLookHeight;

		cam.WorldPosition = camPos;
		cam.WorldRotation = Rotation.LookAt( lookAt - camPos );
	}
}
