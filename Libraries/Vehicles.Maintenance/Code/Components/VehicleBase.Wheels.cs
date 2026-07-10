using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Kinematic wheel sim — bypasses Source 2's contact damping by integrating
// body motion ourselves rather than letting the physics engine do it.
//
// Key decision: in OnAwake we set Body.MotionEnabled = false. Source 2 stops
// integrating the body. We manually compute every per-tick force/influence,
// accumulate into our authoritative `_vel` vector, then move via:
//     WorldPosition += _vel * dt;
//
// The Rigidbody is kept for collider geometry only. A non-simulated body gets
// NO physics contact events, so crash damage is raised from our own geometry
// queries: SweepHorizontal wall hits and landing detection both feed
// Damage.cs ApplyImpactDamage with the measured closing speed. We mirror _vel
// back to Body.Velocity at end-of-tick so debug readers see consistent values.
//
// Drawing on matekdev/sbox-arcade-car-physics for the conceptual layout
// (per-wheel state, raycast suspension, slip-velocity friction) but every
// force application is converted to direct velocity arithmetic.
//
// v1 limitations (documented):
//  • Walls: SweepHorizontal() fires horizontal feeler rays at body height
//    (the floor+walls are one MapCollider, so a low box sweep only ever sees
//    the floor) and clamps + slides the horizontal move. Toggle WallCollision;
//    size via BodyBoxSize (used for ray fan width / nose length / height).
//  • Pitch/roll: the root is tilted to the averaged wheel-ray ground normal
//    (kinematic, not force-driven) — eased via BodyAlignSpeed, clamped by
//    MaxBodyTiltDeg, yaw stays instant. Toggle TerrainTilt. It's an
//    orientation match, not weight-transfer dynamics (arcade, not sim).
//  • Per-wheel suspension forces are SUMMED into a single Z velocity change
//    (no differential lift). The visible attitude comes from the tilt above.
public sealed partial class VehicleBase
{
	// ── Suspension ────────────────────────────────────────────────────
	[Property, Group( "Suspension" ), Range( 5, 100 )]
	public float SuspensionLengthRelaxed { get; set; } = 25f;

	[Property, Group( "Suspension" ), Range( 100, 100000 )]
	public float SuspensionStiffness { get; set; } = 30000f;

	[Property, Group( "Suspension" ), Range( 10, 10000 )]
	public float SuspensionDamping { get; set; } = 3000f;

	// ── Wheel ─────────────────────────────────────────────────────────
	[Property, Group( "Wheel" ), Range( 2, 30 )]
	public float WheelRadius { get; set; } = 8f;

	[Property, Group( "Wheel" ), Range( 0, 1 )]
	public float LateralFriction { get; set; } = 0.85f;

	[Property, Group( "Wheel" ), Range( 0, 1 )]
	public float RollingFriction { get; set; } = 0.05f;

	[Property, Group( "Wheel" ), Range( 1, 200 )]
	public float BrakeForce { get; set; } = 30f;

	[Property, Group( "Wheel" ), Range( 10, 400 )]
	public float SkidLateralThreshold { get; set; } = 80f;

	// ── Steering ──────────────────────────────────────────────────────
	[Property, Group( "Steering" ), Range( 5, 60 )]
	public float SteerAngleMax { get; set; } = 25f;

	[Property, Group( "Steering" ), Range( 0.1f, 20 )]
	public float SteerSpeed { get; set; } = 6f;

	/// <summary>How aggressively the body yaws per second per degree of steer
	/// angle, at full steering authority.</summary>
	[Property, Group( "Steering" ), Range( 0.1f, 5f )]
	public float YawRateScale { get; set; } = 3.0f;

	/// <summary>Speed (km/h) at which the car reaches full steering authority.
	/// Below this, yaw ramps up from zero so a crawling car doesn't pirouette;
	/// at/above it you get the full turn rate. Keep low (~30) — referencing top
	/// speed instead makes turning feel sluggish at normal driving speeds.</summary>
	[Property, Group( "Steering" ), Range( 5, 80 )]
	public float FullSteerSpeedKmh { get; set; } = 30f;

	[Property, Group( "Steering" ), Range( 0, 8 )]
	public int FrontWheelCount { get; set; } = 2;

