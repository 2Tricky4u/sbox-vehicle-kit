namespace Vehicles.Maintenance;

/// <summary>How the driving camera is framed while seated in a vehicle.</summary>
public enum VehicleCameraMode
{
	/// <summary>Chase camera behind the car (the default arcade view).</summary>
	ThirdPerson,
	/// <summary>Camera at the driver's seat looking forward (cockpit view).</summary>
	FirstPerson,
}

/// <summary>
/// Global, library-level driving-camera setting. ONE place to choose the
/// view for every vehicle: set <see cref="Mode"/> once (e.g. in your gamemode
/// bootstrap, or via the <c>vh.cam</c> dev command) and the seat/camera code
/// honours it. Kept here (not on a component) so it's a single global switch
/// rather than a per-vehicle / per-scene property.
/// </summary>
public static class VehicleCamera
{
	public static VehicleCameraMode Mode { get; set; } = VehicleCameraMode.ThirdPerson;
}
