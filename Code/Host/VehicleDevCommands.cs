using Sandbox;
using System;
using System.Linq;
using System.Text;
using Vehicles.Maintenance;

namespace Sandbox.CarMaintenance;

/// <summary>
/// Developer console commands for exercising the Vehicles.Maintenance library
/// without needing UI / NPCs / shops. All `vh.*` commands operate on the
/// vehicle nearest to the active camera (host's view) unless otherwise noted.
///
/// Type `vh.help` in console for the full list at runtime.
///
/// Implementation note: if `[ConCmd("name")]` doesn't compile in your s&box
/// version, try the explicit form `[ConCmd.Server("name")]`. Both should work
/// in current sbox; the bare form is preferred when available.
/// </summary>
public static class VehicleDevCommands
{
	// ── Helpers ───────────────────────────────────────────────────────

	static Scene ActiveScene => Game.ActiveScene;

	static Vector3 CameraOrigin()
	{
		var cam = ActiveScene?.Camera;
		return cam?.WorldPosition ?? Vector3.Zero;
	}

	static VehicleBase Nearest()
	{
		var scene = ActiveScene;
		if ( scene is null ) return null;
		var origin = CameraOrigin();
		VehicleBase best = null;
		float bestDist = float.MaxValue;
		foreach ( var v in scene.GetAllComponents<VehicleBase>() )
		{
			if ( v?.IsValid() != true ) continue;
			var d = (v.WorldPosition - origin).LengthSquared;
			if ( d < bestDist ) { bestDist = d; best = v; }
		}
		return best;
	}

	static bool TryRequireVehicle( out VehicleBase v )
	{
		v = Nearest();
		if ( v is null ) { Log.Warning( "[vh] No vehicle found in scene." ); return false; }
		return true;
	}

	static bool TryParsePart( string s, out PartKind part )
	{
		if ( Enum.TryParse<PartKind>( s, ignoreCase: true, out part ) ) return true;
		Log.Warning( $"[vh] Unknown part '{s}'. Valid: Engine, Body, Tire, Battery, Oil" );
		return false;
	}

	// ── Discovery ─────────────────────────────────────────────────────

	[ConCmd( "vh.help" )]
	public static void Help()
	{
		Log.Info( "[vh] Vehicles.Maintenance dev commands:" );
		Log.Info( "  vh.list                            — list vehicles in scene" );
		Log.Info( "  vh.status                          — print full state of nearest vehicle" );
		Log.Info( "  vh.spawn <cfgIdent>                — spawn vehicle at camera (config by .vcfg resource name)" );
		Log.Info( "  vh.kill                            — destroy nearest vehicle" );
		Log.Info( "  vh.cfgs                            — list all .vcfg idents" );
		Log.Info( "  vh.tunes                           — list all .vtune idents" );
		Log.Info( "  vh.tune <ident>                    — apply tune profile to nearest" );
		Log.Info( "  vh.damage <part> <amount> [wheel]  — damage nearest vehicle" );
		Log.Info( "  vh.repair <part> [amount] [wheel]  — repair nearest (default amount=9999=full)" );
		Log.Info( "  vh.refuel [litres]                 — refuel nearest (default=full)" );
		Log.Info( "  vh.fuel <litres>                   — set fuel directly" );
		Log.Info( "  vh.shift <gear>                    — force a gear on nearest (-1..N, 0=neutral)" );
		Log.Info( "  vh.puncture <wheelIdx>             — puncture a tire" );
		Log.Info( "  vh.engine                          — toggle engine on/off" );
		Log.Info( "  vh.lights                          — toggle headlights" );
		Log.Info( "  vh.door <idx>                      — toggle door [idx]" );
		Log.Info( "  vh.horn                            — honk" );
		Log.Info( "  vh.flip                            — flip/recover nearest (zeroes velocity, levels rotation)" );
		Log.Info( "  vh.debug                           — toggle DebugLog on nearest" );
		Log.Info( "  vh.heal                            — fully restore nearest (fuel, engine, body, tires)" );
		Log.Info( "  vh.cheat                           — toggle owner-side LocalSimulation (solo testing aid)" );
	}

