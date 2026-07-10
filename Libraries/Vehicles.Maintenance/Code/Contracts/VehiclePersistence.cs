using Sandbox;
using System.Linq;

namespace Vehicles.Maintenance;

/// <summary>
/// Closes the persistence loop that <see cref="IVehicleHost.SaveVehicleOwnership"/>
/// opens. Ownership saves are useless if nothing ever reads them back — call
/// <see cref="RestoreOwnershipFor"/> from your gamemode when a player (re)joins:
/// any vehicle still in the scene whose saved owner matches the connection gets
/// its network ownership re-attached, so a driver who crashed out of the game
/// regains control of their (orphaned, host-adopted) car.
///
/// Full save/restore across map restarts — respawning the vehicles themselves
/// from a database — is the gamemode's job: persist what you need in
/// SaveVehicleOwnership, then call <c>VehicleBase.Spawn</c> for each record at
/// boot and this helper (or your own logic) to hand them back.
/// </summary>
public static class VehiclePersistence
{
	/// <summary>Re-attach ownership of every scene vehicle the host remembers
	/// as belonging to this connection. Returns how many were restored.</summary>
	public static int RestoreOwnershipFor( Connection player )
	{
		if ( player is null || VehicleHost.Current is null ) return 0;
		var scene = Game.ActiveScene;
		if ( scene is null ) return 0;

		int restored = 0;
		foreach ( var vehicle in scene.GetAllComponents<VehicleBase>().ToList() )
		{
			if ( vehicle?.GameObject?.IsValid() != true ) continue;
			if ( !VehicleHost.Current.TryLoadVehicleOwnership( vehicle.GameObject.Id, out var steamId, out _ ) ) continue;
			if ( steamId != player.SteamId ) continue;

			try
			{
				if ( vehicle.Network.Active )
					vehicle.Network.AssignOwnership( player );
				restored++;
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"[Vehicles.Maintenance] RestoreOwnershipFor: {e.Message}" );
			}
		}

		if ( restored > 0 )
			Log.Info( $"[Vehicles.Maintenance] Restored ownership of {restored} vehicle(s) to {player.DisplayName}." );
		return restored;
	}
}
