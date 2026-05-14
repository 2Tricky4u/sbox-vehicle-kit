using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Audio layer — pitches the engine loop by RPM, plays one-shots on horn /
// engine start / engine stop / shift / redline events.
//
// Asset paths come from VehicleConfig.EngineSoundPath / HornSoundPath. If
// either is empty, the corresponding playback is skipped silently — the
// component remains functional, just inaudible.
//
// Sound API note: s&box's Sound.Play(string) returns a SoundHandle (or
// similar). Pitch / Position / Stop methods on the handle vary slightly
// across versions; if a property name doesn't compile, check the API
// browser for the exact spelling.
public sealed partial class VehicleBase
{
	[Property, Group( "Sound" ), Range( 0.3f, 1f )]
	public float EngineMinPitch { get; set; } = 0.5f;

	[Property, Group( "Sound" ), Range( 1f, 2.5f )]
	public float EngineMaxPitch { get; set; } = 1.6f;

	[Property, Group( "Sound" ), Range( 0f, 1f )]
	public float EngineVolume { get; set; } = 0.7f;

	SoundHandle _engineLoop;
	bool _engineLoopActive;

	void StartEngineLoopIfNeeded()
	{
		if ( _engineLoopActive ) return;
		var path = Config?.EngineSoundPath;
		if ( string.IsNullOrEmpty( path ) ) return;

		try
		{
			_engineLoop = Sound.Play( path, WorldPosition );
			if ( _engineLoop.IsValid() ) _engineLoopActive = true;
		}
		catch { /* path may be invalid; fail silently */ }
	}

	void StopEngineLoop()
	{
		if ( !_engineLoopActive ) return;
		try { _engineLoop.Stop(); } catch { }
		_engineLoopActive = false;
	}

	void TickSound( float dt )
	{
		// Start / stop the engine loop based on whether the engine is running.
		if ( IsEngineRunning )
		{
			if ( !_engineLoopActive ) StartEngineLoopIfNeeded();
			if ( _engineLoopActive )
			{
				try
				{
					_engineLoop.Position = WorldPosition;
					var rpm01 = MathX.Clamp( EngineRpm / MathF.Max( 1f, RedlineRpm ), 0f, 1f );
					_engineLoop.Pitch = MathX.Lerp( EngineMinPitch, EngineMaxPitch, rpm01 );
					_engineLoop.Volume = EngineVolume;
				}
				catch { /* property may differ across sbox versions */ }
			}
		}
		else if ( _engineLoopActive )
		{
			StopEngineLoop();
		}
	}

	void OnHornFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		var path = Config?.HornSoundPath;
		if ( string.IsNullOrEmpty( path ) ) return;
		try { Sound.Play( path, WorldPosition ); } catch { }
	}

	void OnEngineStartedFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		StartEngineLoopIfNeeded();
	}

	void OnEngineStoppedFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		StopEngineLoop();
	}

	void SoundSubscribe()
	{
		VehicleEvents.OnHorn += OnHornFromBus;
		VehicleEvents.OnEngineStarted += OnEngineStartedFromBus;
		VehicleEvents.OnEngineStopped += OnEngineStoppedFromBus;
	}

	void SoundUnsubscribe()
	{
		VehicleEvents.OnHorn -= OnHornFromBus;
		VehicleEvents.OnEngineStarted -= OnEngineStartedFromBus;
		VehicleEvents.OnEngineStopped -= OnEngineStoppedFromBus;
		StopEngineLoop();
	}
}
