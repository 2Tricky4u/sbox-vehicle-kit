# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An **s&box** (Facepunch Source 2 / C#) project. The repo is two things at once:

- **`Code/`** — a *host game project* (`carmaintenance.sbproj`, `"Type": "game"`) whose only purpose is to load and test the library in the editor. Host-only glue lives in `Code/Host/` (stub `IVehicleHost`, dev console commands, seat/test-driver components).
- **`Libraries/Vehicles.Maintenance/`** — the **actual deliverable**: a gamemode-agnostic vehicle + maintenance library meant to be dropped into any DarkRP-style gamemode via s&box's Library Manager. **Put new vehicle/maintenance code here, not in `Code/`.** The library must never reference the host or any specific gamemode — s&box rule: *libraries cannot reference other libraries or game code* (that's why the wheel sim is self-contained rather than depending on `sbox-libwheel`).

## Building, running, testing

There is **no CLI build/test loop**. This is an s&box editor project; `.csproj`/`.sln*` files are auto-generated (gitignored) and code hotloads in the editor.

- Open `carmaintenance.sbproj` in the s&box editor, open `Assets/scenes/minimal.scene` (the startup scene), press Play.
- Iterate via hotload — save a `.cs` file and the editor recompiles live.
- **"Testing" = the `vh.*` dev console commands** in `Code/Host/VehicleDevCommands.cs`. Type `vh.help` in the in-game console for the full list. Key ones: `vh.spawn <cfgIdent>`, `vh.status`, `vh.scene` (dumps the GameObject tree + a setup checklist that diagnoses most "E does nothing / car won't move" issues), `vh.damage`/`vh.repair`/`vh.refuel`, `vh.tune <ident>`, `vh.debugdraw`, `vh.heal`. When adding a feature, add a matching `vh.*` command so it's testable without UI/NPCs.

## Architecture

Data flows one direction per fixed step:

```
WASD → Input → InputFilter (Approach-ramped) → Powertrain (RPM/gear/auto-shift)
     → Tune (Effective* = base × tune × maintenance health) → Wheels (kinematic sim)
     ⤷ Systems (engine/doors/lights/punctures)   ⤷ VehicleEvents (subscribers)
```

- **`VehicleBase` is one `sealed partial class` split across files by concern** (`VehicleBase.cs`, `.State.cs`, `.Input.cs`, `.InputFilter.cs`, `.Powertrain.cs`, `.Tune.cs`, `.Wheels.cs`, `.Systems.cs`, `.Damage.cs`, `.Spawn.cs`, `.Sound.cs`, `.Debug.cs`). Find the partial matching the concern before editing.
- **Cars are pure data.** Adding a vehicle = a `.vcfg` (`VehicleConfig` GameResource) + `.vmdl` + `.prefab`, with **zero C# changes**. Driving feel presets are `.vtune` (`VehicleTuneProfile`); repair parts are `.partdef` (`PartDefinition`). The `VehicleConfig` schema is **locked at v1** — extend via gamemode `ResourceExtension`, not by adding fields.
- **The maintenance ⇄ driving binding is the whole point.** `VehicleBase.Tune.cs` exposes `Effective*` properties = `base × tune multiplier × maintenance health factor`. A damaged engine literally lowers `EffectiveEnginePower`; worn/punctured tires lower per-wheel grip. Preserve this chain when touching physics or health.
- **Gamemode integration is via contracts, not references.** `IVehicleHost` (currency/job/inventory/persistence) + `IPartInventory` in `Contracts/`. A gamemode implements them and calls `VehicleHost.Register(...)` once at boot; the library reads `VehicleHost.Current`. The host project's stub is `Code/Host/CarMaintenanceVehicleHost.cs`, registered by `VehiclesMaintenanceBootstrap` (which also logs all events). `CarMaintenanceVehicleHost.IsMechanic` returns true for everyone — flip it to test mechanic gating.
- **`VehicleEvents`** (`Events/VehicleEvents.cs`) is a static event bus (spawn, refuel, repair, damage, shift, skid, door, horn, seat, …). VFX/audio/RP hooks subscribe here; fire via the `internal` `Raise*` helpers.

## Critical gotchas

