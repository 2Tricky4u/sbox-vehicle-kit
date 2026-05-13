# Vehicles.Maintenance

Gamemode-agnostic vehicle + maintenance + mechanic-job library for s&box.

Designed so [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP), [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public), and any future DarkRP-style gamemode can drop the same library in and use it.

## What it gives you

- A `VehicleBase` component for any drivable vehicle.
- A `VehicleConfig` GameResource — every car is pure data (model, sounds, performance, fuel, durability, capacity, price). **Adding a car requires no C# changes.**
- A `VehicleTuneProfile` GameResource — multiplier presets (Sport / Drift / Rally / Heavy / Grip / Arcade) that scale engine power, brake, steering, suspension, front/rear grip, downforce. Apply for instant per-car feel.
- A **light powertrain** — synced RPM + auto-shifting gearbox + per-gear torque multiplier + engine braking. Fake-but-convincing, no real clutch/diff sim.
- **Input smoothing** — keyboard taps ramp into analog throttle/steer rather than firing as 0/1.
- **Arcade wheel sim** — raycast suspension, contact-basis friction, downforce, air drag.
- A "mechanic" loop: `RepairTool`, `FuelPump`, `PartItemPickup`, diagnostic Razor UI.
- An **event bus** (`VehicleEvents`) — `OnShifted`, `OnEngineRpmRedlined`, `OnTirePunctured`, `OnWheelSkidStarted`, `OnDoorOpened`, `OnHorn`, `OnRefuel`, `OnRepair`, `OnDamage`, plus engine on/off, headlights, doors.
- **Maintenance ⇄ driving binding** — a damaged engine literally reduces effective power; worn or punctured tires reduce grip; repairing restores them.
- An `IVehicleHost` contract — implement it once in your gamemode to plug in your currency, job, and inventory systems.

## Adding a new car (for gamemode authors)

1. Drop a `.vmdl` model in `Assets/vehicles/models/<name>.vmdl`.
2. Right-click in Asset Browser → **New → Vehicle Config**, save as `Assets/vehicles/<name>.vcfg`.
3. Tune the inspector fields.
4. Build a prefab `<name>.prefab`: root GameObject with `VehicleBase` component (referencing the `.vcfg`) + N child wheel-anchor GameObjects + seat-anchor GameObjects.
5. Spawn from your dealer NPC via `VehicleConfig.Find("<name>")` or `VehicleConfig.WithTag("civilian")`.

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
```

## Folder layout

```
Vehicles.Maintenance/
├── Vehicles.Maintenance.sbproj
├── Assets/                            (vehicle .vcfg + prefabs + sounds — author per-game)
│   └── README.md                       (the "add a new car" recipe)
├── Code/
│   ├── PartKind.cs                     (enum: Engine / Body / Tire / Battery / Oil)
│   ├── Components/
│   │   ├── VehicleBase.cs              (main partial — config, lifecycle)
│   │   ├── VehicleBase.State.cs        ([Sync] Fuel / EngineHealth / TireWear + RPCs)
│   │   ├── VehicleBase.Damage.cs       (collision damage + wear ticking)
│   │   ├── VehicleBase.Input.cs        (raw WASD → _raw* fields)
│   │   ├── VehicleBase.InputFilter.cs  (smooths raw → ThrottleInput / SteerInput)
│   │   ├── VehicleBase.Tune.cs         (reads VehicleTuneProfile, exposes Effective* props)
│   │   ├── VehicleBase.Powertrain.cs   ([Sync] CurrentGear / EngineRpm, auto-shift)
│   │   ├── VehicleBase.Wheels.cs       (arcade wheel sim — raycast + friction + drive)
│   │   ├── VehicleBase.Systems.cs      ([Sync] EngineOn / Doors / Lights / Punctures)
│   │   ├── VehicleBase.Debug.cs        (DebugLog flag, event subscriptions, tick log)
│   │   ├── RepairTool.cs               (held tool, opens DiagnosticPanel)
│   │   ├── FuelPump.cs                 (static prop component)
│   │   └── PartItemPickup.cs           (trigger-based pickup)
│   ├── Resources/
│   │   ├── VehicleConfig.cs            ([AssetType] declarative car def — .vcfg)
│   │   ├── VehicleTuneProfile.cs       ([AssetType] driving preset — .vtune)
│   │   └── PartDefinition.cs           ([AssetType] part SKU — .partdef)
│   ├── Contracts/
│   │   ├── IVehicleHost.cs             (per-gamemode adapter — currency / job)
│   │   ├── IPartInventory.cs           (per-gamemode inventory adapter)
│   │   └── IRepairAction.cs            (per-part custom repair behaviour)
│   ├── Events/
│   │   └── VehicleEvents.cs            (static event bus — lifecycle, maintenance, powertrain, systems)
│   └── UI/
│       ├── DiagnosticPanel.razor       (mechanic's repair UI)
│       ├── DiagnosticPanel.razor.scss
│       └── FuelGauge.razor             (HUD widget when seated)
└── Editor/                              (custom inspectors — empty for now)
```

See `docs/GUIDE.md` (build guide), `docs/TECH_REFERENCE.md` (s&box doc digest), and `TODO.md` (work tracker) in the parent project for the full design and roadmap.
