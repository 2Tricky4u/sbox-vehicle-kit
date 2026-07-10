# Vehicles.Maintenance

Gamemode-agnostic vehicle + maintenance + mechanic-job library for s&box.

Designed so [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP), [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public), and any future DarkRP-style gamemode can drop the same library in and use it. The gameplay payoff is the **maintenance ⇄ driving binding**: a damaged engine literally reduces effective power, worn or punctured tires reduce grip, crashing genuinely damages things, and repairing restores them — so the mechanic job has a real loop: *drive → crash/wear → diagnose → buy parts → repair → get paid.*

## What it gives you

- A `VehicleBase` component for any drivable vehicle.
- A `VehicleConfig` GameResource — every car is pure data (model, sounds, performance, fuel, durability, capacity, price). **Adding a car requires no C# changes.** Engine power is data-driven: `EngineTorqueNm × TorqueToForceScale`, shaped over speed by `AccelerationCurve`.
- A `VehicleTuneProfile` GameResource — multiplier presets that scale engine power, brake, steering, suspension, front/rear grip, downforce. Six ship in the host project: **grip, drift, rally, heavy, sport, arcade**.
- A **light powertrain** — synced RPM + auto-shifting gearbox + per-gear torque multiplier + engine braking. Fake-but-convincing, no real clutch/diff sim.
- **Input smoothing** — keyboard taps ramp into analog throttle/steer rather than firing as 0/1.
- **Kinematic arcade wheel sim** — raycast suspension, body-level friction, downforce, air drag, terrain tilt, wall collide-and-slide. Crash damage comes from the solver's own measured impacts (wall closing speed, hard landings).
- **Maintenance state** — fuel (burns at idle and under load), engine/body health, per-tire wear + punctures, battery (drains at idle, **alternator recharges above idle**), oil (low oil chews the engine).
- **Wrecked + recovery** — body at 0 totals the car (engine dead until repaired above 25%); flipped-and-stuck detection fires `OnVehicleStuck`; `RecoverUprightRpc()` rights/lifts/stops it.
- A "mechanic" loop: `RepairTool`, `FuelPump`, `PartItemPickup` (supports a `PartIdent` string), diagnostic Razor UI, `RepairFlow` (mechanic gate → custom action → charge `RepairBaseCost` → consume part → repair → pay).
- An **event bus** (`VehicleEvents`) — lifecycle (incl. `OnVehicleWrecked`/`OnVehicleStuck`), maintenance, powertrain, systems, seats. **Per-subscriber isolation**: one throwing handler warns and the rest still run.
- Seats — `VehicleSeat.TryEnter/Exit`, driver-input gating, networked occupancy + ownership handoff, occupant parented to the seat so passengers ride along; occupants are ejected safely on destroy.
- An `IVehicleHost` contract — implement it once in your gamemode to plug in your currency, job, inventory, and persistence systems.
- Gameplay hook: `SetKinematicVelocity(v)` for launch pads, explosions, scripted crashes.

## Networking model

Owner-authoritative. **Only the owning client simulates** (`Network.Active ? !IsProxy : LocalSimulation`); state replicates via `[Sync]`, mutations go through `[Rpc.Owner]` methods (`RefuelRpc`, `RepairRpc`, `DamageRpc`, `Toggle*Rpc`, …). Proxies interpolate the synced transform and run engine audio from synced state only. Spawned vehicles use `NetworkOrphaned.Host` so a disconnecting owner's car survives; call `VehiclePersistence.RestoreOwnershipFor(connection)` on player join to hand it back. `LocalSimulation` only affects offline/solo play and can stay enabled.

**Scene requirements:** a `ScreenPanel` for the UI panels, and your player objects tagged **`player`** (vehicle wall traces ignore that tag).

## Adding a new car (for gamemode authors)

1. Pick a model (`.vmdl`, local or cloud).
2. Right-click in Asset Browser → **New → Vehicle Config**, save as `Assets/<name>.vcfg`. Tune the inspector fields — `EngineTorqueNm`, `MassKg`, `MaxSpeedKmh`, and `AccelerationCurve` are what make it drive differently.
3. Build a prefab (copy `prefabs/sedan.prefab` from the host project): root with `VehicleBase` (referencing the `.vcfg`) + `Rigidbody` + collider + model + 4 wheel-anchor children + `VehicleSeat` children (exactly one `IsDriverSeat`). Set `PrefabPath` on the `.vcfg`.
4. Spawn via `VehicleBase.Spawn( VehicleConfig.Find("<name>"), pos, rot, owner )` — or from a dealer NPC via `VehicleConfig.WithTag("civilian")`.

