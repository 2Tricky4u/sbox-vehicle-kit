# Vehicles.Maintenance

Gamemode-agnostic vehicle + maintenance + mechanic-job library for s&box.

Designed so [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP), [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public), and any future DarkRP-style gamemode can drop the same library in and use it.

## What it gives you

- A `VehicleBase` component for any drivable vehicle.
- A `VehicleConfig` GameResource — every car is pure data (model, sounds, performance, fuel, durability, capacity, price). **Adding a car requires no C# changes.**
- A "mechanic" loop: `RepairTool`, `FuelPump`, `PartItemPickup`, diagnostic Razor UI.
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
├── Assets/                       (vehicle .vcfg + prefabs + sounds — empty in v1, ship sedan separately)
├── Code/
│   ├── PartKind.cs
│   ├── Components/
│   │   ├── VehicleBase.cs
│   │   ├── VehicleBase.State.cs
│   │   ├── VehicleBase.Input.cs
│   │   ├── VehicleBase.Wheels.cs
│   │   ├── VehicleBase.Damage.cs
│   │   ├── RepairTool.cs
│   │   ├── FuelPump.cs
│   │   └── PartItemPickup.cs
│   ├── Resources/
│   │   ├── VehicleConfig.cs
│   │   └── PartDefinition.cs
│   ├── Contracts/
│   │   ├── IVehicleHost.cs
│   │   ├── IPartInventory.cs
│   │   └── IRepairAction.cs
│   ├── Events/
│   │   └── VehicleEvents.cs
│   └── UI/
│       ├── DiagnosticPanel.razor
│       ├── DiagnosticPanel.razor.scss
│       └── FuelGauge.razor
└── Editor/                       (custom inspectors — empty for now)
```

See `docs/GUIDE.md` and `docs/TECH_REFERENCE.md` in the parent project for the full design.
