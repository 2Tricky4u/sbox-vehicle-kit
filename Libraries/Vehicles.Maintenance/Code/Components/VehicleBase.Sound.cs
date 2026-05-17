using Sandbox;
using System;

namespace Vehicles.Maintenance;

// Audio layer. A looping engine sound whose PITCH tracks EngineRpm, plus
// one-shots for engine start/stop, gear shifts, horn and tyre skid.
//
// Asset paths come from VehicleConfig (Audio group). Any empty path is
// skipped silently, so the library is mute until you author .sound assets
// and assign them. IMPORTANT: mark the EngineSoundPath .sound asset as
// LOOPING in the sound editor — the loop relies on the asset's loop flag.
//
// All SoundHandle access is wrapped defensively: if a member/name differs in
// your s&box build it degrades to silence rather than throwing.
public sealed partial class VehicleBase
{
	// Pitch multiplier at idle vs. redline. Tune to taste per project.
	const float EngineMinPitch = 0.55f;
	const float EngineMaxPitch = 2.10f;

	SoundHandle _engineLoop;

	void TickSound( float dt )
	{
		if ( IsEngineRunning )
		{
			EnsureEngineLoop();
			if ( _engineLoop is not null )
			{
				try
				{
					var rpmFrac = RedlineRpm > 1f
						? MathX.Clamp( EngineRpm / RedlineRpm, 0f, 1f )
						: 0f;
					_engineLoop.Pitch = MathX.Lerp( EngineMinPitch, EngineMaxPitch, rpmFrac );
					_engineLoop.Volume = 0.55f + 0.45f * MathX.Clamp( MathF.Abs( ThrottleInput ), 0f, 1f );
					_engineLoop.Position = WorldPosition;
				}
				catch { /* SoundHandle member differs in this build */ }
			}
		}
		else
		{
			StopEngineLoop();
		}
	}

	void EnsureEngineLoop()
	{
		if ( _engineLoop is not null ) return;
		if ( string.IsNullOrEmpty( Config?.EngineSoundPath ) ) return;
		try { _engineLoop = Sound.Play( Config.EngineSoundPath, WorldPosition ); }
		catch { _engineLoop = null; }
	}

	void StopEngineLoop()
	{
		if ( _engineLoop is null ) return;
		try { _engineLoop.Stop( 0.15f ); } catch { }
		_engineLoop = null;
	}

	void PlayOneShot( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return;
		try { Sound.Play( path, WorldPosition ); }
		catch { /* path invalid or sound system unavailable */ }
	}

	// ── Event bus handlers (filter by v == this; static bus) ──────────
	void OnHornFromBus( VehicleBase v )
	{
		if ( v == this ) PlayOneShot( Config?.HornSoundPath );
	}

	void OnEngineStartedFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		PlayOneShot( Config?.EngineStartSoundPath );
		EnsureEngineLoop();
	}

	void OnEngineStoppedFromBus( VehicleBase v )
	{
		if ( v == this ) StopEngineLoop();
	}

	void OnShiftedFromBus( VehicleBase v, int oldGear, int newGear )
	{
		if ( v == this ) PlayOneShot( Config?.GearShiftSoundPath );
	}

	void OnSkidStartedFromBus( VehicleBase v, int wheelIdx )
	{
		if ( v == this ) PlayOneShot( Config?.SkidSoundPath );
	}

	void SoundSubscribe()
	{
		VehicleEvents.OnHorn += OnHornFromBus;
		VehicleEvents.OnEngineStarted += OnEngineStartedFromBus;
		VehicleEvents.OnEngineStopped += OnEngineStoppedFromBus;
		VehicleEvents.OnShifted += OnShiftedFromBus;
		VehicleEvents.OnWheelSkidStarted += OnSkidStartedFromBus;
	}

	void SoundUnsubscribe()
	{
		VehicleEvents.OnHorn -= OnHornFromBus;
		VehicleEvents.OnEngineStarted -= OnEngineStartedFromBus;
		VehicleEvents.OnEngineStopped -= OnEngineStoppedFromBus;
		VehicleEvents.OnShifted -= OnShiftedFromBus;
		VehicleEvents.OnWheelSkidStarted -= OnSkidStartedFromBus;
		StopEngineLoop();
	}
}