## Wiring it into your gamemode

```csharp
public class MyDarkRPVehicleHost : IVehicleHost
{
    public bool TryCharge( Connection p, int amount, string reason ) { /* your currency */ }
    public void Pay( Connection p, int amount, string reason ) { /* your currency */ }
    public bool IsMechanic( Connection p ) { /* your job system */ }
    public IPartInventory GetInventory( Connection p ) { /* your inventory */ }
    public void SaveVehicleOwnership( Guid v, ulong steam, VehicleConfig cfg ) { /* your save */ }
    public bool TryLoadVehicleOwnership( Guid v, out ulong steam, out VehicleConfig cfg ) { /* your save */ }
}

// once at gamemode startup:
VehicleHost.Register( new MyDarkRPVehicleHost() );

// on player join (reconnect-safety for orphaned cars):
VehiclePersistence.RestoreOwnershipFor( connection );
```

## Known v1 limits (deliberate)

- Kinematic arcade sim — no per-wheel force model or slip-ratio tires; lateral grip and skid are body-level, so a single flat lowers overall grip rather than pulling the car sideways.
- Wall detection is a 3-ray feeler fan at body height; very thin posts between rays can be missed.
- Suspension sums to one vertical velocity — body tilt is cosmetic, no weight transfer.
- Damage is state-only (no visual dents/smoke). `CargoCapacityKg` and `PaintTintable` are reserved fields for gamemodes to read; the library doesn't consume them.

## Folder layout

```
Vehicles.Maintenance/
├── Vehicles.Maintenance.sbproj
├── Code/
│   ├── PartKind.cs                     (enum: Engine / Body / Tire / Battery / Oil)
│   ├── Components/
│   │   ├── VehicleBase.cs              (main partial — config, lifecycle, sim gating)
│   │   ├── VehicleBase.State.cs        ([Sync] Fuel / health / TireWear / IsWrecked + RPCs)
│   │   ├── VehicleBase.Damage.cs       (impact damage entry point + wear ticking)
│   │   ├── VehicleBase.Input.cs        (raw WASD → _raw* fields)
│   │   ├── VehicleBase.InputFilter.cs  (smooths raw → ThrottleInput / SteerInput)
│   │   ├── VehicleBase.Tune.cs         (tune × maintenance → Effective* props)
│   │   ├── VehicleBase.Powertrain.cs   ([Sync] CurrentGear / EngineRpm, auto-shift)
│   │   ├── VehicleBase.Wheels.cs       (kinematic wheel sim — the physics)
│   │   ├── VehicleBase.Systems.cs      ([Sync] EngineOn / doors / lights / punctures)
│   │   ├── VehicleBase.Recovery.cs     (stuck detection + RecoverUprightRpc + eject)
│   │   ├── VehicleBase.Spawn.cs        (VehicleBase.Spawn — prefab clone + network)
│   │   ├── VehicleBase.Sound.cs        (RPM-pitched engine loop + one-shots)
│   │   ├── VehicleBase.Debug.cs        (DebugLog tick + event log)
│   │   ├── VehicleSeat.cs              (occupancy, ownership handoff, ride-along)
│   │   ├── VehicleDoor.cs / VehicleHeadlight.cs
│   │   ├── RepairTool.cs / FuelPump.cs / PartItemPickup.cs
│   ├── Resources/
│   │   ├── VehicleConfig.cs            (.vcfg) · VehicleTuneProfile.cs (.vtune) · PartDefinition.cs (.partdef)
│   ├── Contracts/
│   │   ├── IVehicleHost.cs / IPartInventory.cs / IRepairAction.cs
│   │   ├── RepairFlow.cs               (the single repair pipeline)
│   │   └── VehiclePersistence.cs       (ownership restore on join)
│   ├── Events/
│   │   └── VehicleEvents.cs            (static bus, per-subscriber isolation)
│   └── UI/
│       ├── VehicleHud.razor            (speed/RPM/fuel/health HUD)
│       ├── DiagnosticPanel.razor       (mechanic's repair UI)
│       ├── PartSelectPanel.razor / Toast.razor / VehicleUi.cs
└── Editor/                              (custom inspectors — empty for now)
```

See `docs/GUIDE.md` (build guide), `docs/TECH_REFERENCE.md` (s&box doc digest), and `TODO.md` (work tracker) in the parent project for the full design and roadmap.
