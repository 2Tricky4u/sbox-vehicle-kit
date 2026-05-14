using Sandbox;

namespace Vehicles.Maintenance;

// Audio layer — minimal compile-safe version. Plays one-shot sounds on
// engine on/off and horn events. Looping engine + RPM-pitch deferred until
// the s&box SoundHandle property/method names are verified for this version
// (see TODO §1 sound task — reintroduce pitching once we know the API).
//
// Asset paths come from VehicleConfig.EngineSoundPath / HornSoundPath. If
// either is empty, the corresponding playback is skipped silently.
public sealed partial class VehicleBase
{
	void TickSound( float dt )
	{
		// Reserved — when SoundHandle pitching API is verified we'll update
		// the engine loop's Pitch from EngineRpm here. No-op for now.
	}

	void PlayOneShot( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return;
		try { Sound.Play( path, WorldPosition ); }
		catch { /* path may be invalid or sound system unavailable */ }
	}

	void OnHornFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		PlayOneShot( Config?.HornSoundPath );
	}

	void OnEngineStartedFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		PlayOneShot( Config?.EngineSoundPath );
	}

	void OnEngineStoppedFromBus( VehicleBase v )
	{
		if ( v != this ) return;
		// No "stop" sound asset by convention — the engine loop simply ends
		// when the start sound finishes (since we're not looping yet).
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
	}
}
