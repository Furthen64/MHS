# MHS Editor PoC

This repository contains a **small proof of concept** for a future Material Handling System editor built with **.NET 10 + Avalonia**.

## What this PoC currently demonstrates

- Avalonia desktop app shell
- Editor-style layout with:
  - File menu
  - left parts palette
  - center viewport
  - right inspector panel
  - bottom status bar
- A temporary software-rendered, continuously spinning 3D cube in the viewport
- Viewport rendering isolated behind a replaceable renderer interface

## What this PoC intentionally does **not** implement yet

- Material handling simulation logic
- Conveyors/hoppers/chutes behavior
- Voxel engine, snapping, picking, routing, save/load, or asset pipeline
- GPU/engine-backed renderer

## Expected SDK

- .NET SDK **10.0**

## Build

From repository root:

```bash
dotnet restore
dotnet build
```

## Run

```bash
dotnet run --project src/Mhs.Editor/Mhs.Editor.csproj
```

## Notes on the current viewport

The viewport uses a lightweight software renderer for the spinning cube (`IViewportRenderer` + `SoftwareCubeViewport`).
This is temporary by design so it can be replaced later by a real renderer without rewriting the editor shell.
