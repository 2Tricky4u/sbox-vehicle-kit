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
// The Rigidbody is kept for collision DETECTION (so Damage.cs ICollisionListener
// still fires) and for mass / collider geometry. We mirror _vel back to
// Body.Velocity at end-of-tick so debug log + downstream readers see consistent
// values.
//
// Drawing on matekdev/sbox-arcade-car-physics for the conceptual layout
// (per-wheel state, raycast suspension, slip-velocity friction) but every
// force application is converted to direct velocity arithmetic.
//
// v1 limitations (documented):
//  • Walls don't physically push back — body teleports through if you ram one.
//    OnCollision events still fire so Damage.cs damage routing works.
//    Proper wall response = future shapecast pass (~50 lines, deferred).
//  • No pitch/roll dynamics — body always upright, follows yaw only.
//  • Per-wheel suspension forces are SUMMED into a single Z velocity change
//    (no differential lift causing pitch/roll). Arcade-correct, sim-incorrect.
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
			var taper = sameDir ? (1f - ratio * ratio) : 1f;
			engineForceMag = ThrottleInput * EffectiveEnginePower * GearTorqueMultiplier * taper;
		}

		// ── Gravity (manual since Source 2 isn't integrating us) ─────
		const float GRAVITY_INCHES_PER_S2 = 9.81f / 0.0254f; // ≈ 386 inches/s²
		_vel.z -= GRAVITY_INCHES_PER_S2 * dt;

		// ── Wheel raycasts + suspension ──────────────────────────────
		var wsDown = WorldRotation * Vector3.Down;
		int groundedCount = 0;
		float totalSuspensionForce = 0f;
		for ( int i = 0; i < WheelAnchors.Count; i++ )
		{
			var anchor = WheelAnchors[i];
			if ( anchor?.IsValid() != true ) continue;
			ProcessWheelKinematic( i, anchor, wsDown, dt, ref totalSuspensionForce );
			if ( _wheels[i].IsGrounded ) groundedCount++;
		}

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
			var maxDvMs = MaxForwardAccelMs2 * dt;
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
		if ( groundedCount > 0 )
		{
			var right = WorldRotation.Right;
			var lat = Vector3.Dot( _vel, right );
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

		// ── Steering: rotate the body around its up axis ─────────────
		// Speed-scaled so stationary cars don't spin in place; sign-scaled
		// so reverse driving inverts steering (like a real car backing up).
		// Yaw is NEGATED: SteerInput is +1 for D/right, but s&box
		// Rotation.FromYaw(+) rotates CCW (left) in its X-fwd/Y-left/Z-up
		// space, so a right input needs a negative yaw delta.
		if ( groundedCount > 0 && MathF.Abs( _currentSteerAngle ) > 0.1f )
		{
			var fwdSpeedMs = ForwardSpeedMs();
			if ( MathF.Abs( fwdSpeedMs ) > 0.5f )
			{
				var fullSteerMs = MathF.Max( 1f, FullSteerSpeedKmh / 3.6f );
				var speedFactor = MathX.Clamp( MathF.Abs( fwdSpeedMs ) / fullSteerMs, 0f, 1f );
				var dirSign = MathF.Sign( fwdSpeedMs );
				var yawRate = -_currentSteerAngle * speedFactor * dirSign * YawRateScale;
				WorldRotation *= Rotation.FromYaw( yawRate * dt );
			}
		}

		// ── The actual move — kinematic integration step ─────────────
		// This is where the cap-bypass happens: we set the position directly
		// rather than letting Source 2's physics integrate Body.Velocity.
		WorldPosition += _vel * dt;

		// Mirror velocity for consumers that read Body.Velocity (debug log,
		// Damage.cs speedKmh, future code).
		try { Body.Velocity = _vel; } catch { }

		DebugTick( dt, groundedCount, engineForceMag );
	}

	void ProcessWheelKinematic( int i, GameObject anchor, Vector3 wsDown, float dt, ref float totalSuspensionForce )
	{
		var wheel = _wheels[i];
		wheel.IsGrounded = false;

		var origin = anchor.WorldPosition;
		var traceLength = SuspensionLengthRelaxed + WheelRadius;

		// Single center raycast — kinematic mode is forgiving enough that
		// the triple-trace from the force-based version isn't needed.
		wheel.Center = Scene.Trace
			.Ray( origin, origin + wsDown * traceLength )
			.IgnoreGameObject( GameObject )
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
