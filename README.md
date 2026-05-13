# Vehicle Kit for s&box

A lightweight, multiplayer-ready vehicle controller and tuning framework for s&box.

Built for arcade-feeling cars, fast iteration, and easy integration into games.

## Features

- Arcade car physics
- Suspension, braking, handbrake, downforce and anti-roll
- Keyboard and gamepad-friendly input smoothing
- Tuning presets: Grip, Drift, Rally, Sport
- Lightweight RPM and gear simulation
- Multiplayer-aware wheel visuals
- Easy prefab/component setup
- MIT licensed

## Why use this?

Vehicle Kit is designed for games that need cars that feel good without becoming a heavy simulation project.

Use it if you want:

- drivable cars quickly
- simple tuning
- multiplayer-friendly behavior
- readable C# code
- a base you can extend for racing, RP, sandbox, or open-world games

## Quick Start

1. Copy the `Code/VehicleKit` folder into your s&box project.
2. Create a car GameObject.
3. Add a Rigidbody and collider.
4. Add the `VehicleController` component.
5. Add four wheel points.
6. Assign a tuning preset.
7. Press Play.

## Example

```csharp
var tune = VehicleTunePreset.Sport;
vehicle.ApplyTune( tune );