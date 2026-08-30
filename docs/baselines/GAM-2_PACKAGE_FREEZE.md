# GAM-2 package freeze

Captured 2026-08-29 from the checked-in package manifests. No package was added,
removed, upgraded, or downgraded for GAM-2.

Source files:

- `Packages/manifest.json`
- `Packages/packages-lock.json`

SHA-256:

- `manifest.json`: `96BB79688274E1B36353536B864CC089F2562BA56B99F50797D922022A857848`
- `packages-lock.json`: `CBF46DE16C5A5D81C1A7FE1C56DE064EB3EE5CE52B0C957A7A910748D8D95380`

## Direct package classification

`REQUIRED_NOW` is limited to packages used by the GAM-2 foundation. `REQUIRED_LATER`
and `REMOVE_CANDIDATE` are recorded decisions only; this freeze does not change
the existing project baseline.

| Package | Version | Classification |
|---|---:|---|
| `com.unity.ai.assistant` | 2.18.0-pre.2 | OPTIONAL |
| `com.unity.ai.inference` | 2.6.1 | OPTIONAL |
| `com.unity.ai.navigation` | 2.0.14 | REQUIRED_LATER |
| `com.unity.collab-proxy` | 2.12.4 | TEMPLATE_DEFAULT |
| `com.unity.ide.rider` | 3.0.40 | TEMPLATE_DEFAULT |
| `com.unity.ide.visualstudio` | 2.0.26 | TEMPLATE_DEFAULT |
| `com.unity.inputsystem` | 1.20.0 | REQUIRED_NOW |
| `com.unity.multiplayer.center` | 1.0.1 | TEMPLATE_DEFAULT |
| `com.unity.pipeline` | 0.5.0-exp.1 | OPTIONAL |
| `com.unity.render-pipelines.universal` | 17.3.0 | TEMPLATE_DEFAULT |
| `com.unity.test-framework` | 1.6.0 | REQUIRED_NOW |
| `com.unity.timeline` | 1.8.12 | REQUIRED_LATER |
| `com.unity.ugui` | 2.0.0 | REQUIRED_LATER |
| `com.unity.visualscripting` | 1.9.12 | OPTIONAL |
| `com.unity.modules.accessibility` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.adaptiveperformance` | 1.0.0 | OPTIONAL |
| `com.unity.modules.ai` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.androidjni` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.animation` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.assetbundle` | 1.0.0 | OPTIONAL |
| `com.unity.modules.audio` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.cloth` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.director` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.imageconversion` | 1.0.0 | OPTIONAL |
| `com.unity.modules.imgui` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.jsonserialize` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.particlesystem` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.physics` | 1.0.0 | REQUIRED_NOW |
| `com.unity.modules.physics2d` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.screencapture` | 1.0.0 | OPTIONAL |
| `com.unity.modules.terrain` | 1.0.0 | OPTIONAL |
| `com.unity.modules.terrainphysics` | 1.0.0 | OPTIONAL |
| `com.unity.modules.tilemap` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.ui` | 1.0.0 | REQUIRED_LATER |
| `com.unity.modules.uielements` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.umbra` | 1.0.0 | TEMPLATE_DEFAULT |
| `com.unity.modules.unityanalytics` | 1.0.0 | OPTIONAL |
| `com.unity.modules.unitywebrequest` | 1.0.0 | OPTIONAL |
| `com.unity.modules.unitywebrequestassetbundle` | 1.0.0 | OPTIONAL |
| `com.unity.modules.unitywebrequestaudio` | 1.0.0 | OPTIONAL |
| `com.unity.modules.unitywebrequesttexture` | 1.0.0 | OPTIONAL |
| `com.unity.modules.unitywebrequestwww` | 1.0.0 | OPTIONAL |
| `com.unity.modules.vectorgraphics` | 1.0.0 | OPTIONAL |
| `com.unity.modules.vehicles` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.video` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.vr` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.wind` | 1.0.0 | REMOVE_CANDIDATE |
| `com.unity.modules.xr` | 1.0.0 | OPTIONAL |

Unity editor baseline: `6000.3.22f1` (`1c726e1fb402`). Cinemachine and Animation
Rigging are absent and are not required by GAM-2.
