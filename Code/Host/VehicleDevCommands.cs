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
		Log.Info( "  vh.debugdraw [seconds]             — draw wheel rays + forward arrow + collider (0=stop)" );
		Log.Info( "  vh.heal                            — fully restore nearest (fuel, engine, body, tires)" );
		Log.Info( "  vh.cheat                           — toggle owner-side LocalSimulation (solo testing aid)" );
		Log.Info( "  vh.diag                            — open the DiagnosticPanel for nearest vehicle" );
		Log.Info( "  vh.parts                           — list .partdef assets" );
		Log.Info( "  vh.give <partIdent> [count]        — add parts to local mechanic's inventory" );
		Log.Info( "  vh.mechanic                        — info on toggling mechanic job (stub host = always mechanic)" );
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
		Log.Info( $"  battery={v.BatteryCharge:F0}/{VehicleBase.BatteryMaxCharge:F0} ({v.BatteryPct * 100:F0}%)  oil={v.OilLevel:F0}/{VehicleBase.OilMaxLevel:F0} ({v.OilPct * 100:F0}%)  lowOil={v.IsLowOil}" );
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
		var cap = v.Config?.FuelCapacityLitres ?? 50f;
		var target = MathX.Clamp( litres, 0f, cap );
		var delta = target - v.Fuel;
		// Use RefuelRpc so OnRefuel listeners (audio, VFX) fire even when setting absolute.
		// Negative delta becomes a no-op in RefuelRpc (it only adds), so for drains we set directly.
		if ( delta > 0 ) v.RefuelRpc( delta );
		else v.Fuel = target;
		Log.Info( $"[vh] Fuel set to {v.Fuel:F1}L (delta {delta:+0.0;-0.0})" );
	}

	[ConCmd( "vh.heal" )]
	public static void Heal()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.RepairRpc( PartKind.Engine, 9999f );
		v.RepairRpc( PartKind.Body, 9999f );
		v.RepairRpc( PartKind.Battery, 9999f );
		v.RepairRpc( PartKind.Oil, 9999f );
		for ( int i = 0; i < v.TireWear.Count; i++ )
			v.RepairRpc( PartKind.Tire, 9999f, i );
		v.RefuelRpc( v.Config?.FuelCapacityLitres ?? 50f );
		Log.Info( "[vh] Fully healed nearest vehicle (engine, body, battery, oil, tires, fuel)." );
	}

	// ── Powertrain / tune ─────────────────────────────────────────────

	[ConCmd( "vh.shift" )]
	public static void Shift( int gear )
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.SetGear( gear );
		v.LockShifts( 5f ); // suppress auto-shift for 5 seconds so the chosen gear actually sticks
		Log.Info( $"[vh] Forced gear {gear} (auto-shift suppressed for 5s)" );
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

	[ConCmd( "vh.debugdraw" )]
	public static void DebugDraw( float seconds = 8f )
	{
		if ( !TryRequireVehicle( out var v ) ) return;

		var existing = v.GetComponent<VehicleDebugDraw>();
		if ( seconds <= 0f )
		{
			existing?.Destroy();
			Log.Info( "[vh] debugdraw stopped." );
			return;
		}

		var drawer = v.GetOrAddComponent<VehicleDebugDraw>();
		drawer.Vehicle = v;
		drawer.Expire = seconds;
		Log.Info( $"[vh] debugdraw on '{v.Config?.DisplayName ?? v.GameObject.Name}' for {seconds:F0}s. " +
			"GREEN=forward (drive dir, root +X) · CYAN=wheel ray · YELLOW=anchor · RED=ground hit · ORANGE=wheel rest · WHITE=collider box" );
	}

	[ConCmd( "vh.cheat" )]
	public static void ToggleLocalSim()
	{
		if ( !TryRequireVehicle( out var v ) ) return;
		v.LocalSimulation = !v.LocalSimulation;
		Log.Info( $"[vh] LocalSimulation → {v.LocalSimulation}" );
	}

	// ── Diagnostic UI + parts inventory (testing the mechanic loop without NPCs) ──

	static DiagnosticPanel _activeDiag;

	[ConCmd( "vh.diag" )]
	public static void OpenDiagnostic()
	{
		if ( !TryRequireVehicle( out var v ) ) return;

		// In s&box panels attach to a ScreenPanel component's RootPanel.
		// We need a ScreenPanel somewhere in the scene to show HUD.
		var screen = ActiveScene?.GetAllComponents<ScreenPanel>().FirstOrDefault();
		if ( screen?.GetPanel() is null )
		{
			Log.Warning( "[vh] No ScreenPanel in scene. Add a GameObject with a ScreenPanel component, then re-run vh.diag." );
			return;
		}

		// Close existing panel if open.
		if ( _activeDiag is not null )
		{
			_activeDiag.Delete();
			_activeDiag = null;
		}

		var panel = new DiagnosticPanel
		{
			Vehicle = v,
			Mechanic = Connection.Local,
		};
		panel.Parent = screen.GetPanel();
		panel.OnClose = () =>
		{
			panel.Delete();
			_activeDiag = null;
		};
		_activeDiag = panel;
		Log.Info( $"[vh] Opened DiagnosticPanel for {v.Config?.DisplayName}. Click Close to dismiss." );
	}

	[ConCmd( "vh.parts" )]
	public static void ListParts()
	{
		var parts = PartDefinition.All.ToList();
		Log.Info( $"[vh] {parts.Count} PartDefinition(s):" );
		foreach ( var p in parts )
			Log.Info( $"  {p.ResourceName,-25} \"{p.DisplayName}\"  repairs={p.RepairsPart}  amount={p.RepairAmount}  price=${p.Price}" );
	}

	[ConCmd( "vh.give" )]
	public static void GivePart( string partIdent, int count = 1 )
	{
		if ( VehicleHost.Current is null ) { Log.Warning( "[vh] No host registered." ); return; }
		var conn = Connection.Local;
		if ( conn is null ) { Log.Warning( "[vh] No local connection." ); return; }
		var def = PartDefinition.FindByIdent( partIdent );
		if ( def is null )
		{
			Log.Warning( $"[vh] No PartDefinition '{partIdent}'. Try `vh.parts`." );
			return;
		}
		var inv = VehicleHost.Current.GetInventory( conn );
		if ( inv is null ) { Log.Warning( "[vh] No inventory." ); return; }
		inv.Add( def, count );
		Log.Info( $"[vh] Gave {count}× {def.DisplayName} to {conn.DisplayName}. Total: {inv.CountOf( def )}" );
	}

	[ConCmd( "vh.mechanic" )]
	public static void ToggleMechanicJob()
	{
		// Only meaningful with the test stub host where IsMechanic is settable;
		// real adapters route through the gamemode's job system.
		if ( VehicleHost.Current is CarMaintenanceVehicleHost )
		{
			Log.Info( "[vh] Stub host treats every player as a mechanic — toggle has no effect." );
			Log.Info( "[vh] To test the gating, edit CarMaintenanceVehicleHost.IsMechanic to return false." );
		}
		else
		{
			Log.Info( "[vh] Mechanic-job toggle requires the host to expose a setter — your registered IVehicleHost doesn't." );
		}
	}
}