	// ── Terrain tilt (body pitch/roll to the ground) ─────────────────
	/// <summary>Pitch/roll the body to the ground normal (from the wheel
	/// rays) so it noses up ramps and leans on slopes. Off = upright,
	/// yaw-only (the original behaviour / safe fallback).</summary>
	[Property, Group( "Tilt" )]
	public bool TerrainTilt { get; set; } = true;

	/// <summary>How fast the body's up eases toward the terrain normal
	/// (per second). Higher = snappier but less bump-smoothing; lower =
	/// floatier. Steering yaw is NOT slewed, so it stays responsive.</summary>
	[Property, Group( "Tilt" ), Range( 1, 30 )]
	public float BodyAlignSpeed { get; set; } = 8f;

	/// <summary>Max lean from world-up (degrees) — clamps steep terrain so
	/// the car can't flip onto its roof.</summary>
	[Property, Group( "Tilt" ), Range( 0, 60 )]
	public float MaxBodyTiltDeg { get; set; } = 35f;

	// ── Engine ────────────────────────────────────────────────────────
	[Property, Group( "Engine" ), Range( 500, 200000 )]
	public float MaxEngineForce { get; set; } = 15000f;

	/// <summary>Linear velocity damping per second on horizontal motion.</summary>
	[Property, Group( "Engine" ), Range( 0, 5f )]
	public float AirDrag { get; set; } = 0.15f;

	[Property, Group( "Engine" ), Range( 0, 50000 )]
	public float Downforce { get; set; } = 5000f;

	/// <summary>Hard ceiling on forward acceleration (m/s²). This is the
	/// perceived-mass knob: the kinematic solver applies 100% of engine force
	/// (Source 2 no longer eats it), so without this the car gains ~40 m/s²
	/// and feels weightless. ~6 m/s² ≈ a brisk sports car (0–100 km/h in ~4.6s);
	/// lower = heavier / slower pickup.</summary>
	[Property, Group( "Engine" ), Range( 1, 30 )]
	public float MaxForwardAccelMs2 { get; set; } = 6f;

	// ── Wall collision (collide-and-slide) ───────────────────────────
	/// <summary>Stop the kinematic body passing through walls. The solver
	/// sweeps a box along the horizontal move and slides along wall faces.
	/// Disable to fall back to the original ghost-through behaviour.</summary>
	[Property, Group( "Collision" )]
	public bool WallCollision { get; set; } = true;

	/// <summary>Approximate car bounding box (inches) used for the wall sweep.
	/// Roughly length × width × height; centred a little above the origin so
	/// it clears the floor.</summary>
	[Property, Group( "Collision" )]
	public Vector3 BodyBoxSize { get; set; } = new( 170f, 80f, 50f );

	bool _wallTraceWarned;

	// ── Internal per-wheel state ──────────────────────────────────────
	private class WheelState
	{
		public bool IsGrounded;
		public SceneTraceResult Center;
		public float Compression;
		public float CompressionPrevious;
	}

	WheelState[] _wheels;
	bool[] _wheelSkidding;
	float _currentSteerAngle;
	bool _kinematicReady;

	/// <summary>Our authoritative velocity in inches/sec (sandbox units).
	/// Body.Velocity is a mirror updated at end of each tick for debug visibility,
	/// but we don't trust it for our own math.</summary>
	Vector3 _vel;

	/// <summary>Public accessor so other partials (Damage.cs, Powertrain.cs) can
	/// read our velocity without going through Body.Velocity (which lags by a tick).</summary>
	public Vector3 KinematicVelocity => _vel;

	/// <summary>Directly set the sim velocity (inches/sec). For gamemodes and
	/// tooling: launch pads, explosions, scripted crashes, dev commands.</summary>
	public void SetKinematicVelocity( Vector3 velocityInches ) => _vel = velocityInches;

	// Landing-impact tracking: while airborne we remember the fall speed so the
	// airborne→grounded transition can convert it into impact damage.
	int _prevGroundedCount;
	float _fallVelZ;

	float _lateralSlipMs;

	/// <summary>Absolute sideways slide of the body in m/s (how hard the car
	/// is scrubbing). Body-level — the kinematic arcade model has no per-wheel
	/// slip; this is the same basis as skid detection. Consumed by TickWear.</summary>
	public float LateralSlipMs => _lateralSlipMs;

