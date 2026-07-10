using Sandbox;

namespace Vehicles.Maintenance;

// One-line spawn entry point. Any caller (dealer UI, dev console, gamemode)
// instantiates a configured, networked vehicle through here. OnVehicleSpawned
// is NOT raised manually — VehicleBase.OnStart already fires it when the
// cloned object starts.
public sealed partial class VehicleBase
{
	/// <summary>
	/// Instantiate <paramref name="cfg"/>'s prefab at the given transform,
	/// apply the config, network-spawn it, and (if an owner is given) persist
	/// ownership via the host. Returns the spawned <see cref="VehicleBase"/>,
	/// or null if the prefab couldn't be resolved.
	/// </summary>
	public static VehicleBase Spawn( VehicleConfig cfg, Vector3 pos, Rotation rot, Connection owner = null )
	{
		if ( cfg is null )
		{
			Log.Warning( "[Vehicles.Maintenance] Spawn: null VehicleConfig." );
			return null;
		}
		if ( string.IsNullOrEmpty( cfg.PrefabPath ) )
		{
			Log.Warning( string.IsNullOrEmpty( cfg.ModelPath )
				? $"[Vehicles.Maintenance] Spawn: '{cfg.DisplayName}' has no PrefabPath set."
				: $"[Vehicles.Maintenance] Spawn: '{cfg.DisplayName}' has a ModelPath but no PrefabPath — a model alone can't spawn. Build a prefab (VehicleBase + collider + wheel anchors + seats) and set PrefabPath." );
			return null;
		}

		var prefabFile = ResourceLibrary.Get<PrefabFile>( cfg.PrefabPath );
		if ( prefabFile is null )
		{
			Log.Warning( $"[Vehicles.Maintenance] Spawn: couldn't load PrefabFile '{cfg.PrefabPath}'." );
			return null;
		}

		var prefab = SceneUtility.GetPrefabScene( prefabFile );
		if ( prefab is null )
		{
			Log.Warning( $"[Vehicles.Maintenance] Spawn: couldn't get prefab scene for '{cfg.PrefabPath}'." );
			return null;
		}

		var go = prefab.Clone( pos, rot );

		// Prefab root normally carries the VehicleBase; fall back to children.
		var vehicle = go.Components.Get<VehicleBase>()
			?? go.Components.GetInChildren<VehicleBase>();
		if ( vehicle is null )
		{
			Log.Warning( $"[Vehicles.Maintenance] Spawn: prefab '{cfg.PrefabPath}' has no VehicleBase component." );
			go.Destroy();
			return null;
		}

		if ( vehicle.Config is null )
			vehicle.Config = cfg;

		if ( owner is not null )
			go.NetworkSpawn( owner );
		else
			go.NetworkSpawn();

		// Optional persistence hook — gamemodes may no-op this.
		if ( owner is not null && VehicleHost.Current is not null )
			VehicleHost.Current.SaveVehicleOwnership( go.Id, owner.SteamId, cfg );

		return vehicle;
	}
}
