using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Input smoothing — converts raw 0/1 keyboard input into ramped throttle/steer
// so the car feels analog instead of binary. Independent rise/fall rates per
// channel, deadzone snap to zero.
public sealed partial class VehicleBase
{
	[Property, Group( "Input" ), Range( 1, 30 )]
	public float ThrottleRiseRate { get; set; } = 6f;

	[Property, Group( "Input" ), Range( 1, 30 )]
	public float ThrottleFallRate { get; set; } = 8f;

	[Property, Group( "Input" ), Range( 1, 30 )]
	public float SteerRiseRate { get; set; } = 8f;

	[Property, Group( "Input" ), Range( 1, 30 )]
	public float SteerFallRate { get; set; } = 12f;

	[Property, Group( "Input" ), Range( 0, 0.3f )]
	public float InputDeadzone { get; set; } = 0.05f;

	void TickInputFilter( float dt )
	{
		// Throttle: ramps faster on rise than fall to avoid abrupt let-off
		// (gives the car a "wound up" feel on quick keyboard taps).
		var rawT = MathF.Abs( _rawThrottle ) < InputDeadzone ? 0f : _rawThrottle;
		var rateT = MathF.Abs( rawT ) > MathF.Abs( ThrottleInput ) ? ThrottleRiseRate : ThrottleFallRate;
		ThrottleInput = ApproachF( ThrottleInput, rawT, rateT * dt );

		// Steering: similar but recenters slightly faster than it engages.
		var rawS = MathF.Abs( _rawSteer ) < InputDeadzone ? 0f : _rawSteer;
		var rateS = MathF.Abs( rawS ) > MathF.Abs( SteerInput ) ? SteerRiseRate : SteerFallRate;
		SteerInput = ApproachF( SteerInput, rawS, rateS * dt );

		// Brake/handbrake stay binary for v1 — analog brake pressure is a future polish item.
		BrakeInput = _rawBrake;
		HandbrakeInput = _rawHandbrake;
	}

	static float ApproachF( float current, float target, float maxDelta )
	{
		var diff = target - current;
		if ( MathF.Abs( diff ) <= maxDelta ) return target;
		return current + MathF.Sign( diff ) * maxDelta;
	}
}