	void EnsureWheelStates()
	{
		if ( _wheels == null || _wheels.Length != WheelAnchors.Count )
		{
			_wheels = new WheelState[WheelAnchors.Count];
			_wheelSkidding = new bool[WheelAnchors.Count];
			for ( int i = 0; i < _wheels.Length; i++ ) _wheels[i] = new WheelState();
		}
	}

	bool IsFrontWheel( int i ) => i < FrontWheelCount;

	/// <summary>Mass in kg for our manual F=ma integration. Body.Mass reads 0
	/// once MotionEnabled=false (a kinematic body has no dynamic mass), which
	/// would make every force/Mass term divide by zero → Infinity → NaN. We
	/// own the integration now, so we own the mass too.</summary>
	float VehicleMass => (Config != null && Config.MassKg > 0f) ? Config.MassKg : 1000f;

	/// <summary>Forward speed in m/s (s&amp;box uses inches; convert via 0.0254).</summary>
	float ForwardSpeedMs()
	{
		var fwd = WorldRotation.Forward;
		var vProj = Vector3.Dot( _vel, fwd );
		return vProj * 0.0254f;
	}

	void SetupKinematicIfNeeded()
	{
		if ( _kinematicReady ) return;
		if ( Body == null ) return;
		// Take movement off Source 2's hands. Body keeps its collider for
		// detection, but no physics integration → no contact damping.
		try { Body.MotionEnabled = false; } catch { /* property may differ in older sbox versions */ }
		_vel = Vector3.Zero;
		_kinematicReady = true;
	}