/// <summary>
/// Transient in-world debug visualizer added by <c>vh.debugdraw</c>. Redraws
/// every frame (no per-line duration param, so it works across s&amp;box
/// versions — only depends on <c>Scene.DebugOverlay.Line</c>) and self-destroys
/// when <see cref="Expire"/> elapses. One per vehicle.
///
/// Legend: GREEN = forward / drive direction (root +X) · CYAN = wheel raycast ·
/// YELLOW = wheel anchor · RED = ground hit · ORANGE = wheel rest height ·
/// WHITE = BoxCollider bounds.
/// </summary>
public sealed class VehicleDebugDraw : Component
{
	public VehicleBase Vehicle { get; set; }
	public TimeUntil Expire { get; set; }

	bool _warned;

	protected override void OnUpdate()
	{
		if ( Vehicle?.IsValid() != true || Expire <= 0f ) { Destroy(); return; }

		try { Draw(); }
		catch ( System.Exception e )
		{
			if ( !_warned )
			{
				_warned = true;
				Log.Warning( $"[vh] debugdraw: Scene.DebugOverlay API unavailable in this s&box build ({e.Message}). Stopping." );
			}
			Destroy();
		}
	}

	void Draw()
	{
		var dbg = Scene?.DebugOverlay;
		if ( dbg is null ) return;

		// Single point of contact with the s&box debug API. `dbg` is `var`
		// so the overlay's concrete type name never appears in our code;
		// helpers take this delegate, keeping the API surface to one call.
		void Line( Vector3 a, Vector3 b, Color c ) => dbg.Line( a, b, c );

		var orange = new Color( 1f, 0.6f, 0f );

		// Forward arrow — physics drive direction (root +X). Compare against
		// the copcar mesh nose: if they don't agree, rotate the model child.
		var fwd = Vehicle.WorldRotation.Forward;
		var right = Vehicle.WorldRotation.Right;
		var a0 = Vehicle.WorldPosition + Vector3.Up * 12f;
		var tip = a0 + fwd * 120f;
		Line( a0, tip, Color.Green );
		Line( tip, tip - fwd * 26f + right * 18f, Color.Green );
		Line( tip, tip - fwd * 26f - right * 18f, Color.Green );

		// Wheel raycasts.
		var down = Vehicle.WorldRotation * Vector3.Down;
		var len = Vehicle.SuspensionLengthRelaxed + Vehicle.WheelRadius;
		var anchors = Vehicle.WheelAnchors;
		if ( anchors != null )
		{
			for ( int i = 0; i < anchors.Count; i++ )
			{
				var anchor = anchors[i];
				if ( anchor?.IsValid() != true ) continue;
				var o = anchor.WorldPosition;
				var end = o + down * len;
				Line( o, end, Color.Cyan );
				Cross( Line, o, 6f, Color.Yellow );

				var tr = Scene.Trace.Ray( o, end ).IgnoreGameObject( Vehicle.GameObject ).Run();
				if ( tr.Hit )
				{
					Cross( Line, tr.HitPosition, 9f, Color.Red );
					Cross( Line, tr.HitPosition - down * Vehicle.WheelRadius, 6f, orange );
				}
			}
		}

		// Collider bounds (built from lines so we don't depend on a Box overload).
		var box = Vehicle.GetComponentInChildren<BoxCollider>();
		if ( box is not null )
			DrawOrientedBox( Line, Vehicle.WorldPosition, Vehicle.WorldRotation, box.Center, box.Scale, Color.White );
	}

