# TODO — Road to 100% Finished

Tracks everything between "where we are" and "shipping the mod." Grouped by area, ordered roughly by dependency. Each item: brief description, files touched, effort, definition of done, **how** to do it, and **refs** (docs / source to consult).

Status legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[~~]` deferred / won't do for v1

> **Cross-cutting refs you'll consult repeatedly.** Listed here once so the per-item refs stay tight:
> - **TR** = our own `docs/TECH_REFERENCE.md` (mirrors official docs, contains code snippets we've already verified work)
> - **Cheat sheet** — https://sbox.game/dev/doc/code/code-basics/cheat-sheet (GameObject / Component / find / clone / etc.)
> - **Property attributes** — https://sbox.game/dev/doc/editor/property-attributes (`[Property] [Group] [Range] [Sync] [Rpc.Owner]` ...)
> - **API browser** — https://sbox.game/api (look up `Sandbox.<Type>` for exact method signatures)
> - **Scene** — https://sbox.game/dev/doc/scene
> - **Networking** — https://sbox.game/dev/doc/networking
> - **UI basics (Razor)** — https://sbox.game/dev/doc/ui/ui-basics
> - **File system** — https://sbox.game/dev/doc/assets/file-system
> - **Custom assets (GameResource)** — https://sbox.game/dev/doc/assets/resources/custom-assets
> - **Libraries** — https://sbox.game/dev/doc/code/libraries
> - **Physics** — https://sbox.game/dev/doc/physics

---

## 0. Status snapshot (2026-05-13)

> **STATUS UPDATE 2026-05-17 (kinematic rewrite + full audit). The 2026-05-13 snapshot below is retained for history but is now superseded on the physics points:**
> - **Driving is no longer force-based.** `VehicleBase.Wheels.cs` is a **kinematic controller**: `Body.MotionEnabled=false`, manual `WorldPosition += _vel*dt` integration. The line below reading "Engine drive via `Body.Velocity +=` (Source 2 contact-clamp workaround)" is **obsolete** — Source 2 no longer integrates the body at all. New feel knobs: `VehicleMass` (Config.MassKg-backed, fixes Body.Mass==0 NaN), `MaxForwardAccelMs2` (perceived-mass accel clamp), smooth `(1-ratio²)` top-speed taper, `FullSteerSpeedKmh` + negated-yaw steering (fixes inverted/sluggish turning).
> - **DUPLICATE PROJECT WARNING.** A stale force-based copy exists at `…\firstaddon\` (5 partials, no steering). THIS project (`…\carmaintenance\`) is canonical. Do not open `firstaddon` in s&box.
> - **Assets reality:** 1 `sedan.vcfg`, 1 *unnamed* `.vtune`, 5 `.partdef`, `minimal.scene`. **No `.vmdl`, no `.prefab`** → `vh.spawn` cannot work yet.
> - **Audit buckets (2026-05-17):** DONE = full driving core (kinematic/powertrain/steering/grip/tune/maintenance/wear/damage/debug), DiagnosticPanel repair flow end-to-end, events bus, headlight/door, dev console. NEEDS WORK = engine-pitch sound is a stub (no .sound assets), collision-damage `Network.IsOwner` gate may not fire in solo, RepairActionRegistry has no implementers, FuelPump uncalled, host is a free-money stub. NOT DONE = model/prefab, seats, HUD, world/NPCs, persistence, DarkRP adapters, 6 .vtune presets.

**What works in code (driving system):**
- Arcade wheel sim with raycast suspension, contact-basis friction, downforce, air drag
- Engine drive via `Body.Velocity +=` (Source 2 contact-clamp workaround)
- Speed cap from `Config.MaxSpeedKmh`
- Input smoothing (Approach-ramped throttle / steer)
- Auto-shifting powertrain (5 gears + reverse + neutral, RPM + auto up/down shift)
- Engine braking when coasting in gear
- Skid detection + event firing
- `VehicleTuneProfile` multipliers feed into `Effective*` properties
- Maintenance health factors (engine, body, per-tire) scale effective values
- Networked `[Sync]` state + `[Rpc.Owner]` mutations across all layers
- Vehicle systems (engine on/off, doors, lights, punctures, horn) with event bus
- Comprehensive debug logging when `DebugLog=true`

**Library file inventory (Code/):**
- `Components/`: `VehicleBase.cs` (main partial) + 9 sibling partials (`.State`, `.Damage`, `.Input`, `.InputFilter`, `.Tune`, `.Powertrain`, `.Wheels`, `.Systems`, `.Debug`), plus `RepairTool`, `FuelPump`, `PartItemPickup`
- `Resources/`: VehicleConfig, VehicleTuneProfile, PartDefinition
- `Contracts/`: IVehicleHost, IPartInventory, IRepairAction
- `Events/`: VehicleEvents
- `UI/`: DiagnosticPanel.razor + .scss, FuelGauge.razor

**Host project (Code/Host/):**
- VehiclesMaintenanceBootstrap (registers stub IVehicleHost)
- CarMaintenanceVehicleHost (test stub: free money, everyone is mechanic)
- InMemoryPartInventory
- TestDriverComponent (auto-sets HasDriver=true + chase camera)

**Assets currently authored:**
- `Assets/sedan.vcfg` (one vehicle config)
- `Assets/new vehicle tune.vtune` (one unnamed tune profile)
- `Assets/scenes/minimal.scene` (test scene)

---

## 1. Code completion (close existing TODOs)

- [x] **Wire DiagnosticPanel repair buttons** — currently stubs.
  - **STATUS:** ✅ done 2026-05-14. `RepairPart(PartKind)` and `RepairTire(int)` fully wired: precheck (vehicle exists, host registered, mechanic has the job), `RepairActionRegistry.TryInvoke` first (gamemode-customizable), default flow consumes from inventory + calls `RepairRpc` + pays mechanic. New rows for Battery + Oil added (now that those PartKinds exist). Per-button text shows part count (`Repair (3)`) or `Out` if depleted, `disabled` SCSS class for ineligible buttons. Inline `_flash` error messages display in panel header for ~2.5s on failure ("Out of Engine Kit", "Mechanic job required", etc.). `BuildHash` includes battery/oil/punctures/Mechanic.SteamId AND inventory counts so button labels refresh when parts are consumed. Auto-defaults `Mechanic` to `Connection.Local` if caller forgot to set. New helpers: `PartDefinition.All` (defensive cfg/ filter), `PartDefinition.Find(PartKind)`, `PartDefinition.FindByIdent(string)`. New dev console commands: `vh.diag` opens the panel, `vh.parts` lists `.partdef` assets, `vh.give <ident> [count]` stocks the inventory, `vh.mechanic` info-only.
  - Files: `Libraries/Vehicles.Maintenance/Code/UI/DiagnosticPanel.razor`
  - Effort: 2–3 hours
  - **How:**
    1. Add a `[Property]` to `DiagnosticPanel` for `Connection Mechanic` (the player using the panel) and `PartDefinition[] AvailableParts`.
    2. In `RepairPart(PartKind part)`: look up a `PartDefinition` in `AvailableParts` whose `RepairsPart == part`; check `VehicleHost.Current.GetInventory(Mechanic).CountOf(def) > 0`; charge via `VehicleHost.Current.TryCharge(...)`; consume via `inv.TryConsume`; call `Vehicle.RepairRpc(part, def.RepairAmount)`; pay via `VehicleHost.Current.Pay(...)`.
    3. `RepairTire(int idx)` is the same but passes `wheelIndex`.
    4. Re-bind `BuildHash()` to include `Mechanic?.SteamId` so the panel rebuilds when the mechanic changes.
  - **Refs:** TR §13 UI · UI basics · `IPartInventory` (Contracts) · `VehicleEvents.OnRepair` (already fires from `RepairRpc`).

- [~] **RepairTool — part-select UI**
  - **STATUS 2026-05-17 (code-complete, pending playtest):** `PartSelectPanel.razor` + `.scss` added — lists `PartDefinition.All` with live inventory counts; click sets `Tool.CurrentPart` and closes. Static `Open/Close/Toggle` mirroring the `vh.diag` pattern, mounts via new shared `VehicleUi.MountRoot()`. `RepairTool` opens it on `Input.Pressed("flashlight")` and also auto-opens when fired with no `CurrentPart`. **DRY refactor (per user):** extracted the whole repair pipeline into `Code/Contracts/RepairFlow.cs` (`TryRepair` → `RepairResult`/`RepairOutcome`); BOTH `RepairTool` and `DiagnosticPanel` now call it — the duplicated precheck→registry→consume→RepairRpc→Pay sequence exists once. `DiagnosticPanel` keeps its exact flash strings via a `MapMsg` switch (regression-checked outcome list). Also dedup'd the ScreenPanel-mount lookup (`VehicleHud.Show` now uses `VehicleUi`). Test aid: `vh.partsui`. Remaining before `[x]`: in-scene playtest (needs ScreenPanel + a RepairTool; verify select→repair and that `vh.diag` flash strings are unchanged); `"flashlight"` action assumed bound (tunable). Full Toast hints remain the separate item below.
  - Files: `RepairTool.cs`, new `Libraries/Vehicles.Maintenance/Code/UI/PartSelectPanel.razor`
  - Effort: 4 hours
  - Done when: pressing a hotkey (e.g. Q) opens a list of parts in the mechanic's inventory; selecting one sets `RepairTool.CurrentPart`.
  - **How:**
    1. In `RepairTool.OnUpdate`: detect `Input.Pressed("flashlight")` or a custom action; toggle a private `bool _selectOpen`.
    2. Create `PartSelectPanel.razor` that iterates `VehicleHost.Current.GetInventory(Network.Owner)` — list each `PartDefinition` with icon + count.
    3. On click → write `CurrentPart` on the RepairTool and close the panel.
    4. Use `ScreenPanel` as the root so it draws as HUD; show/hide by parenting/unparenting from `Game.RootPanel` or toggling `Style.Display`.
  - **Refs:** UI basics · `Input.Pressed` (TR §14) · Property attributes for `[Property] PartDefinition CurrentPart` · API browser → `Sandbox.UI.Panel`.

- [~] **RepairTool — feedback hints** — "Mechanic job required" / "Out of parts" toasts.
  - **STATUS 2026-05-17b (code-complete, pending playtest):** `Toast.razor` + `.scss` added — static `Toast.Show(msg, seconds=3)`, lazy-mounts via `VehicleUi.MountRoot()` (same mount as HUD/part-select; the obsolete `Game.RootPanel` in the How below is NOT used), stacked top-centre, `RealTimeUntil` auto-expire culled in `Tick()`, CSS `@keyframes` fade. Wired into `RepairTool`'s existing `switch(res.Outcome)` — a `Toast.Show(...)` added **alongside** each existing `Log.*` (no shared helper / no dedup, per user). Test aid: `vh.toast [message]`. Needs a `ScreenPanel` in the scene. Remaining before `[x]`: in-scene playtest (toast appears + fades; real "Out of …"/"Mechanic job required" paths).
  - **STATUS 2026-05-17 (partial):** `RepairFlow` now returns a structured `RepairResult`/`RepairOutcome`, and `RepairTool` logs the outcome ("Out of …", "Mechanic job required", etc.). The data needed for hints exists in one place — only the on-screen Toast surface (this item) is left; wire `Toast.Show(...)` to `res.Outcome` once Section 5's Toast lands.
  - Files: `RepairTool.cs` + new `Libraries/Vehicles.Maintenance/Code/UI/Toast.razor` (see Section 5)
  - Effort: 2 hours
  - **How:**
    1. Add a static `Toast.Show(string message, float seconds = 3f)` method on the Toast panel.
    2. In `RepairTool` failure branches, call `Toast.Show(...)`.
    3. Toast lifecycle: append a div to `Game.RootPanel` with an autodelete timer (`TimeUntil _expire`); animate via SCSS opacity.
  - **Refs:** UI basics · `Time` helpers (TR §5).

- [x] **Battery and Oil parts**
  - **STATUS:** ✅ done 2026-05-14. `[Sync] BatteryCharge` / `OilLevel` added to `State.cs` (0..100 hardcoded constants `BatteryMaxCharge` / `OilMaxLevel` — promoted to `VehicleConfig` is left for v1.1). `CanStartEngine` now also requires `BatteryCharge > 5`. `IsLowOil` flag added (`< 20%`). `TickWear` drains battery slowly while engine runs and oil with mileage; low oil accelerates engine wear ×5. `DamageRpc` and `RepairRpc` switches handle both new PartKinds. `vh.heal` includes them. **Note:** step (5) "EngineHealthFactor includes oil" deferred to next item — the cascade through engine wear is enough for gameplay-visible effect today.
  - Files: `Libraries/Vehicles.Maintenance/Code/Components/VehicleBase.State.cs`, `VehicleBase.Tune.cs`
  - Effort: 3 hours
  - Done when: battery=0 ⇒ engine won't crank; oil decays with mileage and accelerates engine wear when low.
  - **How:**
    1. Add `[Sync] float BatteryCharge { get; set; }` and `[Sync] float OilLevel { get; set; }` to `VehicleBase.State.cs`. Init from new `Config.BatteryMaxCharge` / `Config.OilCapacity` fields (will need to add to `VehicleConfig` — this DOES change the locked schema, so document it as a v1.1 addendum).
    2. Update `CanStartEngine` (currently `Fuel > 0.1f && EngineHealth > 5f`) to also require `BatteryCharge > 0.1f`.
    3. In `TickWear` (`VehicleBase.Damage.cs`): decay `OilLevel` slowly with distance; when `OilLevel < 0.2f * capacity`, multiply EngineHealth wear rate by 5×.
    4. Extend `RepairRpc` switch to handle `PartKind.Battery` (refill BatteryCharge to max) and `PartKind.Oil` (refill OilLevel).
    5. Update `EngineHealthFactor` in `VehicleBase.Tune.cs` to also account for low oil (small multiplicative factor).
  - **Refs:** TR §6 attributes · TR §7 sync · existing pattern of `Fuel` / `EngineHealth` in `State.cs`.

- [x] **TickWear refinement** — use lateral slip from wheel sim instead of steering input.
  - **STATUS 2026-05-18:** Done. `VehicleBase.Wheels.cs` exposes `public float LateralSlipMs` (abs sideways speed, m/s), set each tick in the existing lateral-grip block (`MathF.Abs(lat) * 0.0254f`, 0 when airborne). `TickWear` (Damage.cs) replaced the `SteerInput > 0.5 && speedKmh > 30` heuristic with `wear = dt * LateralSlipMs * TireWearPerSlipMs` applied to all tyres when `slip > 0.5 m/s && speedKmh > 5`. New `[Property, Group("Damage")] TireWearPerSlipMs = 0.0035` (Range 0.0005–0.02): hard drift ≈10 m/s → 0→1 in ~28 s; gentle ≈0.5 m/s → ~9 min — matches "done when". **Deviation from step 1's literal per-wheel `WheelLateralSlip[i]` array:** the kinematic model is body-level (its own comment says per-wheel torque/slip doesn't apply; skid detection is already uniform) — exposing one honest body slip instead of a faked per-wheel array. Verify via `vh.heal` + `vh.debug` `minTire%` while drifting vs cruising.
  - Files: `VehicleBase.Damage.cs`, `VehicleBase.Wheels.cs`
  - Effort: 2 hours
  - Done when: hard drifting increases tire wear noticeably faster than gentle cornering; cruising wears tires very slowly.
  - **How:**
    1. In `VehicleBase.Wheels.cs`, after computing `lateralVel`, expose a per-wheel `float WheelLateralSlip[i]` (private field updated each tick).
    2. In `TickWear` (`Damage.cs`), instead of `MathF.Abs(SteerInput) > 0.5f` heuristic, loop wheels: `for each wheel: TireWear[i] += dt * lateralSlip[i] * wearScale`. Tune `wearScale` so a hard drift wears a tire 0→1 in ~30 seconds; cruising wears 0→1 in ~30 minutes.
  - **Refs:** Existing `Wheels.cs` skid detection (we already track `lateralMag`).

- [~] **Seat enter/exit system** — replace `TestDriverComponent`.
  - **STATUS 2026-05-17 (code-complete, pending playtest + scene migration):** Implemented as a **library/host split** (cleaner than the original "raycast on the player pawn" plan, which would force the library to know about gamemode pawns): (a) library `Libraries/Vehicles.Maintenance/Code/Components/VehicleSeat.cs` — pure mechanics: `[Sync] Guid OccupantId`, `[Property] IsDriverSeat`, `TryEnter(GameObject)`/`Exit()`, `[Rpc.Owner]` Enter/Exit, sets `Vehicle.HasDriver` on the driver seat, best-effort guarded `Network.AssignOwnership` handoff, clears HasDriver on destroy; auto-finds Vehicle via `GetInAncestorsOrSelf`. (b) `VehicleEvents.OnSeatEntered/OnSeatExited` added. (c) host `Code/Host/SeatInteractor.cs` — press-`use`(E) raycast from camera, resolves the hit to the vehicle, picks nearest free seat, chase camera while in driver seat; the interactor's own GameObject is the occupant stand-in (no pawn in test scene). **TestDriverComponent NOT deleted** (would break the user's `minimal.scene`) — marked SUPERSEDED in its doc; step (6) below is the migration. Remaining before `[x]`: in-scene playtest (add a SeatInteractor GO + a VehicleSeat on the sedan's driver seat anchor, verify E in/out + camera + HasDriver gating + that driving still works), then retire TestDriverComponent per the orphan item.
  - **STATUS 2026-05-17b (occupant = real pawn):** test scene had no character (TestDriver faked it via a glued camera). Per user decision, `SeatInteractor` now integrates s&box's built-in **`PlayerController`**: place SeatInteractor ON the Player object; on enter it disables the PlayerController + parents the player to the seat anchor (rides along) + chase cam; on exit it unparents, drops the player beside the car, re-enables the PlayerController (which resumes its own camera/movement). Occupant passed to `VehicleSeat.TryEnter` is the player GameObject. Migration now needs an s&box Player object in `minimal.scene`.
  - Files: new `Libraries/Vehicles.Maintenance/Code/Components/VehicleSeat.cs`. Delete `Code/Host/TestDriverComponent.cs` after.
  - Effort: 1 day
  - Done when: press-E on a vehicle attaches player to nearest seat; passenger seats also work.
  - **How:**
    1. `VehicleSeat` is a Component placed on each `SeatAnchor` child of a vehicle. `[Property] bool IsDriverSeat = false;`, `[Sync] Guid OccupantId`.
    2. On the player pawn, raycast forward on `Input.Pressed("use")`; if hit a `VehicleSeat` with no occupant, call `[Rpc.Owner] void RequestSit(Guid playerGuid)` on it.
    3. In `RequestSit`: set `OccupantId`, parent the player's GameObject to the seat's GameObject (or just sync position each tick), set the player's input capture to be the vehicle if driver seat.
    4. If `IsDriverSeat`, set `vehicle.HasDriver = true` and `vehicle.Network.AssignOwnership(player.Network.Owner)`. Pressing `use` again while seated calls a similar exit RPC that unparents + clears `HasDriver` (if last driver) + reassigns ownership back to host.
    5. Move the chase camera logic from `TestDriverComponent` into the driver seat's `OnUpdate` (gated on `OccupantId == LocalPlayerGuid`).
    6. Delete `Code/Host/TestDriverComponent.cs`.
  - **Refs:** TR §7 networking ownership · `Input.Pressed` (TR §14) · `Scene.Trace.Ray` (TR §8) · `Network.AssignOwnership` (API browser → `Sandbox.NetworkAccessor`).

- [x] **`Battery`/`Oil` modulation in EffectiveEnginePower** — once added by the previous task.
  - **STATUS:** ✅ done 2026-05-14 (indirectly). Battery gates `CanStartEngine` (binary — engine won't crank). Oil indirectly affects `EffectiveEnginePower` via accelerated `EngineHealth` decay when low (`IsLowOil`), which then drops `EngineHealthFactor`. No separate `OilFactor` property added — the cascade through engine health is gameplay-visible enough. Revisit if a more direct oil-affects-power link is desired.
  - Files: `VehicleBase.Tune.cs`
  - Effort: 30 min
  - **How:** Add `BatteryHealthFactor` / `OilHealthFactor` similar to `EngineHealthFactor`. Multiply into `EffectiveEnginePower`. Battery is binary-ish (engine just won't start); oil scales linearly below 20%.

- [x] **`VehicleBase.Sound.cs` partial** — engine pitch + horn playback.
  - **STATUS 2026-05-17 (actually implemented now):** the 2026-05-14 note below over-claimed — the audit found `TickSound` was a no-op stub (engine-pitch loop deferred "until SoundHandle API verified"). Now genuinely implemented: looping engine sound via `Sound.Play(Config.EngineSoundPath)` cached as a `SoundHandle`, per-tick `Pitch` from `EngineRpm/RedlineRpm` (`EngineMinPitch`..`EngineMaxPitch`), `Volume` rises with throttle, `Position` follows the car; loop stops on engine-off/destroy. One-shots on engine start (`EngineStartSoundPath`), gear shift (`GearShiftSoundPath`, on `OnShifted`), horn (`HornSoundPath`), tyre skid (`SkidSoundPath`, on `OnWheelSkidStarted`). 3 new optional `.vcfg` Audio fields added (v1.1 additive). All `SoundHandle` access try/catch-guarded. Still silent until the user authors `.sound` assets and assigns the paths — and the engine `.sound` MUST be marked looping. `vh.*` has no sound cmd; test by driving. Limitation: `TickSound` runs under `ShouldSimulate` so in networked play only the owner pitches its own engine (fine for solo v1).
  - **STATUS:** ✅ done 2026-05-14. Created. Pitch-modulates engine loop from `Config.EngineSoundPath` by `EngineRpm/RedlineRpm` (range tunable via `EngineMinPitch`/`EngineMaxPitch`). Plays `Config.HornSoundPath` on `OnHorn`. Starts/stops loop on `OnEngineStarted`/`OnEngineStopped` events. `TickSound(dt)` wired into `VehicleBase.OnUpdate`. `SoundSubscribe`/`Unsubscribe` wired into `OnStart`/`OnDestroy`. **Inaudible until** the user authors `.sound` assets and assigns paths to the `.vcfg`. Audio API surfaces (`SoundHandle.Position`/`.Pitch`/`.Volume`/`.Stop`) wrapped in try/catch for cross-version safety.
  - Files: new `Libraries/Vehicles.Maintenance/Code/Components/VehicleBase.Sound.cs`
  - Effort: 3 hours
  - Done when: holding W audibly winds the engine; gear shifts audibly punctuate; horn plays from `Config.HornSoundPath`.
  - **How:**
    1. New partial. Store `SoundHandle _engineLoop` private field.
    2. In `OnStart`: if `Config?.EngineSoundPath` is set, load via `Sound.Play(Config.EngineSoundPath, GameObject.WorldPosition)` and cache the handle; mark as looping.
    3. In `OnUpdate` (new `TickSound(dt)` called from `VehicleBase.OnUpdate`): `_engineLoop.Pitch = MathX.Lerp(0.5f, 1.5f, EngineRpm / RedlineRpm)`. Update position to `GameObject.WorldPosition` so 3D audio follows the car.
    4. Subscribe to `VehicleEvents.OnHorn` in `DebugSubscribe`-style block: play `Config.HornSoundPath` as a one-shot (`Sound.Play(path, WorldPosition)`).
    5. `OnDestroy`: stop the engine loop.
  - **Refs:** TR §15 audio · API browser → `Sandbox.Sound`, `SoundHandle`.

- [x] **`Vehicle.Spawn(VehicleConfig cfg, Vector3 worldPos, Rotation rot)` static helper.**
  - **STATUS 2026-05-19:** Done as a new partial `Code/Components/VehicleBase.Spawn.cs` (kept off the main `VehicleBase.cs`, matching the partial-per-concern layout): `public static VehicleBase Spawn( VehicleConfig cfg, Vector3 pos, Rotation rot, Connection owner = null )`. Reuses the proven prefab path (`ResourceLibrary.Get<PrefabFile>` → `SceneUtility.GetPrefabScene` → `Clone(pos,rot)`), finds `VehicleBase` (root, child fallback), assigns `Config` if unset, `NetworkSpawn(owner)` (or owner-less), and `VehicleHost.Current.SaveVehicleOwnership(go.Id, owner.SteamId, cfg)` when an owner is given. `OnVehicleSpawned` is NOT fired here — `VehicleBase.OnStart` already raises it on clone (verified). **`vh.spawn` refactored to call this** (its duplicated inline clone removed — single spawn path now). Verify: `vh.spawn sedan` once a `.vcfg` has `PrefabPath` set.
  - Files: extend `VehicleBase.cs` with a static method, or new `VehicleSpawner.cs`
  - Effort: 2 hours
  - Done when: one-line spawn from any caller instantiates prefab, applies config, registers ownership, fires `OnVehicleSpawned`.
  - **How:**
    1. Add `public static VehicleBase Spawn(VehicleConfig cfg, Vector3 pos, Rotation rot, Connection owner = null)` on `VehicleBase` (or a sibling static class).
    2. Use `SceneUtility.GetPrefabScene(cfg.PrefabPath).Clone(pos, rot)` to instantiate.
    3. Walk the cloned GameObject's components to find the `VehicleBase`; assign `Config = cfg` if not already pre-set.
    4. Call `go.NetworkSpawn(owner)` to register networking.
    5. If `owner` non-null, also call `VehicleHost.Current.SaveVehicleOwnership(vehicle.Id, owner.SteamId.Value, cfg)`.
    6. Return the `VehicleBase`.
  - **Refs:** TR §4 GameObject + Scene · cheat sheet (`var newGo = go.Clone()`) · `Networking.CreateLobby` / `NetworkSpawn` (TR §7) · API browser → `Sandbox.SceneUtility`.

- [ ] **Wrecked state (`OnVehicleWrecked` event).**
  - Files: `VehicleBase.State.cs`, `VehicleBase.Wheels.cs`, `Events/VehicleEvents.cs`
  - Effort: 3 hours
  - Done when: repeated crashes lead to a visibly wrecked state where engine refuses to produce force until body health > 5.
  - **How:**
    1. Add `public bool IsWrecked => BodyHealth <= 0` to `State.cs`.
    2. Update `IsEngineRunning` (in `Systems.cs`) to also check `!IsWrecked`.
    3. In `Damage.cs`'s `OnCollisionStart`: when `BodyHealth` crosses 0 downward, fire `VehicleEvents.RaiseWrecked(this)`.
    4. Add the `OnVehicleWrecked` event + raiser to `VehicleEvents.cs`.
    5. Host-side audio/VFX listener spawns smoke particles + crunch sound on wreck.
    6. In `RepairRpc(PartKind.Body, ...)`: if `IsWrecked` becomes false after the repair, fire `OnRepair` as normal (or add a new `OnVehicleUnWrecked` event if useful).
  - **Refs:** Existing `ICollisionListener` pattern in `Damage.cs` · existing event raiser pattern.
  - **Revisit:** If wrecked-state behavior accumulates 5+ distinct effects (smoke intensity, audio cues, despawn timer, input lockout, UI overlay, towing/impound integration) AND the same pattern emerges for other vehicle modes (impounded, parked, garaged), promote to the GoF **State pattern** — `VehicleLifecycleState` abstract base with `OnEnter / OnTick / AllowEngineForce / OnExit`, concrete `NormalState` / `WreckedState` / future modes. Don't refactor at one-bool stage (premature abstraction); revisit when the `if (IsWrecked)` branches start spreading across files.

- [ ] **Flip / stuck recovery.**
  - Files: new `VehicleBase.Recovery.cs` partial OR method on `VehicleBase`
  - Effort: 2 hours
  - Done when: rolled cars can be self-righted in place.
  - **How:**
    1. `[Rpc.Owner] public void RecoverRpc()` — check `Body.Velocity.Length < 5f` (only allowed when nearly stopped) and `_lastRecover.HasTimePassed(10f)` for cooldown.
    2. Set `WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() )` — preserves heading but zeroes pitch/roll.
    3. Lift the body slightly (`WorldPosition += Vector3.Up * 30`) to avoid clipping after rotation.
    4. Zero `Body.Velocity` and `Body.AngularVelocity`.
    5. Bind a key in seat code: `if ( Input.Pressed("reload") && _seat.IsDriverSeat ) vehicle.RecoverRpc();`
  - **Refs:** API → `Rotation` (Yaw, FromYaw) · `TimeUntil` / `TimeSince` (s&box helpers) · Input.

- [x] **Per-wheel impact damage.**
  - **STATUS 2026-05-19:** Done in `VehicleBase.Damage.cs`. `OnCollisionStart` now calls new `ApplyWheelImpact( collision.Contact.Point, impact )` after the body/engine damage: finds the nearest `WheelAnchors` entry by `LengthSquared` to the hit point; if within `WheelImpactRadius` and `nearest < TireWear.Count`, adds `(impact − ImpactDamageThreshold) × WheelImpactWearMultiplier` to that tyre (clamped ≤1) and raises `OnDamage(Tire)`. A very hard near-wheel hit (`impact ≥ WheelPunctureImpact`) sets `TireWear=1` and calls the existing `PunctureTireRpc(idx)` (mask bit + `OnTirePunctured`). 3 new `[Property, Group("Damage")]` knobs: `WheelImpactRadius=24`, `WheelImpactWearMultiplier=0.0006`, `WheelPunctureImpact=1200`. Owner/sim-gated (reuses the existing `ShouldSimulate` guard at the top of `OnCollisionStart`). Verify: drive a wheel into a wall → that tyre's wear rises (watch `vh.debug` `minTire%`); a fast side-slam punctures it (car pulls / `OnTirePunctured`).
  - Files: `VehicleBase.Damage.cs`
  - Effort: 2 hours
  - Done when: scraping a wall raises `TireWear` for the specific wheel.
  - **How:**
    1. In `OnCollisionStart(Collision c)`: get `c.Contact.Point` (world-space hit position).
    2. Find the closest wheel anchor by minimal `(anchor.WorldPosition - hitPoint).LengthSquared`.
    3. If the impact magnitude > a per-wheel threshold and the closest distance is < some radius (say 20 inches), add to that wheel's `TireWear` proportional to impact, OR if very hard, set `TirePunctureMask` bit via `PunctureTireRpc(idx)`.
  - **Refs:** TR §8 physics + `Collision.Contact` (API → `Sandbox.Collision`).

---

## 1b. Orphan code & dead-end APIs

Surface area that exists in the codebase but isn't wired to anything. Each must either be **wired** or **deleted** — orphan code rots into bugs.

- [x] **`IRepairAction` interface — wired via `RepairActionRegistry`.** Strategy pattern. Library calls `RepairActionRegistry.TryInvoke(part, vehicle, mechanic)` first; if a gamemode-registered action runs, default flow is skipped. `RepairTool.cs` consumes this. DiagnosticPanel will consume it when its buttons are wired (Section 1). Gamemodes register custom flows in their `IVehicleHost` bootstrap.
  - **STATUS 2026-05-17:** the `TryInvoke`-first call now lives once in `Code/Contracts/RepairFlow.cs`; both `RepairTool` and `DiagnosticPanel` go through it, so the registry hook is no longer duplicated across consumers.

- [ ] **`VehicleConfig.CargoCapacityKg` is unused.**
  - Effort: 1 day to implement (trunk inventory) or 5 min to delete
  - **How (implement path):**
    1. Add `[Sync] public NetList<Guid> CargoItemIds { get; set; }` to `VehicleBase.State.cs`.
    2. Define `Spec ICargoItem { float WeightKg; Guid ItemId; }`. Gamemode adapter implements.
    3. `[Rpc.Owner] AddCargoRpc(Guid)` / `RemoveCargoRpc(Guid)` — enforce total weight ≤ `Config.CargoCapacityKg`.
    4. Expose `CurrentCargoWeight` and modify `EffectiveEnginePower` to scale down with load (heavier car = weaker acceleration).
  - **Refs:** Same NetList pattern as `TireWear`.

- [x] **`VehicleConfig.HornSoundPath` and `EngineSoundPath` are unused.** Covered by Sound.cs (Section 1).
  - **STATUS:** ✅ resolved 2026-05-14. Both fields are now consumed by `VehicleBase.Sound.cs`. Will play silently until `.sound` assets exist and paths are filled in the `.vcfg`.

- [ ] **`VehicleConfig.PaintTintable` is unused.**
  - Effort: 2 hours
  - **How:** In Section 3 "Material variants" task. Add `[Sync] Color PaintTint` to `VehicleBase.State.cs`; only writable if `Config.PaintTintable`. The vehicle's `ModelRenderer` reads it via `SceneObject.Attributes.Set("Tint", PaintTint)` in `OnUpdate`. Material must have a `Tint` parameter exposed.
  - **Refs:** API → `ModelRenderer`, `SceneObject.Attributes`; Material Editor docs at https://sbox.game/dev/doc/assets/resources/index .

- [~] **`VehicleConfig.PrefabPath` and `ModelPath` are unused.** Solved by `Vehicle.Spawn` helper (Section 1).
  - **STATUS 2026-05-19:** `PrefabPath` is now consumed — `VehicleBase.Spawn` (and therefore `vh.spawn`) loads & clones it. `ModelPath` is still unused; it's for runtime ModelRenderer setup and stays open until the model/prefab tasks in §3.

- [x] **`UI/FuelGauge.razor` is a free-floating Panel.**
  - **STATUS 2026-05-17:** Resolved by inlining (option B). `VehicleHud.razor` now includes a fuel bar; the audit confirmed `FuelGauge.razor` was referenced by zero code (only the README), so the dead file was **deleted**. README still mentions it — clean that up in the publishing pass.
  - **How:** Absorb into `VehicleHud.razor` (Section 5). Keep `FuelGauge.razor` as a child component that VehicleHud composes, OR inline its content and delete the file.

- [x] **`VehicleEvents.OnHorn` / `OnEngineStarted` / `OnEngineStopped` fire but no consumer.** Covered by `VehicleBase.Sound.cs` (Section 1).
  - **STATUS:** ✅ resolved 2026-05-14. `Sound.cs` subscribes to all three.

- [~] **`Code/Host/TestDriverComponent.cs` retires when seats land.** See Section 1 seat task. Delete after.
  - **STATUS 2026-05-17:** Seat system landed (`VehicleSeat` + `SeatInteractor`). TestDriverComponent intentionally kept (not deleted) so the user's `minimal.scene` keeps loading — its doc now says SUPERSEDED. Delete it only once the scene is migrated to SeatInteractor and the seat flow is playtested.

---

## 2. Driving fine-tuning + .vtune presets

- [x] **Tune the existing `sedan.vcfg`** — `MaxEngineForce=160021` is too high.
  - **STATUS:** ✅ partial 2026-05-14. The `.vcfg` itself was already at sane defaults (MaxSpeedKmh=140, MassKg=1400, EngineTorqueNm=250, BrakeStrength=1, Grip=0.85). `MaxEngineForce` lives on the `VehicleBase` *component* (in the scene/prefab), not the `.vcfg` — user's recent log confirms it's now ~12000 (`effEngine=24003N` ÷ tune mul 2.0). **Also fixed:** the existing `new vehicle tune.vtune` had `EnginePowerMultiplier=2.0` which doubled everything (sport-car feel even on baseline) — set to 1.0 for sedan-default behavior. After this change, `effEngine ≈ 12000N` ⇒ ~8.6 m/s² ⇒ 0-100 in ~3.2s, sedan-appropriate.
  - **STATUS 2026-05-17 (kinematic — knobs changed):** under the kinematic controller `MaxEngineForce`/`effEngine` no longer set acceleration directly — the perceived-mass knob is now **`MaxForwardAccelMs2`** (default 6 m/s², on the VehicleBase component, Engine group) which hard-caps per-tick longitudinal Δv, plus the `(1-ratio²)` top-speed taper. Mass is read from **`Config.MassKg`** via the `VehicleMass` helper (Body.Mass reads 0 when kinematic). Steering feel knobs: **`FullSteerSpeedKmh`** (default 30) + **`YawRateScale`** (default 3). Re-tune the sedan using *those* fields, not `MaxEngineForce`. The 2026-05-14 force-based reasoning above is retained for history but no longer the active model.
  - Files: `Assets/sedan.vcfg` (in editor)
  - Effort: 15 min
  - **How:**
    1. In editor's Asset Browser → double-click `sedan.vcfg` to open inspector.
    2. Set: `MaxSpeedKmh=140`, `MassKg=1400`, `EngineTorqueNm=250`, `BrakeStrength=1.0`, `Grip=0.85`.
    3. Then on the VehicleBase component on the prefab: `MaxEngineForce=15000`, `SuspensionStiffness=30000`, `SuspensionDamping=3000`, `LateralFriction=0.85`, `Downforce=5000`, `AirDrag=0.5`.
  - **Refs:** TR §9 GameResource · existing `VehicleConfig` schema in `Resources/VehicleConfig.cs`.

- [ ] **Author the 6 default `.vtune` presets** in `Libraries/Vehicles.Maintenance/Assets/tunes/`.
  - Effort: 1–2 hours total
  - **How:**
    1. In editor Asset Browser → right-click → New → Vehicle Tune (the `.vtune` shows up because of `[AssetType(Extension="vtune")]` we set on the resource).
    2. Save in `Libraries/Vehicles.Maintenance/Assets/tunes/`.
    3. Set inspector values per below. Set `PresetName` to match.
    - `grip.vtune` — FrontGripMultiplier=1.4, RearGripMultiplier=1.4, DownforceMultiplier=1.5, SteeringMultiplier=1.2
    - `drift.vtune` — RearGripMultiplier=0.5, FrontGripMultiplier=1.0, SteeringMultiplier=1.3, BrakeMultiplier=0.9
    - `rally.vtune` — FrontGripMultiplier=1.1, RearGripMultiplier=0.8, SuspensionStiffnessMultiplier=0.7, SuspensionDampingMultiplier=0.7
    - `heavy.vtune` — EnginePowerMultiplier=1.5, BrakeMultiplier=1.5, SuspensionStiffnessMultiplier=1.5, FrontGripMultiplier=0.8, RearGripMultiplier=0.8
    - `sport.vtune` — EnginePowerMultiplier=1.5, BrakeMultiplier=1.3, DownforceMultiplier=1.3, SteeringMultiplier=1.2
    - `arcade.vtune` — all 1.0 (baseline)
  - **Refs:** Existing `VehicleTuneProfile.cs` schema · TR §9.

- [ ] **Tune gear ratios for less aggressive shifting**
  - Effort: 30 min
  - **How:** On VehicleBase inspector: lower `MaxEngineForce` (above) OR change `ForwardGearTorqueMultipliers` from `{2.5, 1.8, 1.3, 1.0, 0.8}` to a flatter `{1.8, 1.4, 1.2, 1.0, 0.85}` for slower acceleration through gears.

---

## 3. 3D models + materials

- [ ] **Find or import a sedan `.vmdl`**
  - Effort: 30 min reuse / 4+ hours custom
  - **How:**
    - **Reuse:** check the s&box content browser for `models/citizen_props/` or asset.party for free car models. Filter by license.
    - **Custom:** export from Blender as FBX → in editor right-click → Create Model → ModelDoc opens → set physics shape (BoxColliders for body, ConvexHulls for fenders), set materials, save as `.vmdl`. Detailed guide at https://sbox.game/dev/doc/assets/resources/index .
  - **Refs:** TR §11 naming conventions (textures: `_color/_normal/_rough`) · API → `Sandbox.Model` · https://sbox.game/dev/doc/getting-started/explore-engine#model .

- [ ] **Wheel anchor positioning** — match the model's wheel positions.
  - Files: vehicle prefab in editor
  - Effort: 30 min
  - **How:**
    1. Open the vehicle prefab.
    2. Move each `WheelAnchor*` child GameObject (the empty ones we created) so its World position sits where a wheel hub should be on the body model — typically 4 corners, vertical position aligned with axle line.
    3. Drag each anchor into `VehicleBase.WheelAnchors` list in inspector. Order: front-left, front-right, rear-left, rear-right (the first `FrontWheelCount` entries are treated as steering wheels).
  - **Refs:** TR §4 GameObject hierarchy · Scene editor docs.

- [ ] **`ModelRenderer` on the prefab**
  - Effort: 5 min
  - **How:** Select the vehicle root GameObject → Add Component → ModelRenderer → drag your `.vmdl` into Model slot. Hide or remove the placeholder BoxCollider's debug visual if needed (the collider stays, just visual).
  - **Refs:** API → `Sandbox.ModelRenderer`.

- [x] **Headlight prefab/component**
  - **STATUS:** ✅ done 2026-05-14. `VehicleHeadlight.cs` created. Drop on a child GameObject with a `SpotLight` (auto-found in `OnAwake`). Polls the parent vehicle's `HeadlightsOn` synced flag each `OnUpdate` and toggles `Light.Enabled` accordingly. Polls (rather than subscribing to `OnHeadlightsToggled`) so it recovers cleanly from hotload / proxy churn. Inactive until placed on a child of a vehicle with a SpotLight.
  - Files: new `Libraries/Vehicles.Maintenance/Code/Components/VehicleHeadlight.cs`
  - Effort: 2 hours
  - **How:**
    1. New `Component`: `[Property] VehicleBase Vehicle; [Property] SpotLight Light;` (auto-find both in `OnAwake`).
    2. Subscribe to `VehicleEvents.OnHeadlightsToggled` filtering by `v == Vehicle`. Set `Light.Enabled = on`.
    3. Place 2 `SpotLight` GameObjects as children at headlight positions on the model.
  - **Refs:** API → `Sandbox.SpotLight` · `VehicleEvents.OnHeadlightsToggled` (already defined).

- [x] **`VehicleDoor` component**
  - **STATUS:** ✅ done 2026-05-14. `VehicleDoor.cs` created. Captures closed local rotation in `OnAwake`; lerps `LocalRotation` toward open or closed each frame based on `Vehicle.IsDoorOpen(DoorIndex)`. Inspector knobs: `DoorIndex` (0..31, must match `ToggleDoorRpc` bit), `OpenAngle` (default 70°), `SwingSpeed` (default 5), `HingeAxis` (default Vector3.Up = vertical hinge, typical car door). One component per door child GameObject. Networked because it polls a `[Sync]` field — no manual replication needed.
  - Files: new `Libraries/Vehicles.Maintenance/Code/Components/VehicleDoor.cs`
  - Effort: 3 hours
  - Done when: clicking a door visibly swings it; networked.
  - **How:**
    1. New `Component`: `[Property] VehicleBase Vehicle; [Property] int DoorIndex; [Property] float OpenAngle = 60f; [Property] float SwingSpeed = 3f;`
    2. Store closed local rotation in `OnAwake`. In `OnUpdate`, lerp `LocalRotation` between closed and open based on `Vehicle.IsDoorOpen(DoorIndex)` and `SwingSpeed * Time.Delta`.
    3. Subscribe to `OnDoorOpened/Closed` (optional, just for VFX/audio hooks; the visual is driven from the synced state in `OnUpdate`).
  - **Refs:** Cheat sheet (`go.LocalRotation`) · existing `Systems.cs` `IsDoorOpen` helper.

- [ ] **Part models**
  - Effort: 4 hours
  - **How:** Reuse Citizen props or asset.party free models. Drop each `.vmdl` into a `PartDefinition.ModelPath` field on the corresponding `.partdef` asset. Used by `PartItemPickup` for world-spawned pickups and (later) by `PartsShopPanel` UI thumbnails.

- [ ] **Material variants for `PaintTintable=true`**
  - Effort: 2 hours
  - **How:**
    1. Open the body material in Material Editor; expose a `Color` parameter named `Tint`.
    2. In a `VehiclePaint.cs` host component: `[Sync] Color PaintTint`; in `OnUpdate` push to `ModelRenderer.SceneObject.Attributes.Set("Tint", PaintTint)`.
    3. Dealer UI (Section 5) lets player pick `PaintTint` if `Vehicle.Config.PaintTintable`.
  - **Refs:** Material editor docs · API → `SceneObject.Attributes`.

- [~~] **Detachable part visuals** — deferred per GUIDE.md Section 6.

---

## 4. VFX + audio

**Note on the existing schema:** `VehicleConfig.EngineSoundPath` and `HornSoundPath` are declared but unused (Section 1b). The work below makes them live.

- [ ] **Engine sound** — looping `.sound` asset, pitched by RPM.
  - **STATUS 2026-05-17:** code side DONE (`VehicleBase.Sound.cs` plays + RPM-pitches the loop). Only the asset is missing — author a looping engine `.sound`, set `VehicleConfig.EngineSoundPath`. Item closes when audible.
  - Files: `Libraries/Vehicles.Maintenance/Assets/sounds/engine_default.sound`
  - Effort: 1 hour
  - **How:**
    1. Find/record a smooth engine loop (freesound.org, CC-licensed). Export as `.wav`.
    2. In editor: right-click .wav → Create Sound. Set `Looping = true`, optionally adjust falloff / volume.
    3. Reference path from `VehicleConfig.EngineSoundPath` on sedan.vcfg.
    4. Consumed by `VehicleBase.Sound.cs` (Section 1).
  - **Refs:** TR §15 audio · https://sbox.game/dev/doc/media/audio .

- [ ] **Horn sound** — one-shot `.sound`.
  - **STATUS 2026-05-17:** code side DONE (played on `OnHorn` from `Config.HornSoundPath`); also wired: gear-shift (`GearShiftSoundPath`), engine-start (`EngineStartSoundPath`), skid (`SkidSoundPath`). Just author the `.sound` assets + set the paths.
  - Files: `Libraries/Vehicles.Maintenance/Assets/sounds/horn_default.sound`
  - Effort: 15 min
  - **How:** Same as engine but `Looping = false`. Set `Config.HornSoundPath`. Played from `VehicleBase.Sound.cs` on `OnHorn`.

- [ ] **Skid VFX + audio** — tire-mark decals + screech.
  - **STATUS 2026-05-17:** a basic skid **one-shot** is already wired in `VehicleBase.Sound.cs` (`OnWheelSkidStarted` → `Config.SkidSoundPath`). Still TODO: the looping screech (start/stop handle) + tyre-mark decals via the host `SkidEffectsListener` below.
  - Files: new `Code/Host/SkidEffectsListener.cs`
  - Effort: 3 hours
  - **How:**
    1. Host-side component subscribes to `VehicleEvents.OnWheelSkidStarted` / `OnWheelSkidStopped`.
    2. On start: spawn a decal-emitter prefab at the wheel's `WorldPosition`; spawn a looping screech sound (cache the handle).
    3. On stop: stop the sound and the decal emitter.
  - **Refs:** API → `DecalRenderer` / `ParticleSystem` · existing skid events in `VehicleEvents.cs`.

- [ ] **Crash sound + damage particles**
  - Effort: 2 hours
  - **How:** Subscribe to `VehicleEvents.OnDamage` host-side. Play impact `.sound` at `v.GameObject.WorldPosition`; spawn dust particles. Scale volume/intensity by `damageAmount`.
  - **Refs:** Existing `OnDamage` event.

- [ ] **Engine-on / engine-off audio cues**
  - Effort: 1 hour
  - **How:** Subscribe to `OnEngineStarted/Stopped` in `VehicleBase.Sound.cs`. Play short starter / shutdown sounds.

- [ ] **Redline warning beep**
  - Effort: 30 min
  - **How:** Subscribe to `OnEngineRpmRedlined`. Play a short beep `.sound` from the seated player's perspective only (gated on `Game.LocalPlayer == driver`).

- [ ] **Wrench sound on repair**
  - Files: `Libraries/Vehicles.Maintenance/Assets/sounds/wrench.sound`
  - Effort: 15 min
  - **How:** Subscribe to `OnRepair` in a host VFX listener.

- [ ] **Refuel sound** — pump-like gurgle.
  - Effort: 15 min
  - **How:** Subscribe to `OnRefuel`.

- [ ] **Exhaust smoke when BodyHealth low**
  - Files: new `VehicleSmokeFx.cs` host component
  - Effort: 2 hours
  - **How:**
    1. Component listens for ticks; on each, set `particleEmitter.RateModifier = MathX.Clamp(1f - Vehicle.BodyHealthPct, 0f, 1f)`.
    2. Particle prefab placed at the back of the vehicle.
  - **Refs:** API → `ParticleSystem`.

- [ ] **Headlight cone VFX** — fog/cookie polish.
  - Effort: 1 hour
  - **How:** On the `SpotLight`, assign a cookie texture (`_color` channel with the headlight shape); enable volumetric fog interaction in the light's properties.
  - **Refs:** API → `SpotLight.CookieTexture` (verify exact property name in API browser).

---

## 5. UI

- [ ] **DiagnosticPanel polish**
  - Files: `Libraries/Vehicles.Maintenance/Code/UI/DiagnosticPanel.razor` + `.scss`
  - Effort: 4 hours (after Section 1 button wiring)
  - **How:**
    1. After Section 1 button wiring, focus on visual polish in `.scss`: progress-bar fill color shifts red as health drops; pulsing animation when below 20%.
    2. Add small icons next to each part name.
    3. Auto-rebuild on `Sync` value changes by including all relevant fields in `BuildHash()`.
  - **Refs:** UI styling · `.scss` flex layout · `BuildHash` is a `Sandbox.UI.Panel` lifecycle method.

- [~] **In-vehicle HUD**
  - **STATUS 2026-05-17 (code-complete, pending playtest):** `VehicleHud.razor` + `.scss` created — modern bottom-center cluster: big speed (km/h), gear badge (R/N/1..), gradient RPM bar (reddens at ≥92% redline), fuel + speed bars, warning pills (FUEL/ENGINE/OIL/BATT/TIRE/ENGINE OFF). Library-clean: static `VehicleHud.Show(VehicleBase)`/`Hide()`, lazily mounts under the scene's `ScreenPanel` (same requirement as `vh.diag`); a real gamemode calls Show with its local driver's vehicle. `SeatInteractor` calls `Show` on driver-seat enter, `Hide` on exit/teardown. `vh.hud` console command toggles it on the nearest vehicle for visual iteration without seats. Absorbed the fuel readout and **deleted dead `FuelGauge.razor`** (see orphan item §1b). Remaining before `[x]`: in-scene playtest (needs a `ScreenPanel` GameObject in the scene), tune visuals to taste.
  - **STATUS 2026-05-17c (racing redesign):** reworked into a **racing HUD** — circular speedometer with a CSS `transform:rotate` needle (no image assets; ticks/number labels placed by computed sin/cos coords, 250° sweep), 10-segment shift-light bar (green→amber→red by RPM), centre digital km/h + gear badge, inline fuel bar, and a dashboard tell-tale row (CHECK/OIL/BATT/TIRE/PWR, dim until active). Static API (`Show`/`Hide`/`Target`/BuildHash) unchanged so SeatInteractor + `vh.hud` wiring still holds.
  - Files: new `Libraries/Vehicles.Maintenance/Code/UI/VehicleHud.razor` (+ `.scss`)
  - Effort: 1–2 days
  - Done when: when seated, full HUD shows speed/RPM/gear/fuel + warnings; auto-hides on exit.
  - **How:**
    1. `VehicleHud` is a `ScreenPanel` placed in the scene OR built dynamically by the driver seat when occupied.
    2. Layout: bottom-center cluster with speed (big), RPM bar (curved or linear), gear (large), fuel bar.
    3. Top-right warning lights: low fuel (Fuel < 10%), engine damage (EngineHealth < 30%), any puncture (TirePunctureMask != 0).
    4. Absorb `FuelGauge.razor` either as a child or inline its content; delete the file when done.
    5. In `BuildHash()`: include `Vehicle.Fuel, EngineRpm, CurrentGear, EngineHealth, TirePunctureMask`.
  - **Refs:** TR §13 UI · UI basics · UI styling.

- [ ] **Cockpit / interior camera mode**
  - Effort: 3 hours
  - **How:**
    1. In the driver seat's update loop, watch `Input.Pressed("camera-1")` / `Pressed("camera-2")` for cycling.
    2. Maintain a list of camera positions (chase, hood, cockpit, cinematic) — each defined relative to vehicle. Lerp to selected one.
    3. Cockpit cam requires a body-interior seat anchor position; if model has no interior, fall back to hood.

- [ ] **Mechanic shop UI**
  - Files: new `Libraries/Vehicles.Maintenance/Code/UI/PartsShopPanel.razor`
  - Effort: 1 day
  - **How:**
    1. List all `PartDefinition` assets via `ResourceLibrary.GetAll<PartDefinition>()`.
    2. Per row: name + icon + price + Buy button.
    3. Buy → `VehicleHost.Current.TryCharge(player, def.Price, $"Bought {def.DisplayName}")`; if succeeds, `inv.Add(def, 1)`.
  - **Refs:** TR §9 ResourceLibrary lookup · existing `IPartInventory` contract.

- [ ] **Dealer UI**
  - Files: new `Libraries/Vehicles.Maintenance/Code/UI/VehicleDealerPanel.razor`
  - Effort: 1 day
  - **How:**
    1. List `VehicleConfig.WithTag("for-sale")` (or default to all) — name + a preview image + price.
    2. Buy → charge → call `VehicleBase.Spawn(cfg, spawnPos, spawnRot, player)`.
    3. Optionally let player pick paint color (set `PaintTint` after spawn).
  - **Refs:** Same as PartsShopPanel · `Vehicle.Spawn` helper (Section 1).

- [ ] **Tune-profile select UI**
  - Effort: 0.5 day on top of dealer
  - **How:** Iterate `ResourceLibrary.GetAll<VehicleTuneProfile>()`; selected one assigns to `Vehicle.Tune`.

- [x] **Toast notifications**
  - **STATUS 2026-05-17:** landed via the §1 feedback-hints item. `Code/UI/Toast.razor` (+`.scss`) — reusable static `Toast.Show(string, float seconds=3)`, mounts through `VehicleUi.MountRoot()`, stacked + auto-expiring with a CSS fade. Any gamemode/library code can call `Toast.Show(...)`. Pending the same in-scene playtest as the feedback-hints item.
  - Files: new `Libraries/Vehicles.Maintenance/Code/UI/Toast.razor`
  - Effort: 3 hours
  - **How:** Static `Show(string)` method appends a transient div to the root panel; auto-removes after timeout; SCSS fade-in/out. See Section 1 RepairTool feedback for usage.

- [ ] **Repair-tool reticle**
  - Effort: 3 hours
  - **How:**
    1. In `RepairTool.OnUpdate` (when held): raycast forward; if hit a `VehicleBase`, render a `Sandbox.UI.Panel` reticle showing the target part's name.
    2. Use `Sandbox.UI.WorldPanel` or screen-space conversion of hit position.
  - **Refs:** UI basics · `Scene.Trace` (TR §8).

- [ ] **PartSelectPanel** — see Section 1.

---

## 6. World / scene content

- [ ] **Garage scene**
  - Files: `Assets/scenes/garage.scene`
  - Effort: 1 day
  - **How:**
    1. In editor: File → New Scene → save as `garage.scene`.
    2. Build a small interior with primitive blocks or use a free map from sbox.game.
    3. Place: parking-spot floor markers, `FuelPump` prop, parts shop counter with `PartsShopNpc`, vehicle dealer with `VehicleDealerNpc`, the `VehiclesMaintenanceBootstrap` GameObject.
    4. Set this as `Metadata.StartupScene` in `carmaintenance.sbproj`.
  - **Refs:** TR §4 Scene · Scene editor docs.

- [ ] **Fuel station prop + FuelPump scene placement**
  - Effort: 2 hours
  - **How:** Use any fuel pump prop; add a `FuelPump` component to the prop's GameObject. Use a trigger volume around it (`BoxCollider IsTrigger=true`) — when player enters and presses E, call `pump.TryUse(nearestVehicle, player)`.

- [ ] **Vehicle dealer NPC**
  - Files: new `Code/Host/VehicleDealerNpc.cs`
  - Effort: 4 hours
  - **How:** Component with a `PressE-to-interact` zone; opens `VehicleDealerPanel.razor` for the player.

- [ ] **Parts shop NPC**
  - Files: new `Code/Host/PartsShopNpc.cs`
  - Effort: 4 hours
  - **How:** Same pattern as dealer; opens `PartsShopPanel.razor`.

- [ ] **Mechanic terminal** — alt to NPC.
  - **How:** Same code, just a different visual prop (computer model + screen).

- [x] **A few `PartDefinition` assets**
  - **STATUS:** ✅ done 2026-05-14. Authored 5 `.partdef` files in `Assets/parts/` (project-root, not the library — matching where user already keeps `sedan.vcfg`): `engine_kit.partdef` (Engine, +50, $200), `tire_set.partdef` (Tire, +50, $150), `body_panel.partdef` (Body, +40, $100), `battery.partdef` (Battery, +100, $80), `oil_can.partdef` (Oil, +100, $30). Hand-written JSON matching the `[AssetType(Extension="partdef")]` schema (no model/icon paths yet). `vh.parts` should now list 5; `vh.give engine_kit 5` works.
  - Files: `Libraries/Vehicles.Maintenance/Assets/parts/*.partdef`
  - Effort: 1 hour data entry
  - **How:** Asset Browser → New → Part Definition → fill in name, `RepairsPart`, `RepairAmount`, `Price`, model. Make: `engine_kit.partdef` (Engine, 50, 200), `tire_set.partdef` (Tire, 50, 150), `body_panel.partdef` (Body, 40, 100), `battery.partdef` (Battery, 100, 80), `oil_can.partdef` (Oil, 100, 30).

---

## 7. Gameplay integration (host-side)

- [ ] **Real currency in `CarMaintenanceVehicleHost`**
  - Files: `Code/Host/CarMaintenanceVehicleHost.cs`, new `Code/Host/PlayerWallet.cs`
  - Effort: 2 hours
  - **How:**
    1. Add `PlayerWallet` Component on the player pawn: `[Sync] long Balance { get; set; }`. Init from save (Section 9).
    2. In `CarMaintenanceVehicleHost.TryCharge`: find the player's `PlayerWallet`, return `false` if `Balance < amount`; else `Balance -= amount`.
    3. In `Pay`: `Balance += amount`.
  - **Refs:** TR §7 `[Sync]`.

- [ ] **Mechanic job toggle**
  - Effort: 1 hour
  - **How:** Add `bool IsMechanic` to `PlayerWallet` (or new `PlayerJob` component). Console command or UI button toggles it. `CarMaintenanceVehicleHost.IsMechanic` reads it.

- [ ] **Earn money for repairs** — already wired in `RepairTool.cs`. Verify in playtest.

- [ ] **Earn / spend on refuel** — already wired in `FuelPump.cs`. Verify.

- [ ] **Persist player wallet across scene reloads**
  - Effort: 1 hour
  - **How:** In `PlayerWallet.OnAwake`: `Balance = FileSystem.Data.ReadJson<long>("wallet.json")`. On `OnDestroy` or `[GameEvent.Shutdown]`: `FileSystem.Data.WriteJson("wallet.json", Balance)`.
  - **Refs:** TR §12 file system.

---

## 8. DarkRP adapter implementations

- [ ] **`SouSou63DarkRPVehicleHost`**
  - Files: new `Adapters/SouSou63DarkRP/SouSou63DarkRPVehicleHost.cs` in a fork of [sousou63/DarkRP](https://github.com/sousou63/DarkRP)
  - Effort: 1.5–2 days
  - **How:**
    1. Clone sousou63/DarkRP. Inspect `Code/Economy/` for their currency API (likely a `Money` static or `Player.Money` extension).
    2. Inspect `Code/Jobs/` for how jobs are registered. Add a `MechanicJob` definition that maps to our `IVehicleHost.IsMechanic` check.
    3. Implement `IVehicleHost` translating each method to their APIs (TryCharge → their `RemoveMoney`, Pay → their `AddMoney`, GetInventory → adapt their inventory).
    4. Add `VehicleHost.Register(new SouSou63DarkRPVehicleHost())` to their startup.
    5. Test in their lobby; iterate.
  - **Refs:** [sousou63/DarkRP repo](https://github.com/sousou63/DarkRP) · our `IVehicleHost` contract.

- [ ] **`DXRPVehicleHost`**
  - Files: new `Adapters/DXRP/DXRPVehicleHost.cs` in a fork of [dxura/dxrp-public](https://github.com/dxura/dxrp-public)
  - Effort: 1.5–2 days
  - **How:** Same shape; their layout has `sdk/` + `Code/Entities/`. Hook their currency/job system.
  - **Refs:** [dxura/dxrp-public](https://github.com/dxura/dxrp-public).

- [ ] **README in each adapter folder** — explain "drop this in your gamemode, add one line of registration."

---

## 9. Persistence

- [ ] **Vehicle ownership save/load**
  - Files: `Code/Host/CarMaintenanceVehicleHost.cs`
  - Effort: 2 hours
  - **How:**
    1. In `SaveVehicleOwnership`: `FileSystem.Data.WriteJson($"vehicles/{vehicleId}.json", new { SteamId = steamId, ConfigName = cfg.ResourceName })`.
    2. In `TryLoadVehicleOwnership`: read the JSON, look up `VehicleConfig.Find(ConfigName)`.
    3. Make sure `FileSystem.Data` has a `vehicles` subfolder (`FileSystem.Data.CreateDirectory("vehicles")` if needed).
  - **Refs:** TR §12 file system · note: only `[Property]` are serialised — use auto-properties.

- [ ] **Vehicle state save**
  - Effort: 2 hours
  - **How:** Extend the saved JSON to include `Fuel`, `EngineHealth`, `BodyHealth`, `TireWear[]`, `TirePunctureMask`. On spawn, if save exists, apply post-OnAwake.

- [ ] **Part inventory save**
  - Effort: 1 hour
  - **How:** `InMemoryPartInventory` adds `Save()` / `Load()` methods using `FileSystem.Data.WriteJson($"inventory_{steamId}.json", ...)`. Call `Load` in `CarMaintenanceVehicleHost.GetInventory` first-access; call `Save` on `Add` / `TryConsume`.

---

## 10. Networking hardening

- [ ] **Multiplayer test session**
  - Effort: 1 day
  - **How:**
    1. In one client: `Networking.CreateLobby(new LobbyConfig { MaxPlayers = 4, Privacy = LobbyPrivacy.Friends })`.
    2. Have a friend `Networking.Connect(lobbyId)`.
    3. Drive, exit, let friend enter; verify ownership transfers; verify damage/repair/refuel/door/light/horn all replicate.
    4. Check `DebugLog` output on both ends — events should fire on the right side (owner vs proxy).
  - **Refs:** TR §7 networking · [Lobbies doc](https://sbox.game/dev/doc/networking/lobbies).

- [ ] **Ownership transfer on seat exit**
  - Files: `VehicleSeat.cs` (Section 1)
  - Effort: 3 hours
  - **How:** On exit, if this was the driver seat, call `vehicle.GameObject.Network.AssignOwnership(null)` (passing null = host). If the player disconnects, the engine should auto-transfer; verify.

- [ ] **`LocalSimulation` flip for shipping**
  - Effort: 30 min
  - **How:** Leave `LocalSimulation = true` as default (it's a Property, so per-instance). For published vehicles in actual multiplayer, networking will use the IsOwner check anyway via `ShouldSimulate`. No code change; just decide & document.

- [ ] **Reject impossible state from clients** (optional)
  - Effort: 4 hours
  - **How:**
    1. Owner-side validator: in `RefuelRpc`, clamp `litres` to a sane max (e.g. `MathX.Clamp(litres, 0, Config.FuelCapacityLitres)`).
    2. Same for `RepairRpc(amount)`.
    3. In `Network.OnRpcReceived` or similar, log suspicious values (or kick if you want strict).
  - **Refs:** Our `feedback_sbox_physics_quirks` memory mentions owner-authoritative semantics; that's the model.

- [~~] **Full host-validated input-command networking** — deferred per GUIDE.md Section 6.

---

## 11. Testing

- [x] **Dev console commands** — `Code/Host/VehicleDevCommands.cs`. 20 commands implemented covering: discovery (`vh.help`, `vh.list`, `vh.status`, `vh.cfgs`, `vh.tunes`), spawn/destroy (`vh.spawn`, `vh.kill`), damage/repair (`vh.damage`, `vh.repair`, `vh.refuel`, `vh.fuel`, `vh.heal`), powertrain (`vh.shift`, `vh.tune`), systems (`vh.puncture`, `vh.engine`, `vh.lights`, `vh.door`, `vh.horn`), recovery + debug (`vh.flip`, `vh.debug`, `vh.cheat`). Also added `DamageRpc` to `VehicleBase.State.cs` (mirror of `RepairRpc`) and promoted `SetGear` to public. Note: if `[ConCmd("name")]` doesn't compile in your s&box version, swap to `[ConCmd.Server("name")]` — both forms have shipped in different sbox builds.
  - **STATUS UPDATES:** 2026-05-14 — gap follow-ups closed: (a) `vh.cfgs` filter via new `VehicleConfig.All` static (rejects engine `core/cfg/*.cfg` false-positives). (b) `vh.status` now emits one `Log.Info` per line (engine console truncates multi-line strings) and includes battery/oil. (c) `vh.heal` now also restores Battery + Oil. (d) `vh.fuel` routes positive deltas through `RefuelRpc` so `OnRefuel` listeners fire. (e) `vh.shift` calls `Powertrain.LockShifts(5f)` so the chosen gear actually sticks instead of being immediately overridden by auto-shift. (f) `vh.spawn` uses `ResourceLibrary.Get<PrefabFile>` (string→PrefabFile fix).

- [ ] **End-to-end mechanic loop test**
  - **How:** Run through: spawn → damage → walk to terminal → buy part → repair tool → see health restore → re-drive → feels stronger. Use console commands instead of UI if Section 5 isn't done yet.

- [ ] **Refuel loop test** — drive until fuel < 5L, drive to pump, refuel, drive away.

- [ ] **Tire puncture loop test** — drive hard until `OnTirePunctured` fires; car pulls; repair tire; pull is gone. Verify in debug log.

- [ ] **Door / lights / horn test** — toggle each via RPC; verify visible + audible across two clients.

- [ ] **Gear shift feel test** — verify all 5 forward gears engage via RPM crossing; reverse engages from stop with S.

- [ ] **Engine-off test** — `vh.refuel -50` (drain) or wait; engine dies at fuel=0; `vh.refuel`; engine restarts. Repeat with engine damage.

- [ ] **Tune-profile A/B test** — `vh.tune sport`; drive; `vh.tune heavy`; drive; feel difference.

- [ ] **Stress test** — `for ( int i = 0; i < 8; i++ ) vh.spawn sedan ...` ; drive one. Frame rate > 60.

- [ ] **Hotload test** — edit `VehicleBase.Powertrain.cs` while a vehicle exists; save; verify hotload doesn't corrupt state. If it does, mark affected fields `[SkipHotload]`.
  - **Refs:** TR §16 hotloading.

---

## 12. Publishing

- [ ] **Library README polish**
  - Files: `Libraries/Vehicles.Maintenance/README.md`
  - Effort: 30 min
  - **How:** Confirm it covers: install via Library Manager, the `IVehicleHost` contract example, "add a new car" recipe (5 steps), link to GUIDE.md, link to TODO.md.

- [ ] **License + attribution**
  - Files: new `Libraries/Vehicles.Maintenance/LICENSE`
  - Effort: 15 min
  - **How:**
    1. Drop in MIT license text.
    2. Add a `NOTICE` or section in README crediting [matekdev/sbox-arcade-car-physics](https://github.com/matekdev/sbox-arcade-car-physics) (MIT) and the original [SergeyMakeev/ArcadeCarPhysics](https://github.com/SergeyMakeev/ArcadeCarPhysics) (MIT) for the wheel-sim pattern.

- [ ] **Publish library**
  - Effort: 1 hour
  - **How:** In editor → View → Library Manager → select `vehicles_maintenance` → Publish. Wait for upload. Test by creating a fresh game project and pulling the library from Library Manager.
  - **Refs:** TR §3 libraries · Library Manager UI in s&box.

- [ ] **sbox.game page**
  - Effort: 1 hour
  - **How:** Short description, 1 screenshot of `DiagnosticPanel` mid-repair, 1 screenshot of vehicle driving with HUD. Include link to GUIDE.md.

- [ ] **Announce in s&box community** — Facepunch forum thread or Discord post linking GUIDE.md.

---

## 13. Out of scope (explicit "won't do for v1")

These are referenced in [`docs/GUIDE.md` Section 6](docs/GUIDE.md#6-what-we-deliberately-dont-simulate). Keeping them visible here so future-you doesn't forget the rationale:

- [~~] Real clutch state machine
- [~~] Real differential model
- [~~] Slip-ratio / slip-angle tire model
- [~~] `IWheelContactSolver` interface + libwheel backend
- [~~] Host-validated input-command networking
- [~~] Detachable physical parts (CMS-style)
- [~~] Drivetrain inertia / starter physics
- [~~] In-game live tune editor (use `.vtune` presets instead)

---

## Suggested execution order

If you're working solo, this is the dependency-friendly order:

1. **Section 11 — dev console commands FIRST.** 2 hours of work, makes everything else 10× easier to test. Do this even before closing other code TODOs.
2. **Section 1 + 1b (close TODOs + orphan cleanup)** — finish DiagnosticPanel buttons + RepairTool + seat system + `Vehicle.Spawn` helper + wrecked-state + flip recovery + per-wheel damage + `VehicleBase.Sound.cs`. Decide on each orphan: wire it or delete it. After this the library has no dead code.
3. **Section 2 (tune existing assets)** — quick wins; sedan should feel right, six `.vtune` presets ready.
4. **Section 6 (world content)** — garage scene + dealer + parts NPC. Otherwise nothing to do in playtest.
5. **Section 7 (host gameplay glue)** — real wallet, persistence within session.
6. **Section 11 (testing)** — end-to-end test, fix bugs.
7. **Section 3 + 4 (models + VFX/audio)** — polish for shipping; can ship without these but feels rough. `VehicleDoor` component lands here.
8. **Section 5 (UI polish)** — HUD; can be incremental. Cockpit camera option lands here.
9. **Section 9 (persistence)** — save/load across sessions.
10. **Section 10 (multiplayer test)** — verify networking.
11. **Section 8 (DarkRP adapters)** — the actual integration with the target gamemodes. Could be parallel work.
12. **Section 12 (publish)** — ship it.

**Realistic timeline to v1:**
- Aggressive solo dev: 3 weeks
- Comfortable: 5–6 weeks
- With both DarkRP adapters polished: add 2 more weeks