	void SimulateWheels()
	{
		if ( Body == null || WheelAnchors == null || WheelAnchors.Count == 0 ) return;
		EnsureWheelStates();
		SetupKinematicIfNeeded();

		var dt = Time.Delta;
		if ( dt <= 0f ) return;

		// Powertrain ticks first so CurrentGear/EngineRpm reflect this step.
		TickPowertrain( dt );

		// Smoothly approach steering target.
		var targetSteer = SteerInput * EffectiveSteerAngleMax;
		_currentSteerAngle = MathX.Lerp( _currentSteerAngle, targetSteer, MathX.Clamp( SteerSpeed * dt, 0f, 1f ) );

		// ── Compute engine force in Newtons ──────────────────────────
		float engineForceMag = 0f;
		if ( CanStartEngine && MathF.Abs( ThrottleInput ) > 0.05f )
		{
			var maxSpeedMs = (Config?.MaxSpeedKmh ?? 140f) / 3.6f;
			var currentSpeedMs = ForwardSpeedMs();
			// Smooth force taper toward top speed (1 - ratio²) instead of a
			// hard cutoff: acceleration fades as we near max, so the car
			// asymptotes to top speed with weight rather than snapping there.
			var targetMax = ThrottleInput > 0 ? maxSpeedMs : maxSpeedMs * 0.5f;
			var ratio = MathX.Clamp( MathF.Abs( currentSpeedMs ) / targetMax, 0f, 1f );
			// Pushing against current motion (reversals) gets full force.
			var sameDir = MathF.Sign( currentSpeedMs ) == MathF.Sign( ThrottleInput );
			// Config.AccelerationCurve shapes the power band per car (evaluated
			// over speed/topSpeed); its default frames (1 → 0.7 → 0) closely
			// match the old hardcoded 1−ratio² taper. Floored so a malformed
			// curve can't make a car undrivable.
			float taper = 1f;
			if ( sameDir )
			{
				taper = Config is not null
					? MathF.Max( 0.02f, Config.AccelerationCurve.Evaluate( ratio ) )
					: 1f - ratio * ratio;
			}
			engineForceMag = ThrottleInput * EffectiveEnginePower * GearTorqueMultiplier * taper;
		}

		// ── Gravity (manual since Source 2 isn't integrating us) ─────
		const float GRAVITY_INCHES_PER_S2 = 9.81f / 0.0254f; // ≈ 386 inches/s²
		_vel.z -= GRAVITY_INCHES_PER_S2 * dt;

		// ── Wheel raycasts + suspension ──────────────────────────────
		// World-down (NOT body-down): keeps ground detection independent of
		// body attitude so terrain-tilt can't feed back into the suspension
		// rays and oscillate. With an upright body this is identical anyway.
		var wsDown = Vector3.Down;
		int groundedCount = 0;
		float totalSuspensionForce = 0f;
		for ( int i = 0; i < WheelAnchors.Count; i++ )
		{
			var anchor = WheelAnchors[i];
			if ( anchor?.IsValid() != true ) continue;
			ProcessWheelKinematic( i, anchor, wsDown, dt, ref totalSuspensionForce );
			if ( _wheels[i].IsGrounded ) groundedCount++;
		}

		// Landing impact: convert remembered fall speed into damage on the
		// airborne→grounded transition. Scaled ×0.7 — a landing hurts less than
		// an equal-speed head-on. Damage.cs applies threshold + cooldown.
		if ( groundedCount == 0 )
		{
			_fallVelZ = _vel.z;
		}
		else
		{
			if ( _prevGroundedCount == 0 && _fallVelZ < -LandingDamageThreshold )
				ApplyImpactDamage( WorldPosition, -_fallVelZ * 0.7f );
			_fallVelZ = 0f;
		}
		_prevGroundedCount = groundedCount;

		// Sum of per-wheel suspension forces → single upward velocity change.
		// Force is in Newtons. Δv = F·dt / m, then convert m/s → inches/sec.
		if ( groundedCount > 0 )
		{
			var suspensionDvMs = totalSuspensionForce * dt / VehicleMass;
			_vel.z += suspensionDvMs / 0.0254f;
		}

		// ── Engine drive (forward, only when grounded + engine running) ──
		if ( groundedCount > 0 && IsEngineRunning && !BrakeInput && MathF.Abs( engineForceMag ) > 0.01f )
		{
			var fwd = WorldRotation.Forward;
			var deltaVms = engineForceMag * dt / VehicleMass;
			// Cap longitudinal acceleration — the kinematic solver applies the
			// full engine force (Source 2 no longer absorbs it), so without
			// this the car gains speed instantly and feels weightless.
			// The cap itself is power-derived: a weak/heavy car sits below the
			// prefab ceiling naturally, so .vcfg torque differences stay visible
			// instead of every car pinning to the same clamp.
			var powerCapMs2 = BaseEngineForce / (VehicleMass * 1.4f);
			var maxDvMs = MathF.Min( MaxForwardAccelMs2, powerCapMs2 ) * dt;
			deltaVms = MathX.Clamp( deltaVms, -maxDvMs, maxDvMs );
			_vel += fwd * (deltaVms / 0.0254f);
		}

		// ── Engine braking when coasting in gear ─────────────────────
		if ( groundedCount > 0 && IsEngineRunning && CurrentGear != 0
			&& MathF.Abs( ThrottleInput ) < 0.05f && EngineBrakingForce > 0f )
		{
			var fwdSpeedMs = ForwardSpeedMs();
			if ( MathF.Abs( fwdSpeedMs ) > 0.5f )
			{
				var fwd = WorldRotation.Forward;
				var brakeDvMs = -MathF.Sign( fwdSpeedMs ) * EngineBrakingForce * dt / VehicleMass;
				_vel += fwd * (brakeDvMs / 0.0254f);
			}
		}

		// ── Active brake (right-click / handbrake) ───────────────────
		if ( BrakeInput || HandbrakeInput )
		{
			var fwd = WorldRotation.Forward;
			var fwdSpeed = Vector3.Dot( _vel, fwd );
			if ( MathF.Abs( fwdSpeed ) > 1f )
			{
				var brakeMag = EffectiveBrake * VehicleMass * (Config?.BrakeStrength ?? 1f);
				if ( HandbrakeInput ) brakeMag *= 0.8f;
				var brakeDvMs = -MathF.Sign( fwdSpeed ) * brakeMag * dt / VehicleMass;
				var brakeDv = brakeDvMs / 0.0254f;
				// Don't overshoot zero
				if ( MathF.Abs( brakeDv ) > MathF.Abs( fwdSpeed ) ) brakeDv = -fwdSpeed;
				_vel += fwd * brakeDv;
			}
		}

		// ── Lateral grip — damp the body's sideways velocity ─────────
		// Per-wheel grip is averaged via EffectiveWheelGrip but applied at
		// body level (we don't have angular dynamics here, so per-wheel
		// torque doesn't apply). Strength scales with config grip + tune.
		_lateralSlipMs = 0f;
		if ( groundedCount > 0 )
		{
			var right = WorldRotation.Right;
			var lat = Vector3.Dot( _vel, right );
			_lateralSlipMs = MathF.Abs( lat ) * 0.0254f;   // inches/s → m/s
			if ( MathF.Abs( lat ) > 0.01f )
			{
				// Average effective grip across grounded wheels
				float gripAvg = 0f;
				int g = 0;
				for ( int i = 0; i < _wheels.Length; i++ )
				{
					if ( !_wheels[i].IsGrounded ) continue;
					gripAvg += EffectiveWheelGrip( i );
					g++;
				}
				if ( g > 0 ) gripAvg /= g;
				var gripFactor = gripAvg * (Config?.Grip ?? 1f);
				var damp = MathX.Clamp( gripFactor * 8f * dt, 0f, 1f );
				_vel -= right * lat * damp;
			}

			// Skid event detection (uniform across grounded wheels in this model)
			var lateralMag = MathF.Abs( lat );
			var nowSkidding = lateralMag > SkidLateralThreshold;
			for ( int i = 0; i < _wheelSkidding.Length; i++ )
			{
				if ( !_wheels[i].IsGrounded )
				{
					if ( _wheelSkidding[i] )
					{
						_wheelSkidding[i] = false;
						VehicleEvents.RaiseWheelSkidStopped( this, i );
					}
					continue;
				}
				if ( nowSkidding && !_wheelSkidding[i] )
				{
					_wheelSkidding[i] = true;
					VehicleEvents.RaiseWheelSkidStarted( this, i );
				}
				else if ( !nowSkidding && _wheelSkidding[i] )
				{
					_wheelSkidding[i] = false;
					VehicleEvents.RaiseWheelSkidStopped( this, i );
				}
			}
		}

		// ── Air drag — damp horizontal motion only (gravity handles vertical) ──
		if ( AirDrag > 0f )
		{
			var dampFactor = MathF.Max( 0f, 1f - AirDrag * dt );
			var horiz = new Vector3( _vel.x, _vel.y, 0 ) * dampFactor;
			_vel = new Vector3( horiz.x, horiz.y, _vel.z );
		}

		// ── Downforce when grounded — sticks car to floor at speed ───
		if ( groundedCount > 0 && EffectiveDownforce > 0f )
		{
			var speedKmh = MathF.Abs( ForwardSpeedMs() * 3.6f );
			var maxKmh = Config?.MaxSpeedKmh ?? 140f;
			var speedFactor = MathX.Clamp( speedKmh / maxKmh, 0f, 1f );
			var downForceDvMs = EffectiveDownforce * speedFactor * dt / VehicleMass;
			_vel.z -= downForceDvMs / 0.0254f;
		}

		// ── Orientation: heading (steering) + tilt to terrain ────────
		// Steering yaw is applied IMMEDIATELY (responsive) around the body's
		// up; the up itself is EASED toward the averaged ground normal from
		// the wheel rays (which are cast world-down, so attitude can't feed
		// back into ground detection → no oscillation). Tilt is clamped.

		// Steering yaw this tick — same gating as before: no spin in place,
		// speed-scaled, reverse inverts. NEGATED (D/right → CW).
		float yawDeg = 0f;
		if ( groundedCount > 0 && MathF.Abs( _currentSteerAngle ) > 0.1f )
		{
			var fwdSpeedMs = ForwardSpeedMs();
			if ( MathF.Abs( fwdSpeedMs ) > 0.5f )
			{
				var fullSteerMs = MathF.Max( 1f, FullSteerSpeedKmh / 3.6f );
				var speedFactor = MathX.Clamp( MathF.Abs( fwdSpeedMs ) / fullSteerMs, 0f, 1f );
				var dirSign = MathF.Sign( fwdSpeedMs );
				yawDeg = -_currentSteerAngle * speedFactor * dirSign * YawRateScale * dt;
			}
		}

		if ( !TerrainTilt )
		{
			// Fallback: original yaw-only, upright body.
			if ( yawDeg != 0f )
				WorldRotation *= Rotation.FromYaw( yawDeg );
		}
		else
		{
			// Desired up = averaged ground normal of grounded wheels.
			var desiredUp = Vector3.Up;
			if ( groundedCount > 0 )
			{
				var nSum = Vector3.Zero;
				for ( int i = 0; i < _wheels.Length; i++ )
					if ( _wheels[i].IsGrounded ) nSum += _wheels[i].Center.Normal;
				if ( nSum.Length > 0.01f ) desiredUp = nSum.Normal;
			}

			// Clamp lean from world-up so steep terrain can't roll the car over.
			var dotU = MathX.Clamp( Vector3.Dot( desiredUp, Vector3.Up ), -1f, 1f );
			var tiltDeg = MathF.Acos( dotU ) * (180f / MathF.PI);
			if ( tiltDeg > MaxBodyTiltDeg && tiltDeg > 0.01f )
				desiredUp = Vector3.Lerp( Vector3.Up, desiredUp, MaxBodyTiltDeg / tiltDeg ).Normal;
			if ( groundedCount == 0 )
				desiredUp = Vector3.Up;   // airborne → relax to level

			// Ease the up (smooths bumps); yaw stays instant.
			var smoothUp = Vector3.Lerp( WorldRotation.Up, desiredUp,
				MathX.Clamp( BodyAlignSpeed * dt, 0f, 1f ) );
			if ( smoothUp.Length < 0.01f ) smoothUp = Vector3.Up;
			smoothUp = smoothUp.Normal;

			var fwd = WorldRotation.Forward;
			if ( yawDeg != 0f )
				fwd = Rotation.FromAxis( smoothUp, yawDeg ) * fwd;
			fwd -= smoothUp * Vector3.Dot( fwd, smoothUp );   // keep ⟂ to up
			if ( fwd.Length < 0.01f ) fwd = WorldRotation.Forward;

			WorldRotation = Rotation.LookAt( fwd.Normal, smoothUp );
		}

		// ── The actual move — kinematic integration step ─────────────
		// This is where the cap-bypass happens: we set the position directly
		// rather than letting Source 2's physics integrate Body.Velocity.
		var delta = _vel * dt;

		// Vertical CCD: a fall faster than one wheel radius per tick can step
		// past the suspension rays and tunnel through thin floors. Clamp the
		// vertical move against a downward ray and treat the stop as a landing.
		if ( delta.z < -WheelRadius )
		{
			try
			{
				var probe = Scene.Trace
					.Ray( WorldPosition + Vector3.Up * 2f, WorldPosition + Vector3.Down * (-delta.z + 2f) )
					.IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "player" )
					.Run();
				if ( probe.Hit && probe.Normal.z > 0.7f )
				{
					var fallSpeed = -_vel.z;
					delta.z = -MathF.Max( 0f, probe.Distance - 2f );
					_vel.z = 0f;
					ApplyImpactDamage( probe.HitPosition, fallSpeed * 0.7f );
				}
			}
			catch { /* trace API mismatch — fall back to unclamped move */ }
		}

