# Build Guide — Universal Vehicle & Maintenance Library for s&box DarkRP gamemodes

This is the actionable build guide. The companion file [`TECH_REFERENCE.md`](./TECH_REFERENCE.md) is the distilled doc/API reference — keep it open while you code.

> **⚠ STATUS 2026-05-17 — read before trusting the physics prose below.**
> The wheel sim was rewritten from force-based to a **kinematic controller**. Anywhere this doc says *"engine via `Body.Velocity +=`"*, *"`ApplyForceAt`"*, *"Source 2 integrates next physics step"*, or *"contact-clamp workaround"* (notably the architecture box ~L68, the data-flow diagram ~L116-119, Step 5d ~L429, and the feasibility list ~L563) that is **obsolete** — superseded by **[Step 5](#step-5--wheel-sim-raycast-suspension-owner-authoritative)**, which is the single source of truth. Reality: `Body.MotionEnabled=false`, manual `WorldPosition += _vel*dt`. Feel knobs (documented nowhere else): `VehicleMass` (Config.MassKg-backed, prevents Body.Mass==0 NaN), `MaxForwardAccelMs2` (perceived-mass accel clamp), `(1-ratio²)` top-speed taper, `FullSteerSpeedKmh` + negated-yaw steering.
> **Also:** a stale force-based duplicate of this project exists at `…\firstaddon\` — the canonical project is `…\carmaintenance\`. Don't open `firstaddon` in s&box.

> **✅ STATUS 2026-07-10 — production-readiness pass landed (Phases 0–6, see git log).**
> The 2026-06-13 audit issues are fixed: crash damage now fires organically from the wall sweep + landing detection (the dead `ICollisionListener` path was removed); engine power is data-driven (`EngineTorqueNm × TorqueToForceScale` + `AccelerationCurve` taper; `EffectiveTorque` deleted); engine events are single-raise edge-detected; the battery has an alternator; networking is proxy-safe (`ShouldSimulate => Network.Active ? !IsProxy : LocalSimulation`, owner-only `[Sync]` seeding, `NetworkOrphaned.Host`, `VehiclePersistence.RestoreOwnershipFor`). New since: `IsWrecked` + recovery (`RecoverUprightRpc`, `OnVehicleStuck`), occupant ride-along + eject, guarded per-subscriber event bus, `vh.push/vh.wreck/vh.unstuck` dev commands, **`vh.spawn` works** (sedan + hatchback prefabs), 6 `.vtune` presets, pump/pickup/ramp/wall playtest props in `minimal.scene`, cloud audio wired. Steps 7–11 are code-complete; remaining: in-person playtest, 2-client smoke test, publish metadata.

---

## 0. Goal & non-goals

**Goal.** Ship a *gamemode-agnostic* s&box code library called (working name) `Vehicles.Maintenance` that any DarkRP-style gamemode (e.g. [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP), [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public)) can drop in and use to get:

- A shared `VehicleBase` component every car uses (fuel, engine health, body health, tire wear, battery, oil)
- **Multiple distinct cars defined entirely as data** — each new car is a `.vcfg` GameResource + `.vmdl` model + `.prefab`, with its own stats. **Zero C# changes** to add a car.
- **Player-facing tuning via `.vtune` GameResource** — Sport / Drift / Rally / Heavy / Grip / Arcade presets layer multipliers over the base config. Damaged engine ⇒ reduced effective power; worn tires ⇒ reduced effective grip — *the maintenance loop has direct driving consequences.*
- A "light powertrain" — RPM, current gear, auto-shift, per-gear torque multiplier, engine braking — fake-but-convincing, no real simulation.
- Input smoothing — keyboard taps ramp into analog feel instead of binary on/off.
- A "Mechanic" gameplay loop (diagnostic → buy parts → repair → get paid)
- Clean adapter points so each gamemode plugs in its own currency / job / inventory
- Event bus for VFX/audio/RP hooks: `OnShifted`, `OnEngineRpmRedlined`, `OnTirePunctured`, `OnWheelSkidStarted`, `OnDoorOpened`, etc.

**v1 ships ONE car (a sedan)** to prove the whole pipeline end-to-end in multiplayer. Once that works, every additional car (pickup, sportscar, motorcycle, police cruiser, …) is a 5-minute author task using the schema below.

**Driving fidelity:** *arcade-plus* — physics-based with raycast wheel suspension, contact-basis friction, downforce, air drag, and a layered acceleration model (engine power × tune multiplier × gear multiplier × maintenance health). NOT a simulator. See [Section 6 — What we deliberately don't simulate](#6-what-we-deliberately-dont-simulate) for the explicit rationale and the upgrade path if you ever want full simulation later.

**Non-goals (v1).**
- Realistic detachable parts (Car Mechanic Simulator's headline feature) — abstracted as health states.
- Paint shop, tuning/upgrades UI in-game — `.vtune` presets cover the gameplay angle; live tuning UI is future polish.
- Full driving simulator fidelity — no real clutch state machine, no real differential, no slip-ratio tire model. See Section 6.

---

## 1. Critical project-type pivot — read this before opening the editor

Per the official docs ([Project Types — Addon](https://sbox.game/dev/doc/getting-started/project-types/addon-project) and [Code → Libraries](https://sbox.game/dev/doc/code/libraries)):

| Project type | Can host C# code? | How others consume it |
|---|---|---|
| **Game** | Yes | Standalone — published as a game on sbox.game |
| **Addon** | **No** (assets/actiongraph only, "yet" per docs) | Targets a single host game; assets are individually published |
| **Library** | **Yes** | Lives in another project's `Libraries/` folder; managed by the in-editor Library Manager |

Your current `firstaddon.sbproj` says `"Type": "addon"` — **that cannot ship C#**. For a reusable, multi-gamemode system you have two viable shapes:

1. **Library** — what you actually want for the *core* code. Rename/recreate as `Type: library`. This is the canonical sbox way to share C# between projects. *Caveat from docs:* "Libraries cannot reference other libraries." Self-contain everything; build vehicle physics on the engine's built-in `Rigidbody` rather than depending on another library like `sbox-libwheel`.
2. **Game (sandbox-style) for development + Library for distribution** — keep a small "host" game project for in-editor testing of the library, and put the actual library code in a sibling `Libraries/Vehicles.Maintenance/` folder. This is the realistic dev setup.

> **Action.** Rename `firstaddon` to `vehicles-maintenance-host` (a game project for testing) and create the actual library at `vehicles-maintenance-host/Libraries/Vehicles.Maintenance/`. Each consuming gamemode then drops the same `Vehicles.Maintenance` folder into *their* `Libraries/` (or installs via the in-editor Library Manager once published).

Reference: [Project Types index](https://sbox.game/dev/doc/getting-started/project-types) · [Code → Libraries](https://sbox.game/dev/doc/code/libraries) · [Code Basics](https://sbox.game/dev/doc/code/code-basics)

---

## 2. Architecture (one-page view)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                  Vehicles.Maintenance  (LIBRARY)                         │
│                                                                          │
│  Components/  (VehicleBase split into partial files by concern)          │
│    VehicleBase.cs            – Config, lifecycle, OnAwake/Update         │
│    VehicleBase.State.cs      – [Sync] Fuel/EngineHealth/TireWear + RPCs  │
│    VehicleBase.Damage.cs     – Collision damage + wear ticking           │
│    VehicleBase.Input.cs      – Raw WASD → _raw* fields                   │
│    VehicleBase.InputFilter.cs – Smooths raw→ThrottleInput/SteerInput     │
│    VehicleBase.Tune.cs       – Reads VehicleTuneProfile, exposes         │
│                                Effective* props (tune × maintenance)     │
│    VehicleBase.Powertrain.cs – [Sync] CurrentGear/EngineRpm,             │
│                                auto-shift, GearTorqueMultiplier          │
│    VehicleBase.Wheels.cs     – Arcade wheel sim (suspension, friction,   │
│                                engine via Velocity+=, downforce, drag)   │
│    VehicleBase.Systems.cs    – [Sync] EngineOn/Doors/Lights/Punctures    │
│                                + Toggle*Rpc, IsEngineRunning gate        │
│    RepairTool.cs             – Held tool, opens DiagnosticPanel          │
│    FuelPump.cs               – Static prop component                     │
│    PartItemPickup.cs         – World-pickup wrapper for part items       │
│                                                                          │
│  Resources/                                                              │
│    VehicleConfig.cs          – [AssetType] declarative car def (.vcfg)   │
│    VehicleTuneProfile.cs     – [AssetType] driving preset (.vtune)       │
│    PartDefinition.cs         – [AssetType] part SKU (.partdef)           │
│                                                                          │
│  Contracts/                                                              │
│    IVehicleHost.cs           – Per-gamemode adapter (currency/job)       │
│    IPartInventory.cs         – Per-gamemode inventory adapter            │
│    IRepairAction.cs          – Per-part custom repair behaviour          │
│                                                                          │
│  UI/                                                                     │
│    DiagnosticPanel.razor     – Mechanic's repair UI                      │
│    FuelGauge.razor           – HUD when seated in vehicle                │
│                                                                          │
│  Events/                                                                 │
│    VehicleEvents.cs          – Static event bus (lifecycle, maintenance, │
│                                powertrain, systems, skid/damage)         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Data flow (per fixed step)

```
   Driver input (WASD)
         │
         ▼
   VehicleBase.Input.cs            ─── reads Input.AnalogMove/Down → _raw*
         │
         ▼
   VehicleBase.InputFilter.cs      ─── Approach()-ramps _raw* → ThrottleInput/SteerInput
         │
         ▼
   VehicleBase.Powertrain.cs       ─── auto-shifts gear, updates RPM
         │
         ▼                                ┌─ VehicleConfig (base stats)
   VehicleBase.Tune.cs             ◄──────┤  VehicleTuneProfile (multipliers)
         │                                └─ MaintenanceState (health factors)
         │   exposes Effective* props
         ▼
   VehicleBase.Wheels.cs           ─── raycast suspension, friction, engine
         │                              (Velocity+= for engine; ApplyForceAt for
         │                              suspension/friction; downforce; air drag)
         ▼
   Body.Velocity / Rigidbody       ─── Source 2 integrates next physics step

   ⤷ VehicleBase.Systems.cs         ─── ticks alongside (engine on/off,
                                        puncture detection, event firing)
   ⤷ VehicleEvents (static)         ─── gamemode/UI/audio subscribe here
```

Mark the wheel-sim layer mentally as a *swappable backend* — today it's the arcade solver in `VehicleBase.Wheels.cs`, but the rest of the stack (Input → InputFilter → Powertrain → Tune → events → systems) sits cleanly above it. If you ever want simulation-grade physics later, swap the contents of `Wheels.cs` (or hide it behind an interface) without touching anything else. See Section 6.

### Consumers

```
                              ▲
                              │ (Library Manager / cloned folder)
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
┌───────────────┐  ┌─────────────────────┐  ┌──────────────────┐
│ sousou63/     │  │ dxura/dxrp-public   │  │ AnyOtherDarkRP   │
│ DarkRP (game) │  │ (game)              │  │ (game)           │
│               │  │                     │  │                  │
│ Implements    │  │ Implements          │  │ Implements       │
│ IVehicleHost  │  │ IVehicleHost        │  │ IVehicleHost     │
│ Registers     │  │ Registers           │  │ Registers        │
│ MechanicJob   │  │ MechanicJob         │  │ MechanicJob      │
│ Drops         │  │ Drops               │  │ Drops            │
│ VehicleConfig │  │ VehicleConfig       │  │ VehicleConfig    │
│ assets        │  │ assets              │  │ assets           │
└───────────────┘  └─────────────────────┘  └──────────────────┘
```

**Key idea — the library never references a specific gamemode.** It declares contracts (`IVehicleHost` etc.) and fires events. Each gamemode writes a tiny adapter that implements those contracts with its own job/economy systems. This is what makes the same library usable by sousou63 *and* dxura without changes.

> **Library scope rule from the docs:** "Libraries cannot reference other libraries" and "a library cannot access the game code directly." (`Code → Libraries`). The contracts pattern above respects both — the gamemode injects an `IVehicleHost` instance into the library at boot time.

---

## 3. Step-by-step build plan

Each step lists the official docs to consult. Open them as you go.

### Step 1 — Restructure project as game-host + library ✅ DONE

1. In `firstaddon.sbproj`, change `"Type": "addon"` → `"Type": "game"` (or create a fresh game project). Update `Title`/`Ident` accordingly.
   - Ref: [Project Types](https://sbox.game/dev/doc/getting-started/project-types)
2. Create `Libraries/Vehicles.Maintenance/` with subfolders `Code/`, `Assets/`, `Editor/`.
   - Ref: [Code → Libraries](https://sbox.game/dev/doc/code/libraries) — required folder layout.
3. Decide the library's package name + organisation slug now (you can't easily rename later once published). Suggest `vehicles-maintenance` ident under your org slug.
4. Pick naming conventions and apply consistently — assets lowercase, snake_case for textures with the `_color/_normal/_rough` suffixes used by the auto-material flow.
   - Ref: [Assets → Naming Conventions](https://sbox.game/dev/doc/assets/naming-conventions)
5. Pick the test scene (you already have `scenes/minimal.scene`); add a flat plane + spawn point for vehicle tests.
   - Ref: [Scene system](https://sbox.game/dev/doc/scene)

### Step 2 — Define the `VehicleConfig` GameResource (data first) ✅ DONE

Before any physics, lock down the data shape. This is what gamemode authors will fill in to add new cars without C# changes.

Create `Libraries/Vehicles.Maintenance/Code/Resources/VehicleConfig.cs`. Schema is **locked** at v1 — every field below is in the inspector for every car you create. Anything beyond this list goes through a gamemode `ResourceExtension<VehicleConfig, ...>` (see [TECH_REFERENCE §10](./TECH_REFERENCE.md#10-gameresource-extensions)).

```csharp
[GameResource( "Vehicle Config", "vcfg",
    "Declarative definition for a maintainable vehicle.",
    Icon = "directions_car",
    Category = "Vehicles" )]
public sealed class VehicleConfig : GameResource
{
    // ─── Identity ─────────────────────────────────────────────────────
    [Property] public string DisplayName { get; set; } = "Sedan";
    [Property, ResourceType( "vmdl" )]   public string ModelPath  { get; set; }
    [Property, ResourceType( "prefab" )] public string PrefabPath { get; set; }

    // ─── Performance ──────────────────────────────────────────────────
    [Group( "Performance" ), Property, Range( 20, 400 )]
    public float MaxSpeedKmh { get; set; } = 140;

    [Group( "Performance" ), Property]
    public float MassKg { get; set; } = 1400;

    [Group( "Performance" ), Property, Range( 0, 1 )]
    public float Grip { get; set; } = 0.85f;

    [Group( "Performance" ), Property]
    public float EngineTorqueNm { get; set; } = 250;

    [Group( "Performance" ), Property, Range( 0, 5 )]
    public float BrakeStrength { get; set; } = 1.0f;

    /// <summary>X = normalized speed (0..1), Y = torque multiplier.</summary>
    [Group( "Performance" ), Property]
    public Curve AccelerationCurve { get; set; } = new Curve( new[] {
        new Curve.Frame( 0f, 1.0f ),
        new Curve.Frame( 0.5f, 0.7f ),
        new Curve.Frame( 1f, 0.0f )
    } );

    // ─── Capacity ─────────────────────────────────────────────────────
    [Group( "Capacity" ), Property, Range( 2, 8 )]
    public int WheelCount { get; set; } = 4;

    [Group( "Capacity" ), Property, Range( 1, 16 )]
    public int SeatCount { get; set; } = 4;

    [Group( "Capacity" ), Property]
    public float CargoCapacityKg { get; set; } = 0;

    // ─── Maintenance ──────────────────────────────────────────────────
    [Group( "Maintenance" ), Property]
    public float FuelCapacityLitres { get; set; } = 50;

    [Group( "Maintenance" ), Property]
    public float FuelConsumptionLPer100Km { get; set; } = 7.5f;

    [Group( "Maintenance" ), Property]
    public float EngineMaxHealth { get; set; } = 100;

    [Group( "Maintenance" ), Property]
    public float BodyMaxHealth { get; set; } = 100;

    // ─── Audio ────────────────────────────────────────────────────────
    [Group( "Audio" ), Property, ResourceType( "sound" )]
    public string EngineSoundPath { get; set; }

    [Group( "Audio" ), Property, ResourceType( "sound" )]
    public string HornSoundPath { get; set; }

    // ─── Cosmetics ────────────────────────────────────────────────────
    [Group( "Cosmetics" ), Property]
    public bool PaintTintable { get; set; } = true;

    /// <summary>Free-form labels — gamemodes filter on these
    /// (e.g. "civilian", "police", "luxury", "starter").</summary>
    [Group( "Cosmetics" ), Property]
    public List<string> Tags { get; set; } = new();

    // ─── Economy ──────────────────────────────────────────────────────
    [Group( "Economy" ), Property]
    public int PurchasePrice { get; set; } = 5000;

    [Group( "Economy" ), Property]
    public int RepairBaseCost { get; set; } = 50;

    // ─── Lookup ───────────────────────────────────────────────────────
    public static VehicleConfig Find( string ident ) =>
        ResourceLibrary.GetAll<VehicleConfig>()
            .FirstOrDefault( v => v.ResourceName == ident );

    public static IEnumerable<VehicleConfig> WithTag( string tag ) =>
        ResourceLibrary.GetAll<VehicleConfig>()
            .Where( v => v.Tags.Contains( tag ) );
}
```

Refs:
- [Custom Assets / GameResource](https://sbox.game/dev/doc/assets/resources/custom-assets) — `[GameResource]` attribute, file extension rules (lowercase, ≤8 chars), `ResourceLibrary.Get<T>()` lookup.
- [Property Attributes](https://sbox.game/dev/doc/editor/property-attributes) — `[Property] [Group] [Range] [ResourceType]`.

> Once this exists, every vehicle in every gamemode is just a `.vcfg` file in their addon's `Assets/`. That's the "easy to extend" property the user wants.

### Step 3 — Define the `IVehicleHost` contract (the abstraction that makes it gamemode-agnostic) ✅ DONE

Create `Libraries/Vehicles.Maintenance/Code/Contracts/IVehicleHost.cs`:

```csharp
public interface IVehicleHost
{
    // Currency
    bool TryCharge( Connection player, int amount, string reason );
    void Pay( Connection player, int amount, string reason );

    // Job system
    bool IsMechanic( Connection player );

    // Inventory (parts, repair tool)
    IPartInventory GetInventory( Connection player );

    // Persistence (vehicle ownership across map reloads)
    void SaveVehicleOwnership( Guid vehicleId, ulong steamId, VehicleConfig cfg );
    bool TryLoadVehicleOwnership( Guid vehicleId, out ulong steamId, out VehicleConfig cfg );
}

public static class VehicleHost
{
    public static IVehicleHost Current { get; private set; }
    public static void Register( IVehicleHost host ) => Current = host;
}
```

Each consuming gamemode writes a ~50-line `MyDarkRPVehicleHost : IVehicleHost` and calls `VehicleHost.Register( this )` once at startup. The library never depends on their types.

Ref: [API Whitelist](https://sbox.game/dev/doc/code/code-basics/api-whitelist) — confirms libraries get fewer restrictions than game code; safe to expose interfaces.

### Step 4 — Implement `VehicleBase` with split partials ✅ DONE

Use the standard sbox component lifecycle. Split into multiple `partial class` files for clarity.

`VehicleBase.cs` — config + lifecycle:

```csharp
public sealed partial class VehicleBase : Component, Component.ITriggerListener
{
    [Property] public VehicleConfig Config { get; set; }
    [RequireComponent] public Rigidbody Body { get; set; }

    protected override void OnAwake()
    {
        Body.MassOverride = Config?.MassKg ?? 1400f;
    }

    protected override void OnUpdate()
    {
        TickInput();
        TickWear( Time.Delta );
    }

    protected override void OnFixedUpdate() => SimulateWheels();
}
```

`VehicleBase.State.cs` — networked maintenance state:

```csharp
public sealed partial class VehicleBase
{
    [Sync] public float Fuel { get; set; }
    [Sync] public float EngineHealth { get; set; }
    [Sync] public float BodyHealth { get; set; }
    [Sync, Change] public NetList<float> TireWear { get; set; } = new();

    public float FuelPct => Config is null ? 0 : Fuel / Config.FuelCapacityLitres;

    [Rpc.Owner] public void RefuelRpc( float litres )
    {
        Fuel = Math.Min( Fuel + litres, Config.FuelCapacityLitres );
        VehicleEvents.OnRefuel?.Invoke( this, litres );
    }

    [Rpc.Owner] public void RepairRpc( PartKind part, float amount )
    {
        switch ( part )
        {
            case PartKind.Engine: EngineHealth = Math.Min( EngineHealth + amount, Config.EngineMaxHealth ); break;
            case PartKind.Body:   BodyHealth   = Math.Min( BodyHealth   + amount, Config.BodyMaxHealth );   break;
        }
        VehicleEvents.OnRepair?.Invoke( this, part, amount );
    }
}
```

Refs you'll need:
- [Networking](https://sbox.game/dev/doc/networking) — `Networking.CreateLobby/Connect`, `NetworkSpawn()`, host vs client.
- [Property Attributes](https://sbox.game/dev/doc/editor/property-attributes) — `[Sync]` (synced over network), `[Change]` (callback on change), `[Rpc.Owner] / [Rpc.Broadcast]`.
- [Code Cheat Sheet](https://sbox.game/dev/doc/code/code-basics/cheat-sheet) — `Time.Delta`, `Log.Info`.
- [Physics](https://sbox.game/dev/doc/physics) — Rigidbody, raycast for wheels.

### Step 5 — Wheel sim (raycast suspension, owner-authoritative) ✅ DONE

**Final approach: kinematic controller** (rewritten 2026-05-15 after the force-based port hit an unfixable Source 2 wall — see below). `VehicleBase.Wheels.cs` no longer uses physics integration at all.

History: we first ported [`matekdev/sbox-arcade-car-physics`](https://github.com/matekdev/sbox-arcade-car-physics) (MIT, ← [`SergeyMakeev/ArcadeCarPhysics`](https://github.com/SergeyMakeev/ArcadeCarPhysics)) using `ApplyForceAt`. Source 2's contact damping silently capped velocity at ~50 km/h whenever the body collider touched ground (regardless of `Friction=0`). `IsTrigger=true` dodged the cap but the body fell through the floor. `Facepunch/sbox-libwheel` was evaluated but uses the same `ApplyForceAt` pattern so wouldn't help. **Resolution: own the integration.**

Kinematic implementation (see `feedback_sbox_physics_quirks` memory for full detail):
- **`Body.MotionEnabled = false`** in `OnAwake` — Source 2 stops integrating the body, so its contact damping never runs. Collider stays for detection (Damage.cs `OnCollision` still fires).
- **Own `Vector3 _vel`** (inch units). Each `OnFixedUpdate`: manual gravity (`9.81/0.0254` in/s²) → per-wheel raycast suspension (summed → `_vel.z`) → engine force (forward) → lateral-grip damping → brake → air drag → downforce → steering (`WorldRotation *= FromYaw(speed-scaled yawRate)`).
- **Integrate by hand:** `WorldPosition += _vel * dt`. This is the cap bypass.
- **Mirror** `Body.Velocity = _vel` end-of-tick for debug/`Damage.cs` consumers.
- **s&box uses inches** — m/s ↔ inches/sec is `* 0.0254`; km/h is `* 0.09144`.
- v1 limitations (documented, deferred to v1.1): walls don't physically block (body ghosts; collision events still fire); no pitch/roll (yaw-only); per-wheel suspension summed (no differential pitch). Future fix for walls = forward shapecast before the position write + slide along hit normal.

Ref: [Physics → Tracing](https://sbox.game/dev/doc/physics) — `Scene.Trace.Ray(...)`.

### Step 5b — Tuning profile + maintenance binding ✅ DONE

The "Best Pick" from the deep-research review: player-facing tuning multipliers with **zero new physics math**. Just constants flowing into the existing arcade solver.

`Resources/VehicleTuneProfile.cs` — `[AssetType(Extension = "vtune")]` GameResource with multipliers:
- `EnginePowerMultiplier`, `BrakeMultiplier`, `SteeringMultiplier`
- `SuspensionStiffnessMultiplier`, `SuspensionDampingMultiplier`
- `FrontGripMultiplier`, `RearGripMultiplier`
- `DownforceMultiplier`

`Components/VehicleBase.Tune.cs` (partial) — exposes `Effective*` properties combining `base × tune × maintenance health`:
- `EffectiveEnginePower = MaxEngineForce × Tune.EnginePowerMultiplier × EngineHealthFactor`
- `EffectiveWheelGrip(i) = LateralFriction × Tune.FrontGripMultiplier_or_RearGripMultiplier × TireHealthFactor(i) × punctureFactor`
- Same pattern for brake, steering (× BodyHealthFactor), suspension, downforce

**The gameplay payoff:** a damaged engine literally drops `EffectiveEnginePower` proportionally — the player *feels* the maintenance loop. Tire wear reduces per-wheel grip. Repairing restores the multipliers. This is what makes the mechanic job worth playing.

**Author six presets** in the editor (Asset Browser → New → Vehicle Tune): `grip.vtune`, `drift.vtune`, `rally.vtune`, `heavy.vtune`, `sport.vtune`, `arcade.vtune`. Assign to a vehicle's `Tune` slot for instant feel.

Ref: [Custom Assets / GameResource](https://sbox.game/dev/doc/assets/resources/custom-assets).

### Step 5c — Input smoothing ✅ DONE

`Components/VehicleBase.InputFilter.cs` (partial) — `Approach()`-ramps raw keyboard input into analog `ThrottleInput`/`SteerInput`:
- Independent rise/fall rates per channel (rises slower on throttle, falls faster on steer for snappy re-center)
- Deadzone snap to 0 below `InputDeadzone`
- Brake/handbrake stay binary (analog brake pressure is future polish)

Result: keyboard taps feel "wound up" instead of binary; steering ramps in/out smoothly.

### Step 5d — Light powertrain ✅ DONE

`Components/VehicleBase.Powertrain.cs` (partial) — **fake-but-convincing** RPM + gear simulation. Explicitly **NOT** a real powertrain (no clutch state machine, no real differential, no per-wheel torque solving — see Section 7).

- `[Sync] int CurrentGear` (-1 reverse, 0 neutral, 1..N forward)
- `[Sync] float EngineRpm`
- `[Property] float[] ForwardGearTorqueMultipliers = { 2.5f, 1.8f, 1.3f, 1.0f, 0.8f }` — 1st gear strongest, top gear weakest
- Auto-shift at `ShiftUpRpm` / `ShiftDownRpm`
- `EngineBrakingForce` slows the car when coasting in gear
- Fires `VehicleEvents.OnShifted(v, oldGear, newGear)` and `OnEngineRpmRedlined(v)`

The engine force in `Wheels.cs` becomes `ThrottleInput × EffectiveEnginePower × GearTorqueMultiplier`. Same `Body.Velocity +=` mechanism — just scaled by gear.

### Step 5e — Vehicle systems + event bus ✅ DONE

`Components/VehicleBase.Systems.cs` (partial) — synced toggles + event-driven systems:
- `[Sync] bool EngineOn` + `ToggleEngineRpc()`
- `[Sync] bool HeadlightsOn` + `ToggleHeadlightsRpc()`
- `[Sync] uint DoorMask` (bit per door) + `ToggleDoorRpc(idx)`
- `[Sync] uint TirePunctureMask` (bit per wheel) — auto-set when `TireWear[i] >= 1.0`; cleared by repair RPC
- Public `IsEngineRunning => CanStartEngine && EngineOn` — the unified "should the engine produce power" gate used everywhere

`Wheels.cs` fires `VehicleEvents.OnWheelSkidStarted(v, wheelIdx)` and `OnWheelSkidStopped(v, wheelIdx)` when lateral velocity crosses `SkidLateralThreshold` — VFX/audio hooks plug in here.

New events in `VehicleEvents.cs`: `OnEngineStarted/Stopped`, `OnDoorOpened/Closed`, `OnTirePunctured`, `OnWheelSkidStarted/Stopped`, `OnHorn`, `OnHeadlightsToggled`, `OnShifted`, `OnEngineRpmRedlined`.

### Step 6 — Maintenance behaviour gates ✅ DONE

Implemented across `VehicleBase.State.cs` (synced state + `CanStartEngine` gate), `VehicleBase.Tune.cs` (`EngineHealthFactor`, `TireHealthFactor`, `BodyHealthFactor`), `VehicleBase.Systems.cs` (`IsEngineRunning`, `TirePunctureMask`), and `VehicleBase.Damage.cs` (collision + wear ticking):

- `Fuel <= 0.1` OR `EngineHealth < 5` → `CanStartEngine` returns false → engine produces no force.
- `EngineHealth < 50%` of max → `EngineHealthFactor` scales linearly down to 0.1 (limp-home minimum).
- `TireWear[i]` rising → per-wheel `TireHealthFactor(i)` drops grip toward 0.3.
- `TireWear[i] >= 1.0` → auto-sets bit in `TirePunctureMask` → that wheel's grip drops to 20% (car visibly pulls toward the flat).
- `BodyHealth < 50%` → `BodyHealthFactor` reduces effective steering angle (sloppier handling when battered).
- `RepairRpc(PartKind.Tire, amount, wheelIdx)` clears the puncture bit when wear drops below 95%.

`TickWear()` (`VehicleBase.Damage.cs`) decrements `Fuel` from speed × consumption, increments `TireWear` under hard cornering. `TickSystems()` (`VehicleBase.Systems.cs`) keeps `EngineOn` / `TirePunctureMask` in sync with the underlying state.

### Step 7 — Repair Tool, Fuel Pump, Diagnostic UI

`RepairTool.cs` — equip-and-aim component. On primary fire: trace forward, if hit a `VehicleBase`, **only** if `VehicleHost.Current.IsMechanic(player)`, open the diagnostic panel for that vehicle. On secondary fire: cancel.

`FuelPump.cs` — static prop with a use action. On use: read fuel litres requested, charge `VehicleHost.Current.TryCharge(player, litres * pricePerL, "Fuel")`, call `vehicle.RefuelRpc(litres)`.

`UI/DiagnosticPanel.razor` — Razor screen panel listing each part, current/max health, "Repair (cost X)" button. Per click: confirm the player owns the right `PartDefinition` in their `IPartInventory`, charge them, call `vehicle.RepairRpc(part, amount)`. Pay the mechanic via `VehicleHost.Current.Pay(...)`.

Refs:
- [UI overview](https://sbox.game/dev/doc/ui)
- [UI basics](https://sbox.game/dev/doc/ui/ui-basics) — Panels = C# classes, support Razor (`.razor`), `PanelComponent` is the root, `ScreenPanel` for HUD.
- [Input](https://sbox.game/dev/doc/gameplay/input) — `Input.Pressed/Down/Released`, `Input.EscapePressed`.

### Step 8 — Persistence

`VehicleHost.Current.SaveVehicleOwnership` is *up to the gamemode* — they can use their own DB. The library also offers a fallback file-based save using `FileSystem.Data` (per the docs the only allowed write target):

```csharp
FileSystem.Data.WriteJson( $"vehicles/{vehicleId}.json", saveData );
```

Ref: [Assets → File System](https://sbox.game/dev/doc/assets/file-system) — `FileSystem.Data` / `FileSystem.OrganizationData`, properties (not fields) are serialised.

### Step 9 — Build the per-gamemode adapters

This is the *small* part — each gamemode gets ~100 LoC of glue. Two example adapters to ship in your repo as proof:

**`Adapters/SouSou63DarkRP/`** — implements `IVehicleHost` against [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP)'s `Economy/`, `Jobs/`, `Items/` modules. Registers a `MechanicJob` via their job system. Lives **inside** their gamemode project (or shipped as a separate library users can drop in).

**`Adapters/DXRP/`** — same against [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public). Their layout uses an `sdk` project + entity-style code; the adapter calls into their currency API.

Each adapter is the *only* file that needs to change when you support a new gamemode.

### Step 10 — Ship ONE Sedan to prove the pipeline (v1 scope)

The whole point of `VehicleConfig` is that adding cars later is pure data — so v1 ships *one* working car end-to-end. Once that drives, refuels, takes damage, and gets repaired in multiplayer, every additional car is a 5-minute author task with no code changes:

> **The "add a new car" recipe** (post-v1, available to gamemode authors)
> 1. Drop a `.vmdl` model in `Assets/vehicles/models/<name>.vmdl`.
> 2. Right-click in Asset Browser → **New → Vehicle Config**, save as `Assets/vehicles/<name>.vcfg`.
> 3. Tune the inspector fields (all the groups in the schema above).
> 4. Make a prefab `<name>.prefab` = root GameObject + `VehicleBase` (referencing the `.vcfg`) + N wheel-anchor children + seat-anchor children.
> 5. Reference it from a dealer NPC via `VehicleConfig.Find("<name>")` or `VehicleConfig.WithTag("civilian")`.
>
> Zero C# touched. No recompile. Whether the host gamemode is sousou63/DarkRP or dxura/dxrp-public is irrelevant — both consume the same `.vcfg`.

**v1 deliverable assets:**
- `Assets/vehicles/sedan.vcfg` + `Assets/vehicles/sedan.prefab` + `Assets/vehicles/models/sedan.vmdl`
- `Assets/sounds/engine_default.sound`, `Assets/sounds/horn_default.sound`, `Assets/sounds/wrench.sound`
- `Assets/scenes/garage.scene` for in-editor testing
- A README in `Assets/vehicles/` titled "Adding a new car" pasting the recipe above so authors can self-serve

Refs:
- [Naming Conventions](https://sbox.game/dev/doc/assets/naming-conventions) for textures (`_color/_normal/_rough/_metal/_ao`).
- [Custom Assets / GameResource](https://sbox.game/dev/doc/assets/resources/custom-assets) — how the inspector window for `.vcfg` is generated.

### Step 11 — Multiplayer test

- Open the host project in the editor.
- `Networking.CreateLobby( new LobbyConfig { MaxPlayers = 4 } )`.
- Have a friend join via `Networking.Connect(lobbyId)`.
- Spawn a vehicle, drive, hit walls, verify damage state syncs.
- Switch ownership: pass `vehicle.Network.AssignOwnership(otherPlayer)` and confirm the new owner sees authoritative physics.

Ref: [Networking](https://sbox.game/dev/doc/networking) · [Lobbies](https://sbox.game/dev/doc/networking/lobbies).

### Step 12 — Polish, publish, document the contract

1. Profile in editor with `hotload_log 2` while iterating on physics tuning.
   - Ref: [Hotloading](https://sbox.game/dev/doc/code/advanced-topics/hotloading) — `[SkipHotload]` for any large static cache you add.
2. Write a README in the library folder documenting `IVehicleHost` for adapter authors.
3. Publish via the in-editor **Library Manager** (View menu) so other gamemode devs can install with one click.
   - Ref: [Code → Libraries](https://sbox.game/dev/doc/code/libraries) — Library Manager UI.

---

## 4. Effort estimate (revised, solo dev)

| Phase | Work | Time | Status |
|---|---|---|---|
| 1 | Project restructure + `VehicleConfig` data shape | 1 day | ✅ done |
| 2 | `IVehicleHost` contract + `VehicleEvents` | 0.5 day | ✅ done |
| 3 | `VehicleBase` + raycast wheels + state | 3–4 days | ✅ done (after Source-2 debugging) |
| 4 | Maintenance gates + damage routing | 1 day | ✅ done |
| 5a | `.vtune` tuning profile + Effective* binding | 1–2 days | ✅ done |
| 5b | Input filter (Approach-ramped throttle/steer) | 0.5 day | ✅ done |
| 5c | Light powertrain (RPM/gear/auto-shift/engine-brake) | 2–3 days | ✅ done |
| 5d | Vehicle systems (engine on/off, doors, lights, punctures, skid events) | 2 days | ✅ done |
| 6 | RepairTool + FuelPump + Razor diagnostic UI wiring | 3 days | scaffolded — needs gameplay test |
| 7 | Adapter for sousou63/DarkRP (proof) | 1.5 days | pending |
| 8 | Adapter for dxura/dxrp-public (proof) | 1.5 days | pending |
| 9 | 1 sedan prefab + garage scene + sounds + 6 `.vtune` presets | 1.5 days | pending |
| 10 | Multiplayer testing + polish | 2 days | pending |

**Total to publishable v1:** ~20–22 working days (one working sedan with tuned driving, mechanic loop wired, two gamemode adapters, six `.vtune` presets). Each *additional* vehicle after v1 is hours, not days — `.vmdl` + `.vcfg` + `.prefab`, optionally a tuned `.vtune`.

**Where you are right now:** ~13 days of code work done. The driving system is feature-complete for v1. Remaining is gameplay testing (Step 6 onward) + content authoring (Step 9).

---

## 5. Feasibility verdict (revised post-implementation)

**High — confirmed by code, not theory.** The library has been implemented through Step 5e. The driving system is feature-complete: a placeholder box drives convincingly with arcade-plus physics, smoothed input, auto-shifting gears, downforce, air drag, and the full event surface. Maintenance state has direct in-driving consequences (damaged engine ⇒ measurably weaker acceleration).

**What's verified working in code:**
- Arcade wheel sim (suspension + lateral grip + downforce + air drag)
- ~~Engine drive via `Body.Velocity +=` (works around Source 2's contact clamp)~~ → **superseded 2026-05-17:** kinematic controller — `Body.MotionEnabled=false`, manual `WorldPosition += _vel*dt` (see Step 5 + top-of-file STATUS banner)
- Speed cap from `Config.MaxSpeedKmh`
- Input smoothing (Approach-ramped throttle/steer)
- Auto-shifting powertrain with per-gear torque multipliers
- Engine braking when coasting in gear
- Skid event firing from physics
- Tune profile multipliers feeding all of the above
- Maintenance health factors scaling engine/grip/steering/body
- Networked `[Sync]` state + `[Rpc.Owner]` mutations across all subsystems

**What's not yet verified (Step 6 onward):**
- RepairTool aim → diagnostic UI → repair flow (scaffolded but never tested in a scene)
- FuelPump charge → refuel RPC
- Multiplayer ownership transfer
- Adapter implementations against real DarkRP gamemodes

**Two known risks remain:**

1. **"Libraries cannot reference other libraries"** ([Code → Libraries](https://sbox.game/dev/doc/code/libraries)) — we self-contained the wheel sim. If we ever want `sbox-libwheel`, we'd switch the library to a Game project type. Out of scope for v1.
2. **Gamemode adapters target moving targets** — sousou63 and dxura both refactor often. Keep adapters in *their* repos so library updates don't break.

The tuning multiplier ⇄ maintenance binding is the *gameplay payoff* of this project: repairing a car isn't a number going up, it's a measurably stronger drive. That binding is now in code.

---

## 6. What we deliberately don't simulate

The [`deep-research-report.md`](../deep-research-report.md) (which surveys Glide, GAuto, simfphys, SCars, LVS) outlines a full-simulation upgrade path — real clutch state machines, real differential, slip-ratio/slip-angle tire model, host-validated networking, an `IWheelContactSolver` interface with a `libwheel` backend. We deliberately did **not** ship any of these in v1. Here's why each, and what it would cost to revisit.

| What we don't simulate | Why we skip | If you ever want it |
|---|---|---|
| **Real clutch state machine** (engagement curves, stall, anti-stall, bite point) | Weeks of debugging for a sandbox/RP game that doesn't reward the fidelity. Auto-clutch in the current powertrain is invisible to the player. | Add `Powertrain/ClutchModel.cs` per the research report. Hook into `VehicleBase.Powertrain.cs` between throttle filtering and engine output. Manual mode would be opt-in via a property. |
| **Real differential** (open/locked/limited-slip, front/rear bias) | `ForwardGearTorqueMultipliers` plus per-wheel grip already produces gear-feel and gives FWD/RWD/AWD differentiation indirectly. | Add `Powertrain/DifferentialState.cs`. Split engine force per-wheel via diff mode; introduce `Axle.IsPowered` flag. Most invasive change since it pierces the wheel sim. |
| **Slip-ratio / slip-angle tire model** | Current `LateralFriction × FrontGripMultiplier × tireWear × punctureFactor` produces drift-able, tunable handling at much lower cost. Reaches "good enough for arcade-plus" feel without a per-wheel ODE. | Replace the friction block in `VehicleBase.Wheels.cs` with a `TireModel.Evaluate()` call returning longitudinal + lateral force from slip. Wheel state grows: `AngularVelocity`, `SlipRatio`, `SlipAngle`, `NormalLoad`. |
| **Drivetrain inertia, starter physics, rev limiter cut** | The simple RPM smoothing + RedlineRpm clamp gives the player every cue they need. | Add an `EngineInertia` property and a starter state machine. Cosmetic; affects audio responsiveness more than driving feel. |
| **`IWheelContactSolver` interface + `libwheel` backend** | Implies switching the library to a Game-project type (since "libraries can't reference other libraries"). v1 design is one solver. | Define `IWheelContactSolver` with `Step(VehicleBase, Wheel[], float dt)`. Move current logic into `ArcadeTraceWheelSolver`. Add `LibwheelContactSolver` wrapping [`Facepunch/sbox-libwheel`](https://github.com/Facepunch/sbox-libwheel). The rest of the stack (Powertrain → Tune → events → systems) is solver-agnostic and won't change. |
| **Host-validated input-command networking** | Current `[Sync] + [Rpc.Owner]` owner-authoritative model is fine for casual / co-op DarkRP. Bandwidth-cheap, simple. | Add `Networking/VehicleInputCommand.cs` stream, `VehicleHostValidator.cs` for impossible-state rejection. Required for competitive multiplayer; out of scope for sandbox RP. |
| **Detachable physical parts** (CMS-style pull-out alternator) | Hours of art + interaction work. The maintenance loop already gives the *gameplay* (diagnose → buy → install → repair → drive better). | Per-part `GameObject` children with attachment joints. Big art lift; gameplay payoff marginal over current health-state model. |

**The architecture is set up so that adding any of these later is local to one or two files.** The `IWheelContactSolver` swap is the cleanest extension point; the rest are additive partials on `VehicleBase`.

If you ever revisit this, re-read `deep-research-report.md` Section "Focused audit of matekdev/sbox-arcade-car-physics" for the precise file-by-file change plan with effort estimates.

---

## 7. Where to look in the official docs (quick index)

This list is curated to the work above. Full inventory in [`TECH_REFERENCE.md`](./TECH_REFERENCE.md).

- Project types — https://sbox.game/dev/doc/getting-started/project-types
- Addon project — https://sbox.game/dev/doc/getting-started/project-types/addon-project
- Code basics — https://sbox.game/dev/doc/code/code-basics
- Libraries — https://sbox.game/dev/doc/code/libraries
- API whitelist — https://sbox.game/dev/doc/code/code-basics/api-whitelist
- Cheat sheet — https://sbox.game/dev/doc/code/code-basics/cheat-sheet
- Hotloading — https://sbox.game/dev/doc/code/advanced-topics/hotloading
- Scene system — https://sbox.game/dev/doc/scene
- Property attributes — https://sbox.game/dev/doc/editor/property-attributes
- Custom assets / GameResource — https://sbox.game/dev/doc/assets/resources/custom-assets
- GameResource extensions — https://sbox.game/dev/doc/assets/resources/gameresource-extensions
- Asset naming conventions — https://sbox.game/dev/doc/assets/naming-conventions
- File system — https://sbox.game/dev/doc/assets/file-system
- Physics — https://sbox.game/dev/doc/physics
- Networking — https://sbox.game/dev/doc/networking
- Lobbies — https://sbox.game/dev/doc/networking/lobbies
- UI overview — https://sbox.game/dev/doc/ui
- UI basics — https://sbox.game/dev/doc/ui/ui-basics
- Input — https://sbox.game/dev/doc/gameplay/input
- Services (achievements/leaderboards/stats) — https://sbox.game/dev/doc/services
- API browser — https://sbox.game/api

External references used in this guide:
- [Facepunch/sbox-public (engine source)](https://github.com/Facepunch/sbox-public)
- [Facepunch/sbox-libwheel](https://github.com/Facepunch/sbox-libwheel) — wheel collider primitive (NOT to depend on as a library)
- [matekdev/sbox-arcade-car-physics](https://github.com/matekdev/sbox-arcade-car-physics) — copy-the-pattern reference
- [sousou63/DarkRP](https://github.com/sousou63/DarkRP) — primary target gamemode
- [dxura/dxrp-public](https://github.com/dxura/dxrp-public) — secondary target gamemode
- [SubZero S&Box Developer Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3595903475)
