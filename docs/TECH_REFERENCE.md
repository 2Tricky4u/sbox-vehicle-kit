# s&box Technical Reference (distilled from official docs + API)

A self-contained working reference for building this addon. Each section quotes/summarises the official source and links back to it. Use this when you need a quick "is this even possible / what's the syntax" check while coding.

> **Provenance.** All page content was extracted from the [`Facepunch/sbox-docs`](https://github.com/Facepunch/sbox-docs) repository (markdown source — same content rendered at <https://sbox.game/dev/doc>). The doc site is a JS-rendered SPA so this reference often quotes the raw markdown.
>
> **Doc site root:** <https://sbox.game/dev/doc/> · **API browser:** <https://sbox.game/api>

---

## Table of contents

1. [Documentation site map (every doc page)](#1-documentation-site-map)
2. [Project types](#2-project-types)
3. [Libraries — the cross-gamemode mechanism](#3-libraries--the-cross-gamemode-mechanism)
4. [Scene / GameObject / Component model](#4-scene--gameobject--component-model)
5. [Code cheat sheet (quick syntax)](#5-code-cheat-sheet)
6. [Property & editor attributes](#6-property--editor-attributes)
7. [Networking — sync, RPCs, lobbies, ownership](#7-networking)
8. [Physics](#8-physics)
9. [Custom assets / GameResource](#9-custom-assets--gameresource)
10. [GameResource extensions](#10-gameresource-extensions)
11. [Asset naming conventions](#11-asset-naming-conventions)
12. [File system](#12-file-system)
13. [UI (Razor + panels)](#13-ui)
14. [Input](#14-input)
15. [Services (achievements, leaderboards, stats)](#15-services)
16. [Hotloading](#16-hotloading)
17. [API whitelist](#17-api-whitelist)
18. [Game mounts (mostly NOT what we want — clarification)](#18-game-mounts)
19. [API browser navigation patterns](#19-api-browser)
20. [Engine constraints summary (gotchas)](#20-engine-constraints-summary)

---

## 1. Documentation site map

Every doc page exists as a markdown file under `docs/` in [`Facepunch/sbox-docs`](https://github.com/Facepunch/sbox-docs/tree/master/docs). URL slug = path (e.g. `docs/assets/naming-conventions.md` → `https://sbox.game/dev/doc/assets/naming-conventions`).

Top-level folders:

| Folder | What's in it |
|---|---|
| `actiongraph/` | Visual scripting (component-actions, custom nodes, examples, variables) |
| `animation/` | Animation overview |
| `assets/` | Asset workflows: file-system, naming, ugc storage, clothing/, ready-to-use-assets/, **resources/** (binary-serialization, cloud-assets, **custom-assets**, **gameresource-extensions**) |
| `code/` | Programming reference: **libraries**, advanced-topics/ (code-generation, **hotloading**, unit-tests), code-basics/ (**api-whitelist**, **cheat-sheet**, console-variables, is-valid, math-types, time) |
| `editor/` | Editor extension: asset-previews, custom-editors, editor-apps, editor-events, editor-shortcuts, editor-widgets, model-editor, **property-attributes**, texture-generators, undo-system, editor-tools/, mapping/ |
| `exporting/` | Standalone-code export |
| `game-mounts/` | Mount external games' assets (NOT cross-addon dependencies) |
| `gameplay/` | clutter, vr, **input/** (controller-input, glyphs, raw-input), navigation/ (navmesh-agent, navmesh-links, areas), terrain/ |
| `getting-started/` | explore-engine, faq, first-project, installation, monetization, reporting-errors, status, system-requirements, **project-types/** (addon-project, game-project) |
| `media/` | audio, video |
| `movie-maker/` | editor-map, exporting-video, timeline |
| `networking/` | index, lobbies |
| `physics/` | index, physics-shapes |
| `platforms/` | commands, input-glyphs |
| `rendering/` | index |
| `scene/` | index (scene/GameObject/Component overview) |
| `services/` | index (achievements, leaderboards, stats, auth tokens) |
| `sound/` | index |
| `ui/` | index, **ui-basics**, ui-events, ui-styling |
| `worldeditor/` | index |

Bold = pages directly relevant to this project; covered below.

---

## 2. Project types

Source: <https://sbox.game/dev/doc/getting-started/project-types> · <https://sbox.game/dev/doc/getting-started/project-types/addon-project> · <https://sbox.game/dev/doc/getting-started/project-types/game-project>

Three types exist:

| Type | `.sbproj` `Type` | C# code? | How shipped |
|---|---|---|---|
| Game | `"game"` | Yes | Standalone game on sbox.game |
| Addon | `"addon"` | **No** ("yet"; assets + actiongraph only) | Targets a specific host game; assets published individually |
| Library | `"library"` | Yes | Lives in another project's `Libraries/` folder |

**Direct quotes from the addon-project page:**

> *"create assets and publish those individually."*
> *"you can't make addon projects that contain code yet"* (actiongraph supported)
> Addon projects must select "the target game" in project settings — establishing a one-to-one host-game relationship.

**Implication:** A code-bearing reusable system must be a **Library** or a **Game**. Asset-only extensions can be Addons.

---

## 3. Libraries — the cross-gamemode mechanism

Source: <https://sbox.game/dev/doc/code/libraries>

**What they are.** Self-contained, reusable code+asset packages stored in `<project>/Libraries/<lib-name>/`. Each has its own `Assets/`, `Code/`, `Editor/` subfolders.

**Reference direction (one-way):**

> "The game code and editor code can access library classes directly. However, [...] a library cannot access the game code directly."

**Hard constraint:**

> **"Libraries cannot reference other libraries."**

That's the rule that forces the contracts/adapter pattern in this addon.

**Distribution.** Source-distributed — consumers download the folder (manually or via the in-editor **Library Manager** under the View menu) and commit it to their project's version control. Updates, removal, and publishing also via the Library Manager.

**Visibility.** "Libraries publish using standard project visibility rules, remaining private to your organization unless explicitly made public."

---

## 4. Scene / GameObject / Component model

Source: <https://sbox.game/dev/doc/scene>

Vocabulary:

| Concept | What it is |
|---|---|
| **Scene** | "Your game world. Everything that renders and updates at one time." JSON file (`.scene`); switchable at runtime. |
| **GameObject** | World object: position, rotation, scale; hierarchical (children inherit parent transform). |
| **Component** | "Modular functionality" attached to a GameObject. You write game logic by writing Components. |
| **GameObjectSystem** | Operates across all GameObjects in a scene; useful for managers. |
| **Prefab** | "Reusable GameObject templates that can be instantiated at runtime or placed in scenes." Supports overrides + nesting. |

### Component lifecycle methods (from cheat-sheet + Component class)

The doc page itself doesn't enumerate them in one place; the engine class exposes:

| Method | When |
|---|---|
| `OnAwake` | Once, before OnStart, after construction |
| `OnStart` | Once, before first OnUpdate |
| `OnEnabled` | When component (or its GameObject) becomes enabled |
| `OnDisabled` | When disabled |
| `OnUpdate` | Every frame, render-rate |
| `OnFixedUpdate` | Every fixed physics step |
| `OnDestroy` | Once, on destroy |

Plus:

- `Component.ITriggerListener` — implement to receive trigger callbacks
- `[RequireComponent]` — auto-add a sibling component if missing

### Common GameObject API (from [cheat sheet](https://sbox.game/dev/doc/code/code-basics/cheat-sheet))

```csharp
// Find
Scene.Directory.FindByName( "Cube" ).First();
Scene.Directory.FindByGuid( guid );

// Lifecycle
var go = new GameObject();
go.Destroy();
go.Enabled = false;
var clone = go.Clone();
go.Tags.Add( "player" );
foreach ( var child in go.Children ) { ... }
if ( go.IsValid() ) { ... }

// Components
var c = go.AddComponent<ModelRenderer>();
var c2 = go.GetComponent<ModelRenderer>();
var c3 = go.GetOrAddComponent<ModelRenderer>();
foreach ( var c in go.Components.GetAll() ) { ... }
foreach ( var cam in Scene.GetAll<CameraComponent>() ) { ... }

// Transforms
go.WorldPosition = new Vector3( 10, 0, 0 );
var pos = go.LocalPosition;
```

---

## 5. Code cheat sheet

Source: <https://sbox.game/dev/doc/code/code-basics/cheat-sheet>

```csharp
// Logging / debug
Log.Info( $"Hello {username}" );
DebugOverlay.ScreenText( new Vector2( 50, 50 ), "Hello" );
Assert.NotNull( obj, "Object was null!" );
```

Also covered (links): `Time.Delta` (frame delta), `Time.Now`, `Math.*`, `Vector3`, `Rotation`. Math helpers in `docs/code/code-basics/math-types.md`.

---

## 6. Property & editor attributes

Source: <https://sbox.game/dev/doc/editor/property-attributes>

**General**
`[Hide]` `[RequireComponent]` `[Group]` `[ToggleGroup]` `[Title]` `[Feature]` `[FeatureEnabled]` `[Order]` `[ShowIf]` `[HideIf]` `[Range]` `[Step]` `[Space]` `[Header]` `[ReadOnly]` `[Flags]` `[InlineEditor]` `[WideMode]` `[Validate]` `[Advanced]` `[KeyProperty]`

**String-specific**
`[Placeholder]` `[TextArea]` `[FilePath]` `[ImageAssetPath]` `[MapAssetPath]` `[FontName]` `[InputAction]`

**Curve**
`[TimeRange]` `[ValueRange]`

**ActionGraph**
`[SingleAction]`

**Networking** *(not on this page but used everywhere — see §7)*
`[Sync]` `[Sync(SyncFlags.Interpolate)]` `[Change]` `[Rpc.Owner]` `[Rpc.Broadcast]` `[Rpc.Host]`

**Resource link**
`[ResourceType( "vmdl" )]` — restrict a string property to a specific asset type in the inspector.

**Examples**

```csharp
[Property, Range( 0, 100 ), Group( "Maintenance" )]
public float Fuel { get; set; }

[Property, ResourceType( "vmdl" )]
public string ModelPath { get; set; }

[Sync, Change( nameof(OnFuelChanged) )]
public float NetworkedFuel { get; set; }

void OnFuelChanged( float oldValue, float newValue ) { /* ... */ }
```

---

## 7. Networking

Source: <https://sbox.game/dev/doc/networking> · <https://sbox.game/dev/doc/networking/lobbies>

**Design philosophy (quoted):**

> *"Our initial aim isn't to provide a bullet proof server-authoritative networking system. Our aim is to provide a system that is really easy to use and understand."*

**Lobbies API**

```csharp
Networking.CreateLobby( new LobbyConfig {
    MaxPlayers = 4, Privacy = LobbyPrivacy.Public, Name = "My DarkRP"
} );

var lobbies = await Networking.QueryLobbies();
Networking.Connect( lobbyId );
```

**GameObject networking**
- Set the GameObject's **Network Mode** in the editor (or via API).
- Spawn networked objects then call `obj.NetworkSpawn()` to register.
- Ownership: `obj.Network.AssignOwnership( connection )`. Owner runs authoritative simulation.

**RPCs (attribute-based)**

```csharp
[Rpc.Broadcast]   // run on all peers (host + clients)
public void DoEffect() { ... }

[Rpc.Owner]       // run only on the object's owner
public void HostOnlyTransition() { ... }

[Rpc.Host]        // run only on the host
public void GiveMoney( int amount ) { ... }
```

**Sync properties** — `[Sync] public float Fuel { get; set; }` is replicated to all peers; the *owner* writes, others read. `SyncFlags.Interpolate` smooths visual values. `[Change( nameof(Handler) )]` fires a callback when the synced value changes.

**Collections** — use `NetList<T>` and `NetDictionary<TK,TV>` for synced collections (engine-provided).

---

## 8. Physics

Source: <https://sbox.game/dev/doc/physics> · <https://sbox.game/dev/doc/physics/physics-shapes>

> "S&box provides a full 3D and 2D physics system powered by the Source 2 engine."

**Components available**: `Rigidbody`, `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`, joints, triggers.

**Tracing (raycasts / shape casts)**

```csharp
var tr = Scene.Trace
    .Ray( origin, origin + direction * distance )
    .Run();

if ( tr.Hit ) Log.Info( $"Hit {tr.GameObject.Name} at {tr.HitPosition}" );
```

Also `Scene.Trace.Sphere( radius, ... )`, `Scene.Trace.Box( extents, ... )`. Filter with `.WithTag( "vehicle" )`, `.IgnoreGameObject( go )`, `.WithAnyTags( ... )`, `.WithoutTags( ... )`.

**Physics events**: `PrePhysicsStep`, `PostPhysicsStep` callbacks.

**For complex shapes** (vehicles, machines): "combine multiple simple hulls" rather than relying on a single mesh collider — better physics performance.

---

## 9. Custom assets / GameResource

Source: <https://sbox.game/dev/doc/assets/resources/custom-assets>

User-defined data assets that get an inspector window, hot-reload, and asset-browser entry.

```csharp
[GameResource( "Vehicle Config", "vcfg",
    "Declarative vehicle definition.",
    Icon = "directions_car",
    Category = "Vehicles" )]
public sealed class VehicleConfig : GameResource
{
    [Property] public string DisplayName { get; set; } = "Sedan";
    [Property] public float MaxSpeedKmh { get; set; } = 140;

    protected override void PostLoad() { /* register into a static list, etc. */ }
}
```

**Rules (verbatim):**
- *"You should ensure that your **filetype is all lowercase** and **less than or equal to 8 characters**."*
- Stored as JSON files with the custom extension.
- Standard property attributes work (`[Property]`, `[Range]`, etc.).

**Lookup**

```csharp
var cfg  = ResourceLibrary.Get<VehicleConfig>( "vehicles/sedan.vcfg" );
var ok   = ResourceLibrary.TryGet<VehicleConfig>( path, out var cfg2 );
var all  = ResourceLibrary.GetAll<VehicleConfig>();
```

`PostLoad()` is called per asset on load — useful for self-registration into static lookup tables.

---

## 10. GameResource extensions

Source: <https://sbox.game/dev/doc/assets/resources/gameresource-extensions>

> "Append additional data to existing GameResources without modifying the original class or assets."

Inherit `ResourceExtension<TResource, TSelf>`. Use the asset's "Extends" tab to bind an extension to specific assets, or mark "Default" as a fallback.

Lookup methods:
- `FindForResourceOrDefault()`
- `FindForResource()` (null if missing)
- `FindAllForResource()`
- `FindDefault()`

**Use case for this project.** A consuming gamemode can write their own `VehicleEconomyExtension : ResourceExtension<VehicleConfig, VehicleEconomyExtension>` that adds `int RentalPricePerDay`, `string ShopCategory`, etc., without modifying the library's `VehicleConfig`. Bind it to the vehicle assets they care about.

---

## 11. Asset naming conventions

Source: <https://sbox.game/dev/doc/assets/naming-conventions>

Texture suffixes the editor recognises for auto-material assignment:

| Suffix | Channel |
|---|---|
| `_color` | Base Color |
| `_normal` | Normal map |
| `_rough` | Roughness |
| `_metal` | Metallic |
| `_ao` | Ambient Occlusion |
| `_trans` | Opacity |
| `_selfillum` | Emissive |
| `_mask` | Tint mask |
| `_blend` | Blend mask |
| `_height` | Height |

Example set: `sand_color.png`, `sand_normal.png`, `sand_rough.png`. Right-click any one in Asset Browser → **Create Material** → it pulls the matching siblings.

> Conventions are shader-dependent — custom shaders may use different patterns.

---

## 12. File system

Source: <https://sbox.game/dev/doc/assets/file-system>

The standard `System.IO.*` is **blocked by the API whitelist**. Use these virtual filesystems instead:

| FileSystem | Path | Purpose |
|---|---|---|
| `FileSystem.Data` | `<sbox>/data/org/game/` | Per-game read/write |
| `FileSystem.Mounted` | (aggregate) | Read core game + current game + dependencies |
| `FileSystem.OrganizationData` | `<sbox>/data/org/` | Cross-game-within-org storage |

```csharp
FileSystem.Data.WriteAllText( "player.txt", "Hello" );
var s = FileSystem.Data.ReadAllText( "player.txt" );

FileSystem.Data.WriteJson( "vehicles/save.json", saveObj );
var loaded = FileSystem.Data.ReadJson<SaveData>( "vehicles/save.json" );
```

**Serialisation rule (gotcha):** "Only `[Property]`s of your class [are serialised] unless directed not to." Plain fields are skipped — always use auto-properties for persisted state.

---

## 13. UI

Sources: <https://sbox.game/dev/doc/ui> · <https://sbox.game/dev/doc/ui/ui-basics> · <https://sbox.game/dev/doc/ui/ui-styling> · <https://sbox.game/dev/doc/ui/ui-events>

**Core concept.** Panels are C# classes with parent/child relationships. Two flavours:
1. Pure C# (inherit `Panel`, set children via property assignment).
2. **Razor** files (`.razor`) — HTML/CSS-like syntax with embedded C#. Files compile to panel classes.

**Root setup.** A `PanelComponent` is the UI root. Attach to a GameObject that has a `ScreenPanel` (HUD) or `WorldPanel` (in-world). Override `OnTreeFirstBuilt()` to wire up children once the tree exists.

**Razor example**

```razor
@using Sandbox;
@using Sandbox.UI;

<root class="diagnostic">
    <label class="title">@Vehicle.Config.DisplayName</label>
    <div class="row">
        <label>Engine</label>
        <progress max="100" value=@Vehicle.EngineHealth />
        <button onclick=@(() => Repair( PartKind.Engine ))>Repair</button>
    </div>
</root>

@code {
    public VehicleBase Vehicle { get; set; }
    void Repair( PartKind p ) { /* ... */ }
}
```

**Scaling.** "ScreenPanels will rescale all UI based on a 1080p target height automatically." Configurable.

**Styling.** SCSS files (`.scss`) co-located with `.razor` files; selectors can target Razor classes directly.

---

## 14. Input

Source: <https://sbox.game/dev/doc/gameplay/input>

```csharp
if ( Input.Pressed( "attack1" ) ) FirePrimary();
if ( Input.Down( "forward" ) )    Accelerate();
if ( Input.Released( "use" ) )    EndUse();

var look = Input.AnalogLook;     // pitch/yaw deltas
var move = Input.AnalogMove;     // WASD/joystick vector
```

Action names ("attack1", "forward", "use", ...) are configured per-project in **Project Settings → Input**.

**Custom actions.** Add an action in Project Settings, then bind it in the inspector with `[InputAction]` on a string property.

**Pause menu.** ESC opens it by default. Override with:

```csharp
Input.EscapePressed = false;   // suppress default
// then handle ESC yourself
```

Sub-pages: `controller-input`, `glyphs`, `raw-input`.

---

## 15. Services

Source: <https://sbox.game/dev/doc/services>

Built-in backend services (NOT a generic "save game" service):
- **Achievements** — track and award.
- **Leaderboards** — ranked, web API access.
- **Stats** — player statistics tracking.
- **Auth Tokens** — for backend service calls.

There's **no built-in "save player profile" service** — use `FileSystem.Data` / `FileSystem.OrganizationData` (§12), or a backend you host.

---

## 16. Hotloading

Source: <https://sbox.game/dev/doc/code/advanced-topics/hotloading>

> Saving a `.cs` or `.razor` file recompiles changed assemblies; the system "explore[s] the heap to find and upgrade any instances of those types."

**Profiling:** `hotload_log 2` in console.

**Edge cases (gotchas):**
- Removed types → references become `null`.
- Default field values won't update unless using properties or `[SkipHotload]`.
- Dictionary/HashSet hotloads can corrupt state if `Equals/GetHashCode` semantics changed.
- Generic static fields can't be hotloaded.
- Delegates may break if lambdas reorder.
- Reflection caches need manual flushing post-hotload.

`[SkipHotload]` on a field tells the hotload system to leave it alone — use sparingly.

---

## 17. API whitelist

Source: <https://sbox.game/dev/doc/code/code-basics/api-whitelist>

> "Any code failing whitelist checks won't load; in the editor the compiler emits `SB1000 Whitelist Error`."

- **Editor code and libraries bypass the restrictions.**
- Standalone games can opt out but cannot publish to the platform with whitelisting disabled.
- Sandboxed replacements:
  - `Log.Info` instead of `Console.WriteLine`
  - `FileSystem.*` instead of `System.IO.*`
  - (more — request additions on the issue tracker)

For us: gameplay code must respect the whitelist. *Library* code has more freedom but the consuming game still has to compile the result, so write to the whitelist anyway.

---

## 18. Game mounts

Source: <https://sbox.game/dev/doc/game-mounts> · <https://sbox.game/dev/doc/game-mounts/creating-mounts>

> Mounts let users "play with assets from other games they have installed inside s&box."

- Detects external installs via Steam.
- Converts assets (Model/Texture/Material/Sound/Scene/PrefabFile) at runtime.
- Accessed via `mount://` paths.
- Implemented by extending `BaseGameMount` (`Initialize()`, `Mount()`, `Ident`, `Title`) + `ResourceLoader` subclasses.

**Important clarification for this project:** Game Mounts are for *external games' assets* (Garry's Mod, CS:GO etc.), not for cross-addon dependencies inside s&box. For cross-DarkRP code reuse use a **Library** (§3), not a mount.

---

## 19. API browser

Root: <https://sbox.game/api>

JS-rendered SPA — direct WebFetch returns a stub. Pattern observed in URLs and from cross-references:

- Class pages: `/api/Sandbox.Component`, `/api/Sandbox.GameObject`, `/api/Sandbox.Rigidbody`, `/api/Sandbox.Networking`
- Property/method anchors: appended fragment, e.g. `/api/Sandbox.GameObject#WorldPosition`
- Namespace listings: `/api/Sandbox`, `/api/Sandbox.UI`

**Tip while developing.** When the docs say "see `Networking.CreateLobby`" or similar, the API browser at `/api/Sandbox.Networking` is the canonical signature reference.

Most-likely-relevant API namespaces for this project (verify in browser):
- `Sandbox` — `GameObject`, `Component`, `Scene`, `Time`, `Log`, `Input`, `Networking`, `Connection`
- `Sandbox.Physics` — `Rigidbody`, colliders, traces
- `Sandbox.UI` — `Panel`, `PanelComponent`, `ScreenPanel`, `WorldPanel`
- `Sandbox.Network` — sync/RPC plumbing
- `Sandbox.Diagnostics` — `Assert`, `DebugOverlay`

---

## 20. Engine constraints summary (gotchas)

Pulled from across the docs — consult before designing each module.

| Gotcha | Where docs say it | Impact on this project |
|---|---|---|
| Addon project type cannot host C# | Project Types / Addon | Use **Library** (or Game) for code; current `firstaddon` Type=`addon` is wrong for this purpose |
| Libraries cannot reference libraries | Code → Libraries | Can't depend on `sbox-libwheel` etc. as libs — self-contain the wheel sim |
| Libraries cannot access game code directly | Code → Libraries | Forces the contracts/adapter pattern (`IVehicleHost`) |
| Custom asset extensions must be lowercase ≤8 chars | Custom Assets | Pick `.vcfg`, `.partdef`, etc. up front — hard to rename later |
| Only `[Property]`s are serialised in `WriteJson` | File System | Use auto-properties for all persisted state |
| `System.IO.*` is whitelist-blocked | API Whitelist + File System | Use `FileSystem.Data` / `.OrganizationData` |
| Default field values don't reset on hotload | Hotloading | Use property initialisers or `[SkipHotload]` |
| ESC pause menu fires by default | Input | Set `Input.EscapePressed = false` if you need ESC for your own UI |
| Texture suffixes drive auto-material binding | Naming Conventions | Use `_color/_normal/_rough` etc. or auto-binding silently breaks |
| Networking is not server-authoritative by default | Networking | Owner-authoritative — set ownership consciously and don't trust client RPCs |
| Game Mounts ≠ addon dependencies | Game Mounts | Don't try to use mounts to ship the library |

---

## 21. Helpful third-party references

Cited because they fill gaps where official docs are sparse on vehicles:

- [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) — engine-side C# you're allowed to reference patterns from.
- [Facepunch/sbox-libwheel](https://github.com/Facepunch/sbox-libwheel) — official wheel collider lib (do **not** reference as a library; OK to study).
- [matekdev/sbox-arcade-car-physics](https://github.com/matekdev/sbox-arcade-car-physics) — MIT, 5 small files, copy the raycast-wheel pattern (and credit).
- [CAVC](https://sbox.game/clearly/cavc/) — alternative vehicle controller worth looking at for ideas.
- [sousou63/DarkRP](https://github.com/sousou63/DarkRP) — primary adapter target. Folders: `Code/Jobs`, `Code/Economy`, `Code/Items`, `Code/Player`, `Code/Components`, `Code/UI` etc.
- [dxura/dxrp-public](https://github.com/dxura/dxrp-public) — secondary adapter target. Has `sdk/` (separate addon), `Code/Entities/`, `maps/rp_downtown_scuffed`.
- [SubZero S&Box Developer Guide (Steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3595903475) — high-level primer (general — not vehicle/DarkRP-specific).

---

*Last refreshed against `Facepunch/sbox-docs@master` on 2026-05-12.*
