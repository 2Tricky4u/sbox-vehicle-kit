using Sandbox;
using System;

namespace Vehicles.Maintenance;

/// <summary>
/// Visually opens/closes a door GameObject by lerping its local rotation
/// in response to the parent vehicle's <c>DoorMask</c> bit.
///
/// Setup: place on a child GameObject that represents the door (could be
/// the model node itself, or a hinge pivot if the model has separate door
/// geometry). On Awake, the closed local rotation is captured. The open
/// rotation is computed by rotating around the local Z axis by OpenAngle.
/// Set DoorIndex to match the bit position used in ToggleDoorRpc.
/// </summary>
[Title( "Vehicle Door" )]
[Category( "Vehicles" )]
[Icon( "door_front" )]
public sealed class VehicleDoor : Component
{
	[Property] public VehicleBase Vehicle { get; set; }

	[Property, Range( 0, 31 )]
	public int DoorIndex { get; set; } = 0;

	[Property, Range( 0, 180 )]
	public float OpenAngle { get; set; } = 70f;

	[Property, Range( 0.5f, 20 )]
	public float SwingSpeed { get; set; } = 5f;

	/// <summary>Axis to rotate around for the open swing. Z by default
	/// (vertical hinge — typical car door).</summary>
	[Property] public Vector3 HingeAxis { get; set; } = Vector3.Up;

	Rotation _closedRot;
	Rotation _openRot;

	protected override void OnAwake()
	{
		Vehicle ??= Components.GetInAncestorsOrSelf<VehicleBase>();
		_closedRot = LocalRotation;
		_openRot = _closedRot * Rotation.FromAxis( HingeAxis, OpenAngle );
	}

	protected override void OnUpdate()
	{
		if ( Vehicle is null ) return;
		var target = Vehicle.IsDoorOpen( DoorIndex ) ? _openRot : _closedRot;
		LocalRotation = Rotation.Lerp( LocalRotation, target, MathX.Clamp( SwingSpeed * Time.Delta, 0f, 1f ) );
	}
}
