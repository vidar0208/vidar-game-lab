# Vidar Game Lab

Real-time rendering and game tech techniques rebuilt in Unity, one folder per technique.
Each one starts from an open-source implementation, is rebuilt for URP, and is shown in a short comparison video.

Videos and write-ups: [x.com/vidar_0208](https://x.com/vidar_0208) and [vidar.games](https://vidar.games)

## Techniques

| Technique | What it does | IA |
|---|---|---|
| [Toon shader](Assets/Techniques/toon-shader) | Flat light bands, back rim, lit rim and stepped specular in place of PBR shading. | yes |
| [Dynamic footprints](Assets/Techniques/dynamic-footprints) | Weight and landing impulse pressed into a heightmap, with snow, mud and sand. | yes |
| [Cross-section](Assets/Techniques/cross-section) | A plane cuts a mesh open and stencil caps make the cut faces look solid. | |
| [Scan pulse](Assets/Techniques/post-processing-scan) | World position rebuilt from depth turns into rings that sweep the scene. | yes |
| [Impossible geometry](Assets/Techniques/impossible-geometry) | Three rooms in one space, picked by a stencil ID written by each doorway. | |
| [Vertical dissolve](Assets/Techniques/vertical-dissolve) | A noisy height line cuts a character away with a glowing edge. | yes |
| [Ice refraction](Assets/Techniques/ice-refraction) | The scene behind the ice is shifted by a cracked-ice normal. | |
| [Digital rain](Assets/Techniques/matrix-rain) | Falling glyphs projected onto every surface, then the surface drops away. | yes |
| [Six-way smoke lighting](Assets/Techniques/smoke-lighting) | Six baked lightmaps per smoke sprite, mixed by the light direction. | |
| [Ropes and cables](Assets/Techniques/ropes-and-cables) | Two fixed ends and one sprung midpoint shape a cheap rope. | |
| [Parallax mapping](Assets/Techniques/parallax-mapping) | A ray marches through a height map so flat quads show bricks, a hole and a valley. | |

Each folder has a README with the full write-up, the source project and its licence, and a preview frame.

## Requirements

- Unity 6.3 LTS (6000.3.28f1) with the Universal Render Pipeline 17.3. Other 6.x versions may work but are not tested.
- Built and tested on an Apple M4 Mac mini with Metal. No geometry shaders or hardware ray tracing are used.
- `ffmpeg`, only for rendering videos with `tools/capture.sh`.

## Open a technique

1. Clone the repository and open the folder as a project in Unity Hub. The first import takes a few minutes.
2. Open **Vidar Game Lab > Techniques** from the menu bar.
3. Press **Build** next to a technique. Its `Editor/BuildScene.cs` creates the scene, materials and `Main.unity` from code.
4. Press **Open**, then Play. Each scene runs its own timeline with labels and camera moves.

## Render a video

```
tools/capture.sh smoke-lighting GameTech.Posts.SmokeScene.Build
```

This builds the scene, renders every frame in batch mode at a fixed time step and encodes `Recordings/<technique>/video.mp4`.
The builder name for each technique is in its README.

## The IA character

Scenes marked IA in the table use the IA character model. The model is not included: its licence (VN3) does not allow redistributing the model files.
To run those scenes, get the model from its official distribution and put `ia.fbx` and its textures in `Assets/Shared/IA/`.
The scripts there (`IARig.cs`, `Editor/IAKit.cs`) load it, apply the material and pose it.

IA. モデル：葉葉波、キャラクターデザイン：えびごはん (©はっぱろぐ！・えびごはん・EAUploader). Fan-made reproduction, not official. #いあ3D

## Layout

- `Assets/Techniques/<technique>/`: the scene builder, runtime scripts and shaders of one technique.
- `Assets/_Pipeline/`: shared helpers for every technique: frame-by-frame capture (`FrameCapturer`, `BatchCapture`), on-screen labels (`LabelSpec`), procedural meshes (`MeshKit`), scene setup (`SceneKit`) and the technique window.
- `Assets/Shared/IA/`: helper scripts for the IA character (model not included).
- `tools/capture.sh`: batch render and encode.

## Licence

Code written for this repository is under the MIT licence (`LICENSE`).
Code based on other projects keeps its original licence. Each source, its licence and its copyright notice are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
