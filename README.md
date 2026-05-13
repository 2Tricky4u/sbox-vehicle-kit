# carmaintenance

An **s&box** host project building a gamemode-agnostic vehicle & maintenance library — designed so any DarkRP-style gamemode (e.g. [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP), [`dxura/dxrp-public`](https://github.com/dxura/dxrp-public)) can drop the same library in and instantly get cars + a mechanic-job gameplay loop.

The actual library lives at [`Libraries/Vehicles.Maintenance/`](Libraries/Vehicles.Maintenance/). This repo is the **dev/test host** for that library.

---

## What the mod actually is

A **Car-Mechanic-Simulator-flavored gameplay layer for DarkRP**, with:

- **Multiple distinct cars defined purely as data** — each new vehicle is a `.vcfg` GameResource + `.vmdl` model + `.prefab`. Zero C# changes to add a car.
- **Arcade-plus driving** — raycast wheel suspension, contact-basis friction, downforce, air drag, light powertrain (RPM / auto-shift gearbox / engine braking), input smoothing.
- **Player-facing tuning** via `.vtune` GameResource presets (Sport / Drift / Rally / Heavy / Grip / Arcade) that multiply into the driving constants without touching physics math.
- **The maintenance ⇄ driving bridge** — a damaged engine *literally* reduces effective power; worn tires reduce grip; repairing restores them. Mechanic work has visible, in-driving consequences (the actual gameplay payoff).
- **A "Mechanic" gameplay loop** — diagnostic panel, RepairTool, FuelPump, PartItemPickup, plus an event bus for VFX/audio hooks.
- **`IVehicleHost` contract** — each consuming gamemode writes a ~100-line adapter for its currency/job/inventory; the library never references any specific gamemode's code.

The "Car Mechanic Simulator" inspiration sits in the maintenance state (fuel, engine health, body health, per-tire wear, optional battery/oil) and the repair flow, not in physically detachable parts. Parts are health values; repairs consume `PartDefinition` SKUs from inventory and pay the mechanic.

---

## Current status (2026-05-13)

**Working in code:**

- ✅ Arcade wheel sim with raycast suspension, friction, downforce, air drag
- ✅ Engine drive (Source 2 contact-clamp workaround — `Body.Velocity +=` instead of `ApplyForceAt`)
- ✅ Speed cap from `Config.MaxSpeedKmh`
- ✅ Input smoothing (`Approach`-ramped throttle / steer)
- ✅ Auto-shifting powertrain (5 gears + reverse + neutral)
- ✅ Engine braking, skid detection + events
- ✅ `VehicleTuneProfile` multipliers feeding into `Effective*` properties
- ✅ Maintenance health factors (engine/body/per-tire) scaling effective values
- ✅ Networked `[Sync]` state + `[Rpc.Owner]` mutations
- ✅ Vehicle systems (engine on/off, doors, lights, punctures, horn)
- ✅ Comprehensive debug logging (per-tick state dump + event transition log)

**Scaffolded but not yet gameplay-tested:**

- ⚠️ RepairTool — held tool, mechanic-gated, consumes parts (UI buttons still stubs)
- ⚠️ FuelPump — charges player via `IVehicleHost.TryCharge`, calls `RefuelRpc`
- ⚠️ DiagnosticPanel.razor — read-only status display (Repair buttons not yet wired)
- ⚠️ PartItemPickup — trigger-based world pickup

**Not yet started:**

- ❌ Real seat enter/exit (currently `TestDriverComponent` auto-occupies driver seat)
- ❌ Sedan `.vmdl` model (driving a placeholder box right now)
- ❌ Engine / horn / wrench / skid audio
- ❌ In-vehicle HUD (speedometer / RPM / gear indicator)
- ❌ Vehicle dealer + parts shop UI/NPCs
- ❌ DarkRP adapter implementations
- ❌ Save/load persistence beyond a single session

**Realistic timeline to v1** (per [`TODO.md`](TODO.md)):
- Aggressive solo dev: ~3 weeks
- Comfortable: ~5–6 weeks
- With both target DarkRP adapters polished: add ~2 weeks

---

## Roadmap

Three documents define the work:

| Doc | What it is |
|---|---|
| [`docs/GUIDE.md`](docs/GUIDE.md) | The build guide — 12 numbered steps from "empty project" to "shipped v1," with the architecture diagram, locked `VehicleConfig` schema, hard-won s&box implementation notes, and effort table. Steps 1–5e are ✅ done. |
| [`docs/TECH_REFERENCE.md`](docs/TECH_REFERENCE.md) | Distilled s&box documentation (mirrors `sbox.game/dev/doc` + API browser). Self-contained reference for every layer the project touches — networking, physics, UI, GameResource, file system, hotload, whitelist, etc. |
| [`TODO.md`](TODO.md) | Per-task accountability — 80+ items grouped into 13 sections (code completion, orphan cleanup, models, VFX, UI, world content, gameplay, persistence, networking, testing, publishing). Each item has files, effort, "done when," **how to do it**, and **refs**. |

[`deep-research-report.md`](deep-research-report.md) is a separate research pass surveying Garry's Mod Lua vehicle bases (Glide, GAuto, simfphys, SCars, LVS) for transferable patterns. The full-simulation path it sketches is **deliberately deferred** — see [GUIDE.md Section 6](docs/GUIDE.md#6-what-we-deliberately-dont-simulate) for what we won't simulate and why.

### Next concrete actions

Per the suggested execution order in TODO.md:

1. **Dev console commands** (~2 hours) — `vh.spawn`, `vh.damage`, `vh.refuel`, `vh.shift`, etc. Makes everything else 10× easier to test.
2. **Close Section 1 + 1b TODOs** — DiagnosticPanel button wiring, seat system (retiring `TestDriverComponent`), `Vehicle.Spawn` helper, wrecked state, flip recovery, per-wheel impact damage, `VehicleBase.Sound.cs`.
3. **Author the 6 `.vtune` presets** + tune existing `sedan.vcfg`.
4. **Garage scene** + dealer/parts NPCs.
5. **Multiplayer test pass** with two clients.
6. **Implement the two DarkRP adapters** (sousou63 + dxura).

---

## Project layout

```
carmaintenance/
├── Assets/                    (host-project assets — sedan.vcfg, scenes, test tunes)
├── Code/
│   └── Host/                  (host-side glue — bootstrap, stub IVehicleHost, test driver)
├── Libraries/
│   └── Vehicles.Maintenance/  (the actual library — gamemode-agnostic, drop into any host)
├── docs/
│   ├── GUIDE.md               (build guide)
│   └── TECH_REFERENCE.md      (s&box doc digest)
├── deep-research-report.md    (research pass on Lua vehicle bases)
├── TODO.md                    (work tracker with How+Refs per item)
└── README.md                  (this file)
```

The library `Libraries/Vehicles.Maintenance/` is the unit of publishing — once tested, it goes through s&box's Library Manager and any other game project can pull it in.

---

## Getting started (for development)

This is a **game-type s&box project** that hosts the library for testing.

1. Open `carmaintenance.sbproj` in the s&box editor.
2. Open `Assets/scenes/minimal.scene`.
3. The scene should already have a vehicle GameObject with `VehicleBase` + `TestDriverComponent`, plus a `VehiclesMaintenanceBootstrap` registering the test `IVehicleHost`.
4. Press Play. WASD to drive. Watch the console for `[Vehicle] ▸ SHIFT N → 1` etc. when `DebugLog` is on.
5. Read [`docs/GUIDE.md`](docs/GUIDE.md) for the full architecture; [`TODO.md`](TODO.md) for what's next.

---

## Tech overview

See [`docs/GUIDE.md`](docs/GUIDE.md) §2 for the layered data flow diagram and [`docs/TECH_REFERENCE.md`](docs/TECH_REFERENCE.md) for s&box-specific API references.

**Layers (input → output, per fixed step):**

```
Driver input → VehicleBase.Input → VehicleBase.InputFilter (Approach-ramped)
            → VehicleBase.Powertrain (RPM, gear, auto-shift)
            → VehicleBase.Tune (Effective* = base × tune × maintenance health)
            → VehicleBase.Wheels (raycast suspension, friction, engine via Velocity+=)
            → Body.Velocity / Rigidbody → Source 2 integrates next step
            ⤷ VehicleBase.Systems (engine on/off, puncture detection)
            ⤷ VehicleEvents (gamemode / UI / audio subscribe)
```

The wheel-sim layer is positioned as a **swappable backend** — current implementation is an arcade port of [matekdev/sbox-arcade-car-physics](https://github.com/matekdev/sbox-arcade-car-physics) (itself based on [SergeyMakeev/ArcadeCarPhysics](https://github.com/SergeyMakeev/ArcadeCarPhysics)). Everything above the wheel sim (input, powertrain, tune, events, systems) is solver-agnostic.

---

## License

MIT (intended — `LICENSE` file is on the TODO).

Attribution: arcade wheel-sim pattern based on [matekdev/sbox-arcade-car-physics](https://github.com/matekdev/sbox-arcade-car-physics) and [SergeyMakeev/ArcadeCarPhysics](https://github.com/SergeyMakeev/ArcadeCarPhysics) (both MIT).

---

## Useful links

- **s&box docs:** https://sbox.game/dev/doc/
- **s&box API browser:** https://sbox.game/api
- **Facepunch/sbox-public** (engine source): https://github.com/Facepunch/sbox-public
- **sousou63/DarkRP** (primary target gamemode): https://github.com/sousou63/DarkRP
- **dxura/dxrp-public** (secondary target): https://github.com/dxura/dxrp-public
- **Project guide:** [`docs/GUIDE.md`](docs/GUIDE.md)
- **Tech reference:** [`docs/TECH_REFERENCE.md`](docs/TECH_REFERENCE.md)
- **Work tracker:** [`TODO.md`](TODO.md)
