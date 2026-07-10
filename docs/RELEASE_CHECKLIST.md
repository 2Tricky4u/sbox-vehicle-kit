# Release checklist — the human-in-the-loop steps

Everything code-side is done and scripted-verified (see git log, Phases 0–6).
These are the steps that need a person at the keyboard.

## 1. Move the repo out of OneDrive (10 min, do first)

OneDrive sync contends with s&box hotload writes (`obj/`, `.sbox/`, generated
`.csproj`) — we hit one editor stall mid-session already.

1. Close the s&box editor (and this Claude Code session).
2. Move the whole folder: `C:\Users\xagao\OneDrive\Documents\s&box projects\carmaintenance` → `C:\dev\carmaintenance`.
3. Reopen `C:\dev\carmaintenance\carmaintenance.sbproj` in s&box (the MCP bridge reconnects by itself).
4. Restart Claude Code from the new folder. Git history moves with the folder — nothing else to do.

## 2. Hands-on playtest (minimal.scene, ~20 min)

Everything below is code-verified; this pass is about *feel* and the flows that need real input.

- [ ] Drive the sedan (WASD). Check 0–100 feel, steering, braking. `vh.tune sport` / `drift` / `heavy` — each should feel distinct.
- [ ] **Audio**: engine loop pitch follows RPM; horn (`vh.horn`); skid on hard cornering. If the engine loop cuts out after ~10 s, open `Assets/sounds/engine_loop.sound` in the sound editor and tick its loop flag (one-time check).
- [ ] Crash into the **testWall** / launch off the **testRamp** (both south of spawn at y≈-400) → damage numbers on the HUD, `WRECKED` at body 0, engine refuses to start, repair body above 25% → drives again.
- [ ] **Seats**: E enters the nearest seat, E exits, camera follows (`vh.cam first` for cockpit). Passenger seat: enter the second seat, have the car... park first. Eject: `vh.kill` while seated → player restored beside the wreck site.
- [ ] **Mechanic loop**: walk over the 5 colored pickup cubes (north of spawn, y 280–520) → `vh.partsui` shows the parts → damage the car → `vh.diag` → repair buttons consume parts and charge/pay via the stub host. Toasts appear.
- [ ] **Fuel**: `vh.fuel 5`, drive to the red pump cube (-1250, 200), press E near it → +5 L, toast with price.
- [ ] `vh.spawn sedan`, `vh.spawn hatchback` → both drivable; hatchback noticeably weaker/lighter.

If wheel anchors look off on the hatchback (wheels floating/sunken), `vh.debugdraw`
shows the rays — adjust the four `wheel *` children in `Assets/prefabs/hatchback.prefab`.

## 3. Two-client multiplayer smoke test (needs a 2nd PC or account)

Host a lobby from the editor; join from the second machine.

- [ ] Joiner sees the host's car move smoothly (no fighting/teleporting/jitter).
- [ ] Joiner's `vh.status` on the host's car shows the same fuel/health (synced state).
- [ ] Joiner refuels/repairs the host's car (`vh.refuel`, `vh.repair engine`) — `[Rpc.Owner]` should route to the host; if nothing happens, that's the one known-risk area to report.
- [ ] Both players spawn their own cars (`vh.spawn sedan`) and drive simultaneously.
- [ ] Crash damage is computed once (watch the host log — one Damage line per crash, not two).
- [ ] Host quits while the joiner watches the host's car: it must survive (orphaned to host... for a host quit the session ends — test instead with a 3-player lobby or joiner-owned car + joiner quits).
- [ ] Rejoin after owning a car → `VehiclePersistence.RestoreOwnershipFor` (call it from a dev command or the bootstrap) hands it back.

## 4. Publish (Library Manager)

1. Right-click **Vehicles.Maintenance** in Library Manager → **Publish Project**.
2. Fill the full profile — sbox.game buries packages without it:
   - Thumbnail: `docs/screenshots/spawned-cars-thumb.png` (or retake a nicer angle).
   - 3–4 screenshots (garage props, HUD while driving, DiagnosticPanel open, a crash).
   - Description: lift the first paragraph of `Libraries/Vehicles.Maintenance/README.md`.
   - Tags: `vehicles`, `darkrp`, `library`, `maintenance`, `mechanic`.
3. Keep visibility private/org until the 2-client test passes, then flip public.

## 5. Post-v1 (tracked in TODO.md)

Dealer + parts-shop NPCs/UI · the two DarkRP adapters (sousou63, dxura) — the real
integration proof · visual damage & flat-tire meshes · per-wheel grip model · custom
car models to replace the cloud placeholders.