		if ( WallCollision )
		{
			// Vertical (gravity/suspension) passes through untouched — only the
			// horizontal move is swept so the floor never blocks driving.
			var horiz = new Vector3( delta.x, delta.y, 0f );
			var pos = SweepHorizontal( WorldPosition, horiz );
			WorldPosition = pos + new Vector3( 0f, 0f, delta.z );
		}
		else
		{
			WorldPosition += delta;
		}

		// Mirror velocity for consumers that read Body.Velocity (debug log,
		// Damage.cs speedKmh, future code).
		try { Body.Velocity = _vel; } catch { }

		DebugTick( dt, groundedCount, engineForceMag );
	}

	// Wall blocking via horizontal "feeler" rays at body height.
	//
	// Why rays, not a box sweep: the level's floor and walls are usually ONE
	// shared MapCollider. A low box sweep keeps hitting the floor first (up
	// normal) and we'd never see the wall. Horizontal rays fired from mid-body
	// height physically cannot touch the floor, so they isolate walls cleanly.
	// We fan 3 rays across the car width in the move direction; if the nearest
	// wall (near-vertical normal) is closer than the car can travel, clamp the
	// move, kill the into-wall velocity, and slide the remainder along the face.
	Vector3 SweepHorizontal( Vector3 pos, Vector3 horiz )
	{
		var dist = horiz.Length;
		if ( dist < 0.05f ) return pos;

		try
		{
			var dir = horiz / dist;
			var halfLen = BodyBoxSize.x * 0.5f;     // centre → nose
			var halfWid = BodyBoxSize.y * 0.5f;     // centre → side
			const float skin = 2.0f;

			var feeler = pos + Vector3.Up * (BodyBoxSize.z * 0.5f); // above the ground
			var right = Vector3.Cross( Vector3.Up, dir ).Normal;    // ⟂ to travel
			var rayLen = dist + halfLen + skin;

			float nearest = float.MaxValue;
			Vector3 wallN = default;
			Vector3 wallHitPos = default;
			for ( int s = -1; s <= 1; s++ )
			{
				var start = feeler + right * (halfWid * s);
				var tr = Scene.Trace.Ray( start, start + dir * rayLen )
					.IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "player" )   // players never block a car (no player-impact system in v1)
					.Run();
				if ( !tr.Hit ) continue;
				if ( MathF.Abs( tr.Normal.z ) > 0.7f ) continue;     // floor/ramp, not a wall
				if ( tr.Distance < nearest )
				{
					nearest = tr.Distance;
					wallN = tr.Normal.WithZ( 0f ).Normal;
					wallHitPos = tr.HitPosition;
				}
			}

			if ( nearest == float.MaxValue )            // no wall ahead → full move
				return pos + horiz;

			// How far the car centre may advance before its nose meets the wall.
			var allowed = MathX.Clamp( nearest - halfLen - skin, 0f, dist );
			pos += dir * allowed;

			if ( wallN.Length >= 0.01f )
			{
				// Slide the unused part of the move along the wall face.
				var remaining = horiz - dir * allowed;
				var slide = remaining - wallN * Vector3.Dot( remaining, wallN );
				pos += slide;

				// Cancel only the velocity going INTO the wall (keep tangential
				// + any away-from-wall component, so you can reverse off it).
				// The closing speed IS the crash — this is the organic trigger
				// for the crash→damage→repair loop (the kinematic body gets no
				// physics contact events, so damage must come from here).
				var into = Vector3.Dot( _vel, wallN );
				if ( into < 0f )
				{
					ApplyImpactDamage( wallHitPos, -into );
					_vel -= wallN * into;
				}
			}

			return pos;
		}
		catch ( System.Exception e )
		{
			if ( !_wallTraceWarned )
			{
				_wallTraceWarned = true;
				Log.Warning( $"[Vehicles.Maintenance] Wall feeler unavailable in this s&box build ({e.Message}); set WallCollision=false to silence. Falling back to ghost-through." );
			}
			return pos + horiz;
		}
	}

	void ProcessWheelKinematic( int i, GameObject anchor, Vector3 wsDown, float dt, ref float totalSuspensionForce )
	{
		var wheel = _wheels[i];
		wheel.IsGrounded = false;

		var origin = anchor.WorldPosition;
		var traceLength = SuspensionLengthRelaxed + WheelRadius;

		// Single center raycast — kinematic mode is forgiving enough that
		// the triple-trace from the force-based version isn't needed.
		// Hierarchy ignore: the vehicle's model collider lives on a CHILD
		// object; ignoring only the root would let the suspension ray hit
		// the car's own body once a real model is attached.
		wheel.Center = Scene.Trace
			.Ray( origin, origin + wsDown * traceLength )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !wheel.Center.Hit )
		{
			wheel.CompressionPrevious = wheel.Compression;
			wheel.Compression = MathX.Clamp( wheel.Compression - dt * 1.0f, 0f, 1f );
			return;
		}

		var suspensionLength = wheel.Center.Distance - WheelRadius;
		wheel.IsGrounded = true;
		wheel.Compression = 1.0f - MathX.Clamp( suspensionLength / SuspensionLengthRelaxed, 0f, 1f );

		// Hooke's law + damping. Each wheel contributes Newtons; caller sums
		// them and converts to a single body Z velocity change.
		var springForce = wheel.Compression * EffectiveSuspensionStiffness;
		var compressionVel = (wheel.Compression - wheel.CompressionPrevious) / dt;
		wheel.CompressionPrevious = wheel.Compression;
		var damperForce = compressionVel * EffectiveSuspensionDamping;
		totalSuspensionForce += springForce + damperForce;
	}
}
