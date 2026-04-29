# Client Architecture

## Current module boundaries

- `Shared`: protocol contracts and gameplay simulation that are intentionally shared with the server.
- `Client/Assets/Scripts/Rpc`: transport creation, launch argument parsing, and connection diagnostics.
- `Client/Assets/Scripts/Rpc/Generated`: generated RPC client and callback bindings.
- `Client/Assets/Scripts/Gameplay`: Unity runtime orchestration, view synchronization, presentation, HUD, and local meta progression.

## Why this split matters

Before this change, the client runtime relied on Unity's default `Assembly-CSharp`, so gameplay code, RPC bootstrapping, and any future feature scripts all lived in one compilation unit. That makes dependency drift easy and increases the blast radius of every change.

Introducing `SampleClient.Rpc` and `SampleClient.Gameplay` establishes an explicit direction:

`Shared` -> `ULinkRPC.Generated` -> `SampleClient.Rpc` -> `SampleClient.Gameplay`

`Gameplay` can depend on transport abstractions and generated client bindings, but `Rpc` should never depend back on gameplay code.

## Immediate risks still present

- `DotArenaGame` is still the application coordinator and remains the main long-term refactor target.
- `DotArenaSceneUiPresenter` is large and owns both widget lookup and runtime widget construction.
- `DotArenaMetaProgression` mixes persistence, reward rules, and view-model style summary builders.

## Recommended next refactor steps

1. Extract a `DotArenaMatchFlowController` from `DotArenaGame` for entry, matchmaking, in-match, and settlement transitions.
2. Split `DotArenaMetaProgression` into repository (`load/save`), domain rules (`progression/rewards`), and summary builders for UI.
3. Move scene UI creation code toward prefab-driven or UI Toolkit-driven assets so `DotArenaSceneUiPresenter` stops being a large procedural builder.