	static void Cross( System.Action<Vector3, Vector3, Color> line, Vector3 p, float s, Color c )
	{
		line( p - Vector3.Forward * s, p + Vector3.Forward * s, c );
		line( p - Vector3.Left * s, p + Vector3.Left * s, c );
		line( p - Vector3.Up * s, p + Vector3.Up * s, c );
	}

	static void DrawOrientedBox( System.Action<Vector3, Vector3, Color> line, Vector3 origin, Rotation rot, Vector3 center, Vector3 size, Color c )
	{
		var h = size * 0.5f;
		var corners = new Vector3[8];
		int n = 0;
		for ( int sx = -1; sx <= 1; sx += 2 )
		for ( int sy = -1; sy <= 1; sy += 2 )
		for ( int sz = -1; sz <= 1; sz += 2 )
			corners[n++] = origin + rot * (center + new Vector3( sx * h.x, sy * h.y, sz * h.z ));

		// indices: bit0=x, bit1=y, bit2=z (matches loop order above)
		int[,] edges =
		{
			{0,1},{0,2},{0,4},{1,3},{1,5},{2,3},
			{2,6},{3,7},{4,5},{4,6},{5,7},{6,7}
		};
		for ( int e = 0; e < edges.GetLength( 0 ); e++ )
			line( corners[edges[e, 0]], corners[edges[e, 1]], c );
	}
}
