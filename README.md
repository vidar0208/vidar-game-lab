# Vidar Game Lab

Real-time rendering and game tech techniques rebuilt in Unity, one folder per technique.
Each one starts from an open-source implementation, is rebuilt for URP, and is shown in a short comparison video.

Videos and write-ups: [x.com/vidar_0208](https://x.com/vidar_0208) and [vidar.games](https://vidar.games)

## Techniques

| Technique | What it does | Character |
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
| [Texture painting](Assets/Techniques/texture-painting) | Ink stamped at the hit UV into each object's own RenderTexture, with normals and a drip pass. | |
| [Ink painting](Assets/Techniques/ink-painting) | Chinese ink painting look: back-face outlines, triplanar texture strokes, wash and distance fade. | yes |
| [Anisotropic Kuwahara filter](Assets/Techniques/anisotropic-kuwahara) | Painterly post-process: basic, generalized and anisotropic Kuwahara compared, with a structure tensor view. | |
| [ASCII render](Assets/Techniques/ascii-render) | Post-process that turns every 8 x 8 pixel tile into a glyph, with difference-of-Gaussians edge glyphs and an optional colour tint. | |

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

## The character

Scenes marked in the Character column have a character in them. The character in the videos is not included in this repository.
Without a model these scenes build a simple stand-in mannequin from code (`Assets/Shared/Character/Editor/Mannequin.cs`), so they build and render as they are.
To use your own rigged character, put one FBX or prefab in `Assets/Shared/Character/Model/` (git ignores that folder).
The model is switched to a Humanoid rig on first use. The scenes are framed for a character about 1.4 m tall; T-pose and A-pose both work.
`Editor/CharacterKit.cs` loads it, gives every material the technique's shader and puts it under an empty root that the scenes move and turn.
MonoBehaviour scripts you place next to the model (a hair simulation, for example) are added to that root.
`CharacterRig.cs` poses it, and blinks it if the model has a blend shape named `Blink` (0 open, 100 closed).

## Layout

- `Assets/Techniques/<technique>/`: the scene builder, runtime scripts and shaders of one technique.
- `Assets/_Pipeline/`: shared helpers for every technique: frame-by-frame capture (`FrameCapturer`, `BatchCapture`), on-screen labels (`LabelSpec`), procedural meshes (`MeshKit`), scene setup (`SceneKit`) and the technique window.
- `Assets/Shared/Character/`: spawns the scene character (your model or the stand-in mannequin) and poses it.
- `tools/capture.sh`: batch render and encode.

## Licence

Code written for this repository is under the MIT licence (`LICENSE`).
Code based on other projects keeps its original licence. Each source, its licence and its copyright notice are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
