# Vehicles.Maintenance — Assets

Drop vehicle definitions, prefabs, models, and sounds here.

## Adding a new car

1. Drop a `.vmdl` model in `vehicles/models/<name>.vmdl`.
2. Right-click in Asset Browser → **New → Vehicle Config** → save as `vehicles/<name>.vcfg`.
3. Tune the inspector fields (Identity / Performance / Capacity / Maintenance / Audio / Cosmetics / Economy).
4. Make a prefab `vehicles/<name>.prefab`:
   - Root GameObject with a `VehicleBase` component referencing the `.vcfg`.
   - N child GameObjects for wheel anchors → drag into `WheelAnchors` list (count must match `WheelCount` in the config).
   - Seat anchor children → drag into `SeatAnchors` list.
   - The model can be a child `ModelRenderer` or set on the root.
5. Reference from a dealer NPC via `VehicleConfig.Find("<name>")`, or query by tag with `VehicleConfig.WithTag("civilian")`.

No C# changes required. The library reads everything off the `VehicleConfig` at runtime.

## Sounds

Author engine and horn sounds as `.sound` assets here, then reference their paths from the `Audio` group of each `VehicleConfig`.
