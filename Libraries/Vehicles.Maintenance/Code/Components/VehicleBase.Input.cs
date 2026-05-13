using Sandbox;

namespace Vehicles.Maintenance;

public sealed partial class VehicleBase
{
	// Public smoothed inputs — what the physics, UI, and audio read.
	// (Set by TickInputFilter in VehicleBase.InputFilter.cs.)
	public float ThrottleInput { get; private set; }
	public float SteerInput { get; private set; }
	public bool BrakeInput { get; private set; }
	public bool HandbrakeInput { get; private set; }

	// Raw per-frame inputs — internal, consumed by the filter.
	float _rawThrottle, _rawSteer;
	bool _rawBrake, _rawHandbrake;

	/// <summary>Set by seat enter/exit logic. Without a driver, all input is zeroed.</summary>
	public bool HasDriver { get; set; }

	void TickInput()
	{
		if ( !HasDriver )
		{
			_rawThrottle = 0;
			_rawSteer = 0;
			_rawBrake = false;
			_rawHandbrake = false;
		}
		else
		{
			// AnalogMove.x = forward/back, .y = left/right (verify against Project Settings → Input)
			_rawThrottle = Input.AnalogMove.x;
			_rawSteer = -Input.AnalogMove.y;
			_rawBrake = Input.Down( "attack2" );
			_rawHandbrake = Input.Down( "jump" );
		}
		TickInputFilter( Time.Delta );
	}
}