	[ConCmd( "vh.list" )]
	public static void List()
	{
		var scene = ActiveScene;
		if ( scene is null ) { Log.Warning( "[vh] No active scene." ); return; }
		int n = 0;
		foreach ( var v in scene.GetAllComponents<VehicleBase>() )
		{
			if ( v?.IsValid() != true ) continue;
			var cfgName = v.Config?.DisplayName ?? "(no config)";
			Log.Info( $"[vh] #{n++}  {cfgName}  pos={v.WorldPosition}  fuel={v.Fuel:F1}L  engine={v.EngineHealth:F0}/{v.Config?.EngineMaxHealth:F0}  gear={v.CurrentGear}" );
		}
		if ( n == 0 ) Log.Info( "[vh] No vehicles in scene." );
	}

	[ConCmd( "vh.status" )]
	public static void Status()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		// One Log.Info per line — multi-line strings get truncated by the s&box console renderer.
		Log.Info( $"[vh] Status: {v.Config?.DisplayName ?? "(no config)"} (id={v.GameObject.Id})" );
		Log.Info( $"  pos={v.WorldPosition}  rot={v.WorldRotation}" );
		var spdKmh = MathF.Abs( (v.Body?.Velocity.Length ?? 0) * 0.0254f * 3.6f );
		Log.Info( $"  velocity={v.Body?.Velocity}  ({spdKmh:F1} km/h)" );
		Log.Info( $"  fuel={v.Fuel:F2}L / {v.Config?.FuelCapacityLitres:F0}L  engine={v.EngineHealth:F0} / {v.Config?.EngineMaxHealth:F0}  body={v.BodyHealth:F0} / {v.Config?.BodyMaxHealth:F0}" );
		var tireSb = new StringBuilder( "  tireWear=[" );
		for ( int i = 0; i < v.TireWear.Count; i++ ) tireSb.Append( $"{v.TireWear[i]:F2}{(i == v.TireWear.Count - 1 ? "" : ", ")}" );
		tireSb.Append( $"]  punctureMask=0x{v.TirePunctureMask:X}" );
		Log.Info( tireSb.ToString() );
		Log.Info( $"  gear={v.CurrentGear}  rpm={v.EngineRpm:F0}  throttle={v.ThrottleInput:F2}  steer={v.SteerInput:F2}" );
		Log.Info( $"  engineOn={v.EngineOn}  lights={v.HeadlightsOn}  doors=0x{v.DoorMask:X}  isRunning={v.IsEngineRunning}" );
		Log.Info( $"  tune={v.Tune?.PresetName ?? "(none)"}  effEnginePower={v.EffectiveEnginePower:F0}N  effDownforce={v.EffectiveDownforce:F0}N" );
		Log.Info( $"  factors: engine={v.EngineHealthFactor:F2}  body={v.BodyHealthFactor:F2}" );
	}

	// VehicleConfig.All filters out the engine's .cfg files in core/cfg/ that
	// sbox's resource system mis-matches as .vcfg. See VehicleConfig.cs for the
	// rationale. Tune profiles don't suffer the same collision (no engine
	// .tune files) so a plain ResourceLibrary call is fine for those.

	[ConCmd( "vh.cfgs" )]
	public static void ListConfigs()
	{
		var configs = VehicleConfig.All.ToList();
		Log.Info( $"[vh] {configs.Count} VehicleConfig(s):" );
		foreach ( var c in configs )
			Log.Info( $"  {c.ResourceName,-30} \"{c.DisplayName}\"  maxSpeed={c.MaxSpeedKmh}km/h  mass={c.MassKg}kg" );
	}

	[ConCmd( "vh.tunes" )]
	public static void ListTunes()
	{
		var tunes = ResourceLibrary.GetAll<VehicleTuneProfile>().Where( t => t is not null ).ToList();
		Log.Info( $"[vh] {tunes.Count} VehicleTuneProfile(s):" );
		foreach ( var t in tunes )
			Log.Info( $"  {t.ResourceName,-30} \"{t.PresetName}\"  enginex{t.EnginePowerMultiplier:F2}  brake×{t.BrakeMultiplier:F2}  gripF×{t.FrontGripMultiplier:F2}  gripR×{t.RearGripMultiplier:F2}" );
	}

	// ── Spawn / destroy ───────────────────────────────────────────────

	[ConCmd( "vh.spawn" )]
	public static void Spawn( string cfgIdent )
	{
		var cfg = VehicleConfig.All
			.FirstOrDefault( c => c.ResourceName == cfgIdent || c.DisplayName == cfgIdent );
		if ( cfg is null ) { Log.Warning( $"[vh] No VehicleConfig found with ident '{cfgIdent}'. Try `vh.cfgs`." ); return; }
		if ( string.IsNullOrEmpty( cfg.PrefabPath ) )
		{
			Log.Warning( $"[vh] Config '{cfgIdent}' has no PrefabPath set — cannot spawn from console. Set PrefabPath on the .vcfg first, or drop the prefab into the scene manually." );
			return;
		}

		var scene = ActiveScene;
		var origin = CameraOrigin();
		// Drop the spawn slightly in front of and below the camera to avoid clipping.
		var cam = scene?.Camera;
		var spawnPos = cam is not null
			? origin + cam.WorldRotation.Forward * 200f + Vector3.Down * 50f
			: origin;

		var prefabFile = ResourceLibrary.Get<PrefabFile>( cfg.PrefabPath );
		if ( prefabFile is null ) { Log.Warning( $"[vh] Couldn't load PrefabFile '{cfg.PrefabPath}'." ); return; }
		var prefab = SceneUtility.GetPrefabScene( prefabFile );
		if ( prefab is null ) { Log.Warning( $"[vh] Couldn't get prefab scene for '{cfg.PrefabPath}'." ); return; }

		var go = prefab.Clone( spawnPos, Rotation.FromYaw( cam?.WorldRotation.Yaw() ?? 0f ) );
		var vehicle = go.Components.Get<VehicleBase>();
		if ( vehicle is not null && vehicle.Config is null ) vehicle.Config = cfg;
		go.NetworkSpawn();
		Log.Info( $"[vh] Spawned {cfg.DisplayName} at {spawnPos}" );
	}

	[ConCmd( "vh.kill" )]
	public static void Kill()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		Log.Info( $"[vh] Destroying {v.Config?.DisplayName ?? "vehicle"} at {v.WorldPosition}" );
		v.GameObject.Destroy();
	}

	// ── Damage / repair ───────────────────────────────────────────────

	[ConCmd( "vh.damage" )]
	public static void Damage( string partStr, float amount, int wheelIdx = -1 )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		if ( !TryParsePart( partStr, out var part ) ) return;
		v.DamageRpc( part, amount, wheelIdx );
		Log.Info( $"[vh] Damaged {part} by {amount}" + (wheelIdx >= 0 ? $" (wheel {wheelIdx})" : "") );
	}

	[ConCmd( "vh.repair" )]
	public static void Repair( string partStr, float amount = 9999f, int wheelIdx = -1 )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		if ( !TryParsePart( partStr, out var part ) ) return;
		v.RepairRpc( part, amount, wheelIdx );
		Log.Info( $"[vh] Repaired {part} by {amount}" + (wheelIdx >= 0 ? $" (wheel {wheelIdx})" : "") );
	}

	[ConCmd( "vh.refuel" )]
	public static void Refuel( float litres = -1f )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		var cap = v.Config?.FuelCapacityLitres ?? 50f;
		if ( litres < 0 ) litres = cap; // default = full top-up
		v.RefuelRpc( litres );
		Log.Info( $"[vh] Refueled +{litres:F1}L → {v.Fuel:F1}L / {cap:F0}L" );
	}

	[ConCmd( "vh.fuel" )]
	public static void SetFuel( float litres )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		// No direct "SetFuel" RPC — do a damage-then-refuel.
		v.Fuel = MathX.Clamp( litres, 0f, v.Config?.FuelCapacityLitres ?? 50f );
		Log.Info( $"[vh] Fuel set to {v.Fuel:F1}L" );
	}

	[ConCmd( "vh.heal" )]
	public static void Heal()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.RepairRpc( PartKind.Engine, 9999f );
		v.RepairRpc( PartKind.Body, 9999f );
		for ( int i = 0; i < v.TireWear.Count; i++ )
			v.RepairRpc( PartKind.Tire, 9999f, i );
		v.RefuelRpc( v.Config?.FuelCapacityLitres ?? 50f );
		Log.Info( "[vh] Fully healed nearest vehicle." );
	}

	// ── Powertrain / tune ─────────────────────────────────────────────

	[ConCmd( "vh.shift" )]
	public static void Shift( int gear )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.SetGear( gear );
		Log.Info( $"[vh] Forced gear {gear} (auto-shift may re-adjust based on RPM)" );
	}

	[ConCmd( "vh.tune" )]
	public static void Tune( string tuneIdent )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		var tune = ResourceLibrary.GetAll<VehicleTuneProfile>()
			.Where( t => t is not null )
			.FirstOrDefault( t => t.ResourceName == tuneIdent || t.PresetName == tuneIdent );
		if ( tune is null )
		{
			Log.Warning( $"[vh] No VehicleTuneProfile found with ident '{tuneIdent}'. Try `vh.tunes` to list available." );
			return;
		}
		v.Tune = tune;
		Log.Info( $"[vh] Applied tune '{tune.PresetName}' to {v.Config?.DisplayName ?? "vehicle"}" );
	}

	// ── Systems ───────────────────────────────────────────────────────

	[ConCmd( "vh.puncture" )]
	public static void Puncture( int wheelIdx )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.PunctureTireRpc( wheelIdx );
		Log.Info( $"[vh] Punctured tire {wheelIdx}" );
	}

	[ConCmd( "vh.engine" )]
	public static void ToggleEngine()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.ToggleEngineRpc();
		Log.Info( $"[vh] Engine toggled → {v.EngineOn}" );
	}

	[ConCmd( "vh.lights" )]
	public static void ToggleLights()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.ToggleHeadlightsRpc();
		Log.Info( $"[vh] Headlights toggled → {v.HeadlightsOn}" );
	}

	[ConCmd( "vh.door" )]
	public static void ToggleDoor( int idx = 0 )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.ToggleDoorRpc( idx );
		Log.Info( $"[vh] Door {idx} toggled (mask now 0x{v.DoorMask:X})" );
	}

	[ConCmd( "vh.horn" )]
	public static void Horn()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.HornRpc();
		Log.Info( "[vh] *honk*" );
	}

	// ── Recovery / debug toggles ──────────────────────────────────────

	[ConCmd( "vh.flip" )]
	public static void Flip()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		if ( v.Body is null ) { Log.Warning( "[vh] Vehicle has no Rigidbody." ); return; }
		// Preserve heading; zero pitch/roll. Lift slightly to avoid clipping.
		var yaw = v.WorldRotation.Yaw();
		v.WorldRotation = Rotation.FromYaw( yaw );
		v.WorldPosition += Vector3.Up * 30f;
		v.Body.Velocity = Vector3.Zero;
		v.Body.AngularVelocity = Vector3.Zero;
		Log.Info( "[vh] Flipped + zeroed velocity." );
	}

	[ConCmd( "vh.debug" )]
	public static void ToggleDebug()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.DebugLog = !v.DebugLog;
		Log.Info( $"[vh] DebugLog → {v.DebugLog}" );
	}

	[ConCmd( "vh.cheat" )]
	public static void ToggleLocalSim()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.LocalSimulation = !v.LocalSimulation;
		Log.Info( $"[vh] LocalSimulation → {v.LocalSimulation}" );
	}
}