- **The wheel sim is a KINEMATIC controller, not force-based.** `OnAwake` sets `Body.MotionEnabled = false`; `VehicleBase.Wheels.cs` owns a `Vector3 _vel`, does manual gravity + raycast suspension + steering, and integrates by hand with `WorldPosition += _vel * dt`. This deliberately bypasses Source 2's contact damping, which silently capped velocity at ~50 km/h. **Do not reintroduce `ApplyForceAt` / `Body.Velocity +=` for movement** — it was removed for this exact reason. The collider stays for collision *detection* only. (Older prose in `docs/GUIDE.md` mentioning `ApplyForceAt`/`Body.Velocity +=` is obsolete; the STATUS banner at the top of that file is the source of truth.)
- **s&box uses inches.** Convert: m/s ↔ in/s is `× 0.0254`; km/h ↔ in/s is `× 0.09144`. Gravity in the sim is `9.81 / 0.0254` in/s².
- **Networking:** owner-authoritative. State is `[Sync]` properties; mutations go through `[Rpc.Owner]` methods (`RefuelRpc`, `RepairRpc`, `DamageRpc`, `Toggle*Rpc`, …). Only **properties** serialize/sync, not fields. `LocalSimulation` (on by default) bypasses the `Network.IsOwner` gate for solo editor testing.
- **GameResource lookups:** use `VehicleConfig.All` / `PartDefinition.All`, not raw `ResourceLibrary.GetAll<T>()` — the engine's `core/cfg/*.cfg` files collide with the `.vcfg` extension and `.All` filters them out (see comments in `VehicleConfig.cs` / `VehicleDevCommands.cs`).
- **GameResource extensions** must be lowercase and ≤8 chars.
- Match existing style: **tabs** for indentation, `crlf` line endings (`.editorconfig`), Allman braces, expression-bodied properties/accessors where the surrounding code does.

## Reference docs (gitignored, but on disk — read them)

`docs/GUIDE.md` (12-step build plan + locked schema + hard-won s&box notes), `docs/TECH_REFERENCE.md` (distilled s&box API reference), and `TODO.md` (80+ granular tasks with files/effort/"how"/refs) are gitignored but present locally and are the richest source of project context. `README.md` is the tracked overview. The kinematic-physics history and other quirks are recorded in the `feedback_sbox_physics_quirks` memory referenced from `GUIDE.md`.

## Issues to resolve

Found in a code audit (2026-06-13). Roughly ordered by severity; the first two may be structurally broken, not merely unfinished.

### Critical — verify before building further

- **Collision damage probably never fires.** `VehicleBase.Damage.cs:87` relies on `ICollisionListener.OnCollisionStart` / `collision.Contact.Speed`, but `Body.MotionEnabled = false` means Source 2 doesn't dynamically simulate the body — contact-impact events generally require a simulated body. Walls are handled by manual raycasts (`SweepHorizontal`) *because* the body doesn't physically collide, which implies the crash → damage → repair loop has no organic trigger; damage only happens via `vh.damage`. **First test:** spawn a car, drive into a wall, watch console for `OnDamage`. If nothing fires, route collision damage off the `SweepHorizontal` wall-hit (it already has impact direction + speed) instead of `ICollisionListener`.
- **Multiplayer is unproven and self-conflicting.** `LocalSimulation` defaults to `true` (`VehicleBase.cs:27`) and `ShouldSimulate = LocalSimulation || Network.IsOwner`. In a real lobby every proxy also has `LocalSimulation=true`, so all clients run the kinematic sim and write `WorldPosition`/`[Sync]` state at once and fight. Needs per-instance disabling for non-owners. Also: with `MotionEnabled=false` there's no physics-driven network interpolation — position relies on transform sync only, untested.

### Bugs

- **Inverted engine event:** `VehicleBase.Systems.cs:84-86` fires `RaiseEngineStarted` when `!EngineOn` (engine just turned *off*). Should be `RaiseEngineStopped`. Inverts any audio/VFX hook on start/stop.
- **Battery only drains, never recharges.** `TickWear` (`Damage.cs:67-68`) decrements `BatteryCharge` while the engine runs; the "alternator ≈ break-even" comment is not implemented. After ~20 min engine-on, `CanStartEngine` (`State.cs:32`) goes false with no in-fiction fix but `RepairRpc(Battery)`. Add alternator recharge while driving (or above idle RPM).

### Data-driven gap (contradicts the "cars are pure data" claim)

- `VehicleConfig.AccelerationCurve` is **referenced nowhere** — dead.
- `VehicleConfig.EngineTorqueNm` is used **only** by `EffectiveTorque` (`State.cs:39`), which the wheel sim never reads. Drive force comes from `MaxEngineForce`, a *component* property (`Wheels.cs:104`), not Config. So `.vcfg` files differ only in mass / top speed / grip / brake — not engine power. Either wire these Config fields into the sim or remove them from the schema. (`EffectiveTorque` is otherwise dead code.)

### Arcade limitations (documented in `Wheels.cs`, but real gameplay constraints)

- Lateral grip + skid detection are **body-level, not per-wheel** — all tires skid in unison; a single flat only drops uniform grip, can't realistically pull the car.
- Wall collision is **3 feeler rays** — wall edges/posts between rays let the car ghost through.
- Suspension is **summed into one Z velocity** — no weight transfer, only cosmetic tilt.

### Repo / process

- **Project lives under `OneDrive\Documents\…`** — OneDrive sync/locks contend with s&box hotload writing `obj/`, `.sbox/`, generated `.csproj`; risks file-lock contention and mid-compile corruption. Move the repo outside OneDrive.
- **`docs/`, `TODO.md`, `deep-research-report.md` are gitignored** — the entire roadmap and design context aren't version-controlled or shareable.
- Minor: `EnsureTireWearList()` mutates a `[Sync] NetList` in `OnAwake` on every client, not just the owner.
