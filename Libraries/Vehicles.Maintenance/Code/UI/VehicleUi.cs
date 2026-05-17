using Sandbox;
using Sandbox.UI;
using System.Linq;

namespace Vehicles.Maintenance;

/// <summary>
/// Shared mount point for the library's screen panels (HUD, part-select,
/// diagnostic). Panels attach to the scene's <see cref="ScreenPanel"/> root;
/// this is the one place that lookup lives so every panel mounts the same way
/// and the "no ScreenPanel in scene" warning isn't duplicated/spammed.
/// </summary>
public static class VehicleUi
{
	static bool _warned;

	/// <summary>The root <see cref="Panel"/> to parent library UI under, or
	/// null if the scene has no <see cref="ScreenPanel"/> (warned once).</summary>
	public static Panel MountRoot()
	{
		var root = Game.ActiveScene?
			.GetAllComponents<ScreenPanel>()
			.FirstOrDefault()?
			.GetPanel();

		if ( root is null && !_warned )
		{
			_warned = true;
			Log.Warning( "[Vehicles.Maintenance] No ScreenPanel in scene — UI can't mount. Add a GameObject with a ScreenPanel component (same requirement as vh.diag)." );
		}
		return root;
	}
}
