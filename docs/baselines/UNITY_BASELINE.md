# Unity Baseline

Captured from `bd46e4da640e7cc4594c05a819ace54bf271900e` on branch `foundation` during GAM-1.

## Project

| Field | Value |
| --- | --- |
| Unity editor | `6000.3.22f1` LTS (`1c726e1fb402`) |
| Render pipeline | Universal Render Pipeline (URP) `17.3.0` |
| Initial scene | `Assets/Scenes/SampleScene.unity` |
| Product/platform scope | Windows desktop per frozen product constitution |
| Initial commit SHA | `bd46e4da640e7cc4594c05a819ace54bf271900e` |
| Root history at entry | 1 commit |
| Repository state at entry | Clean |

## Direct package audit

Versions below are the direct entries in `Packages/manifest.json`; resolved transitive versions remain recorded in `Packages/packages-lock.json`.

| Package | Version | Classification |
| --- | --- | --- |
| `com.unity.render-pipelines.universal` | `17.3.0` | `REQUIRED_NOW` |
| `com.unity.inputsystem` | `1.20.0` | `REQUIRED_NOW` |
| `com.unity.test-framework` | `1.6.0` | `REQUIRED_NOW` |
| `com.unity.ai.assistant` | `2.18.0-pre.2` | `TEMPLATE_DEFAULT` |
| `com.unity.ai.inference` | `2.6.1` | `TEMPLATE_DEFAULT` |
| `com.unity.ai.navigation` | `2.0.14` | `TEMPLATE_DEFAULT` |
| `com.unity.collab-proxy` | `2.12.4` | `TEMPLATE_DEFAULT` |
| `com.unity.ide.rider` | `3.0.40` | `TEMPLATE_DEFAULT` |
| `com.unity.ide.visualstudio` | `2.0.26` | `TEMPLATE_DEFAULT` |
| `com.unity.multiplayer.center` | `1.0.1` | `TEMPLATE_DEFAULT` |
| `com.unity.pipeline` | `0.5.0-exp.1` | `TEMPLATE_DEFAULT` |
| `com.unity.timeline` | `1.8.12` | `TEMPLATE_DEFAULT` |
| `com.unity.ugui` | `2.0.0` | `TEMPLATE_DEFAULT` |
| `com.unity.visualscripting` | `1.9.12` | `TEMPLATE_DEFAULT` |
| `com.unity.cinemachine` | not installed | `REQUIRED_LATER` |
| `com.unity.animation.rigging` | not installed | `REQUIRED_LATER` |
| `com.unity.modules.*` | `1.0.0` | `TEMPLATE_DEFAULT` |

The resolved lock also includes URP dependencies (`com.unity.render-pipelines.core` `17.3.0`, `com.unity.render-pipelines.universal-config` `17.0.3`, `com.unity.shadergraph` `17.3.0`) and Test Framework dependencies. No package upgrades or installations were performed in GAM-1.

## Starter content classification

| Path | Classification | GAM-1 action |
| --- | --- | --- |
| `Assets/Readme.asset` | `STARTER_TUTORIAL_ONLY` | Retained; not referenced by required project configuration. |
| `Assets/TutorialInfo/` | `STARTER_TUTORIAL_ONLY` | Retained; cleanup is optional and outside the constitution install. |
| `Assets/Settings/`, `Assets/Scenes/`, `ProjectSettings/`, `Packages/` | `REQUIRED_PROJECT_CONFIG` | Retained unchanged. |

## Package feature presence

- URP: present, direct `17.3.0`.
- Input System: present, direct `1.20.0`.
- Cinemachine: absent.
- Animation Rigging: absent.
- Test Framework: present, direct `1.6.0`.

## Audit notes

The current `.gitignore` covers `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`, `Build`, and `Builds`. The current `.gitattributes` applies LFS to expected binary-heavy classes including models, audio, video, images, compressed archives, fonts, and binaries. Existing LFS content is limited to the starter URP icon. No generated Unity directory is tracked.
