using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Arcade wheel sim — port of https://github.com/SergeyMakeev/ArcadeCarPhysics
// via https://github.com/matekdev/sbox-arcade-car-physics
//
// Key details vs naïve approaches that fail in Source 2:
//  • Force divided by dt before ApplyForceAt (per-step impulse → instantaneous N).
//  • Force applied at TouchTrace.HitPosition, slightly above ground (-wsDown * 0.2).
//  • Contact basis derived from cross products of contact normal + wheel forward,
//    NOT vehicle world axes (correct on slopes).
//  • 3 raycasts per wheel (left edge / center / right edge) to reject edge cases.
//  • Speed-target engine model: force = (target - current) * mass, scales itself
//    so the car self-limits to MaxSpeed without hand-tuned terminal velocity.
//  • s&box uses INCHES; m/s ↔ inches/sec is * 0.0254.
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

	[Property, Group( "Wheel" ), Range( 0.5f, 10f )]
	public float WheelWidth { get; set; } = 4f;

	[Property, Group( "Wheel" ), Range( 0, 1 )]
	public float LateralFriction { get; set; } = 0.85f;

	[Property, Group( "Wheel" ), Range( 0, 1 )]
	public float RollingFriction { get; set; } = 0.05f;

	[Property, Group( "Wheel" ), Range( 1, 200 )]
	public float BrakeForce { get; set; } = 30f;

	/// <summary>Lateral velocity (inches/sec) above which a wheel is considered skidding.
	/// Fires OnWheelSkidStarted / OnWheelSkidStopped events for VFX/audio hooks.</summary>
	[Property, Group( "Wheel" ), Range( 10, 400 )]
	public float SkidLateralThreshold { get; set; } = 80f;

	// ── Steering ──────────────────────────────────────────────────────
	[Property, Group( "Steering" ), Range( 5, 60 )]
	public float SteerAngleMax { get; set; } = 25f;

	[Property, Group( "Steering" ), Range( 0.1f, 20 )]
	public float SteerSpeed { get; set; } = 6f;

	/// <summary>How many of the (front-of-list) wheel anchors steer.
	/// Convention: anchors are listed front-to-rear.</summary>
	[Property, Group( "Steering" ), Range( 0, 8 )]
	public int FrontWheelCount { get; set; } = 2;

	// ── Engine ────────────────────────────────────────────────────────
	/// <summary>Engine force in Newtons (continuous, integrated over time).
	/// 15000 N on a 1400 kg car gives ~10 m/s² (≈0-100 km/h in 2.8s).</summary>
	[Property, Group( "Engine" ), Range( 500, 100000 )]
	public float MaxEngineForce { get; set; } = 15000f;

	/// <summary>Linear velocity damping per second (simple arcade air drag).
	/// 0.5 = velocity halves every ~1.4 sec; 1.0 = halves every 0.7 sec.</summary>
	[Property, Group( "Engine" ), Range( 0, 5f )]
	public float AirDrag { get; set; } = 0.5f;

	/// <summary>Downforce when grounded — keeps car planted at speed.
	/// Force in Newtons; scaled by speed factor.</summary>
	[Property, Group( "Engine" ), Range( 0, 50000 )]
	public float Downforce { get; set; } = 5000f;

	// (DebugLog property + tick log now live in VehicleBase.Debug.cs)

	// ── Internal per-wheel state ──────────────────────────────────────
	private class WheelState
	{
		public bool IsGrounded;
		public SceneTraceResult Center;
		public SceneTraceResult Left;
		public SceneTraceResult Right;
		public float Compression;
		public float CompressionPrevious;
	}

	private WheelState[] _wheels;
	private bool[] _wheelSkidding;
	private float _currentSteerAngle;

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

	/// <summary>Forward speed in m/s. s&amp;box uses inches; convert via 0.0254.</summary>
	float ForwardSpeedMs()
	{
		var fwd = WorldRotation.Forward;
		var vProj = Vector3.Dot( Body.Velocity, fwd );
		return vProj * 0.0254f;
	}

	void SimulateWheels()
	{
		if ( Body == null || WheelAnchors == null || WheelAnchors.Count == 0 ) return;
		EnsureWheelStates();

		var dt = Time.Delta;
		if ( dt <= 0f ) return;
		var wsDown = WorldRotation * Vector3.Down;

		// Powertrain ticks first so CurrentGear/EngineRpm are fresh for engine force below.
		TickPowertrain( dt );

		// Smoothly approach steering target. Uses tune-modulated max angle.
		var targetSteer = SteerInput * EffectiveSteerAngleMax;
		_currentSteerAngle = MathX.Lerp( _currentSteerAngle, targetSteer, MathX.Clamp( SteerSpeed * dt, 0f, 1f ) );

		// Engine FORCE in Newtons. Throttle × tune-modulated power × current-gear multiplier.
		float engineForceMag = 0f;
		if ( IsEngineRunning && MathF.Abs( ThrottleInput ) > 0.05f )
		{
			var maxSpeedMs = (Config?.MaxSpeedKmh ?? 140f) / 3.6f;
			var currentSpeedMs = ForwardSpeedMs();
			// Forward throttle can't push past +max; reverse can't go past -max/2.
			var atMaxFwd = ThrottleInput > 0 && currentSpeedMs >= maxSpeedMs;
			var atMaxRev = ThrottleInput < 0 && currentSpeedMs <= -maxSpeedMs * 0.5f;
			if ( !atMaxFwd && !atMaxRev )
				engineForceMag = ThrottleInput * EffectiveEnginePower * GearTorqueMultiplier;
		}

		// Per-wheel share is unused now (engine applied once outside loop), kept for the log.
		var enginePerWheel = engineForceMag / WheelAnchors.Count;

		int groundedCount = 0;
		for ( int i = 0; i < WheelAnchors.Count; i++ )
		{
			var anchor = WheelAnchors[i];
			if ( anchor?.IsValid() != true ) continue;
			ProcessWheel( i, anchor, wsDown, dt, enginePerWheel );
			if ( _wheels[i].IsGrounded ) groundedCount++;
		}

		// ── Engine: single direct velocity write after all wheels processed ──
		// Velocity write goes through s&box's contact-clamping where ApplyForceAt does not.
		if ( groundedCount > 0 && IsEngineRunning && !BrakeInput && MathF.Abs( engineForceMag ) > 0.01f )
		{
			// F = ma → Δv = F·dt / m  (continuous force integrated over one fixed step)
			var deltaVms = engineForceMag * dt / Body.Mass;
			var fwd = WorldRotation.Forward;
			Body.Velocity += fwd * (deltaVms / 0.0254f);
		}

		// ── Engine braking (when coasting in gear, engine drags car back) ──
		if ( groundedCount > 0 && IsEngineRunning && CurrentGear != 0
			&& MathF.Abs( ThrottleInput ) < 0.05f && EngineBrakingForce > 0f )
		{
			var fwdSpeed = ForwardSpeedMs();
			if ( MathF.Abs( fwdSpeed ) > 0.5f )
			{
				var fwd = WorldRotation.Forward;
				var brakeDv = -MathF.Sign( fwdSpeed ) * EngineBrakingForce * dt / Body.Mass;
				Body.Velocity += fwd * (brakeDv / 0.0254f);
			}
		}

		// ── Air drag (linear damping — simple arcade) ──
		if ( AirDrag > 0f )
		{
			var dampFactor = MathF.Max( 0f, 1f - AirDrag * dt );
			Body.Velocity = Body.Velocity * dampFactor;
		}

		// ── Downforce (keeps car planted at speed; tune-modulated) ──
		if ( groundedCount > 0 && EffectiveDownforce > 0f )
		{
			var speedKmh = MathF.Abs( ForwardSpeedMs() * 3.6f );
			var maxKmh = Config?.MaxSpeedKmh ?? 140f;
			var speedFactor = MathX.Clamp( speedKmh / maxKmh, 0f, 1f );
			Body.ApplyForce( WorldRotation * Vector3.Down * EffectiveDownforce * speedFactor );
		}

		DebugTick( dt, groundedCount, engineForceMag );
	}

	void ProcessWheel( int i, GameObject anchor, Vector3 wsDown, float dt, float engineForcePerWheel )
	{
		var wheel = _wheels[i];
		wheel.IsGrounded = false;

		var origin = anchor.WorldPosition;
		var traceLength = SuspensionLengthRelaxed + WheelRadius;

		// Wheel orientation (front wheels steer).
		var steerYaw = IsFrontWheel( i ) ? _currentSteerAngle : 0f;
		var wsWheelRot = WorldRotation * Rotation.FromYaw( steerYaw );
		var wsWheelLeft = wsWheelRot * Vector3.Left;

		// Triple trace.
		wheel.Left = Scene.Trace
			.Ray( origin + wsWheelLeft * WheelWidth, origin + wsWheelLeft * WheelWidth + wsDown * traceLength )
			.IgnoreGameObject( GameObject )
			.Run();
		wheel.Right = Scene.Trace
			.Ray( origin - wsWheelLeft * WheelWidth, origin - wsWheelLeft * WheelWidth + wsDown * traceLength )
			.IgnoreGameObject( GameObject )
			.Run();
		wheel.Center = Scene.Trace
			.Ray( origin, origin + wsDown * traceLength )
			.IgnoreGameObject( GameObject )
			.Run();

		// Just check whether the trace hit something. The normal-tilt check
		// (matekdev's groundDot) was excluding the user's ground entirely.
		var leftHit   = wheel.Left.Hit;
		var rightHit  = wheel.Right.Hit;
		var centerHit = wheel.Center.Hit;

		if ( !centerHit )
		{
			wheel.CompressionPrevious = wheel.Compression;
			wheel.Compression = MathX.Clamp( wheel.Compression - dt * 1.0f, 0f, 1f );
			return;
		}

		var suspensionLength = wheel.Center.Distance - WheelRadius;
		wheel.IsGrounded = true;
		wheel.Compression = 1.0f - MathX.Clamp( suspensionLength / SuspensionLengthRelaxed, 0f, 1f );

		// ── Suspension force (Hooke's law + damping; tune-modulated) ──
		var springForce = wheel.Compression * -EffectiveSuspensionStiffness;
		var compressionVel = (wheel.Compression - wheel.CompressionPrevious) / dt;
		wheel.CompressionPrevious = wheel.Compression;
		var damperForce = -compressionVel * EffectiveSuspensionDamping;
		var totalSuspension = (springForce + damperForce);
		// Project onto contact normal so sloped ground works.
		totalSuspension *= Vector3.Dot( wheel.Center.Normal, -wsDown );
		Body.ApplyForceAt( wheel.Center.HitPosition, wsDown * totalSuspension );

		// ── Friction & engine force in the contact plane ──
		var wheelVelocity = Body.GetVelocityAtPoint( wheel.Center.HitPosition );

		var contactUp = wheel.Center.Normal;
		// Prefer side-trace-derived contact left for accurate slope handling;
		// fall back to wheel-aligned left when side traces don't both hit.
		Vector3 contactLeft;
		if ( leftHit && rightHit )
			contactLeft = (wheel.Left.HitPosition - wheel.Right.HitPosition).Normal;
		else
			contactLeft = (wsWheelRot * Vector3.Left).Normal;
		var contactForward = Vector3.Cross( contactLeft, contactUp ).Normal;

		// Sliding velocity in contact plane (lateral + a bit of longitudinal).
		var lateralVel = Vector3.Dot( wheelVelocity, contactLeft ) * contactLeft;
		var forwardVel = Vector3.Dot( wheelVelocity, contactForward ) * contactForward;
		var slideVelocity = (lateralVel + forwardVel) * 0.5f;

		// Skid detection — fire transition events for VFX/audio.
		var lateralMag = lateralVel.Length;
		var nowSkidding = lateralMag > SkidLateralThreshold;
		if ( _wheelSkidding != null && i < _wheelSkidding.Length )
		{
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

		// Force needed to fully arrest the slide for this wheel's share.
		var slidingForce = (slideVelocity * Body.Mass / dt) / WheelAnchors.Count;

		// EffectiveWheelGrip rolls in tune front/rear multipliers + tire wear factor.
		var lateralFric = MathX.Clamp( EffectiveWheelGrip( i ) * (Config?.Grip ?? 1f), 0f, 2f );
		var frictionForce = -slidingForce * lateralFric;

		// Pull longitudinal component out so we can modify it (brake / rolling).
		var longitudinalForce = Vector3.Dot( frictionForce, contactForward ) * contactForward;

		if ( BrakeInput || HandbrakeInput )
		{
			var brakeMag = EffectiveBrake * Body.Mass * (Config?.BrakeStrength ?? 1f);
			if ( HandbrakeInput ) brakeMag *= 0.8f;
			var clampedMag = MathX.Clamp( brakeMag, 0f, longitudinalForce.Length );
			var brakeForceVec = longitudinalForce.Normal * clampedMag;
			longitudinalForce -= brakeForceVec;
		}
		else if ( MathF.Abs( ThrottleInput ) < 0.05f )
		{
			// Coasting: rolling resistance reduces forward retention so the car slows.
			var rollingK = 1.0f - MathX.Clamp( RollingFriction, 0f, 1f );
			longitudinalForce *= rollingK;
		}

		// Final friction = full friction MINUS the (modified) longitudinal.
		// Net effect: braking adds opposing force; rolling reduces forward friction so car coasts.
		frictionForce -= longitudinalForce;
		Body.ApplyForceAt( wheel.Center.HitPosition, frictionForce );

		// (Engine force applied OUTSIDE the per-wheel loop — see SimulateWheels.)
	}
}
