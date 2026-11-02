# CraftingPrototype

A first-person cocktail crafting prototype built with Unity 6 and the Universal Render Pipeline.

The bar supports two-handed tool handling, pouring, shaking, stirring, ice transfer and serving. A deterministic C# simulation tracks ingredient composition, temperature, ice melt and carbonation. Three orders provide a short play loop with qualitative feedback and retries.

## Open the project

1. Install Unity **6000.3.0b1** with the required platform modules.
2. Add this directory to Unity Hub and let the package import finish.
3. Open `Assets/Bar/Scenes/BarPresentation.unity` and enter Play Mode.

Interface languages: Chinese, English and Japanese. Press **F10** for settings and **F3** for the developer state inspector. On-screen prompts describe available interactions.

## Project layout

- `Assets/Bar/Core`: deterministic simulation.
- `Assets/Bar/Domain`, `Data`, `Input`, `Logging`: item state, actions, configuration and session records.
- `Assets/Bar/Unity`: Unity interaction and presentation.
- `Assets/Bar/Editor`: import, assembly and asset validation tools.
- `Assets/Bar/Tests`: EditMode and PlayMode tests.
- `ThirdPartyNotices`: font and media licenses and source records.

## Builds

WebGL is a supported build target. Use the presentation scene in Build Profiles; generated player builds are not tracked in this repository. Local WebGL output belongs in `output/`.

The simulation and presentation parameters are authored values and have not been calibrated against physical cocktail measurements. Media and font licenses are retained in `ThirdPartyNotices` and the asset license files.
