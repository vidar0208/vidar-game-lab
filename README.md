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
| [Halftone comic](Assets/Techniques/halftone-comic) | Post-process that prints the frame as CMYK dots on rotated grids, with plate offsets, a pitch and angle sweep and a zoomed brightness ramp. | |
| [2D stable fluids](Assets/Techniques/stable-fluids-2d) | GPU stable fluids on a 256 x 256 grid with compute shaders: advection, implicit diffusion and Jacobi pressure projection around an obstacle. | |
| [Pixel-art dither](Assets/Techniques/pixel-art-dither) | Pixel-art post-process in URP: point-sampled or averaged downsampling, Bayer ordered dither with per-channel levels, and a palette lookup. | |
| [Planar reflections](Assets/Techniques/planar-reflections) | Mirrored render of the scene into a texture with an oblique clip plane, read by a glossy floor at its screen position; single level, URP. | |
| [FFT convolution bloom](Assets/Techniques/fft-convolution-bloom) | Bloom as a frequency-domain convolution: FFT of the bright pixels and of a kernel image, complex multiply, inverse FFT; any kernel shape at a fixed cost, URP. | |
| [Anime speed lines](Assets/Techniques/anime-speed-lines) | Full-screen anime speed lines: scrolling simplex noise in polar coordinates, remapped to thin lines and kept out of a centre mask, URP. | |
| [Per-object shadows](Assets/Techniques/per-object-shadows) | Per-object shadow atlas: each moving object gets its own light-space slice sized by screen coverage, resolved on screen with instanced boxes, next to URP cascades. | |
| [Rim light](Assets/Techniques/rim-light) | Fresnel rim light rewritten for URP: bias + scale x (1 - N dot V)^power as emission, with sine pulsing and world-space 3D simplex noise. | |
| [Screen-space cavity](Assets/Techniques/screen-space-cavity) | Screen-space curvature and cavity from depth and normals, stored as a light multiplier: creases darkened, edges brightened, ported from a URP 10 renderer feature to a command buffer. | |
| [Specular probes](Assets/Techniques/specular-probes) | Baked lights show up as highlights in a baked reflection probe: bright spheres are drawn at each light while the probe bakes, then removed. | |
| [AgX tone mapping](Assets/Techniques/agx-tonemapping) | AgX and ACES 2.0 tone mapping in URP through a renderer feature that rewrites URP's colour grading LUT, compared with URP's built-in ACES on the same HDR frame (SDR output). | |
| [GPU fog particles](Assets/Techniques/gpu-fog-particles) | Textureless fog: camera-facing particle quads whose alpha comes from three noise layers (value, simplex, Voronoi) computed in the shader, with a scene-depth fade, URP. | |
| [Casual PRT](Assets/Techniques/casual-prt) | Probe GI that follows a moving sun: each probe stores 512 surfels once, a compute shader relights them every frame and projects to L2 SH, pixels blend the 8 nearest probes, URP. | |
| [Screen-space path tracing](Assets/Techniques/screen-space-path-tracing) | Multi-bounce path tracing through the depth buffer and G-buffer with real-time temporal or offline accumulation and a reflection probe fallback, URP deferred, one renderer per split-screen camera. | |
| [Dithered transparency](Assets/Techniques/dithered-transparency) | Alpha as a screen-space keep-or-discard test (4x4 matrix or blue-noise texture) in the opaque pass, with depth write and a dithered shadow caster, compared with alpha blending. | |
| [Motion Blur](Assets/Techniques/motion-blur) | Reconstruction-filter motion blur (velocity setup, tile max, neighbour max, depth-aware gather) plus frame blending, ported to a URP renderer feature that reads URP's motion vectors. | |
| [Triplanar Normal Mapping](Assets/Techniques/triplanar-normals) | One normal map projected along X, Y and Z on meshes without UVs, with the naive, UDN, Whiteout, RNM and GPU Gems 3 blends in one URP lit shader and a CPU error test. | |
| [Volumetric Lights](Assets/Techniques/volumetric-lights) | Per-light ray-marched beams for sun, spot and point lights in URP: shadow-mapped scattering in each light's volume, half-res march, bilateral blur and upsample. | |
| [GPU Trails](Assets/Techniques/gpu-trail) | 1,029 ribbon trails kept on the GPU in URP: compute shaders append nodes, build camera-facing strips, cull against the camera and pick a LOD per trail; indirect draws. | |
| [Virtual Texture](Assets/Techniques/virtual-texture) | A 16,384 x 16,384 virtual texture in URP: feedback pass with async readback, a page table and a 64-tile LRU cache; pages drawn procedurally with their numbers. | |
| [CoR Skinning](Assets/Techniques/cor-skinning) | Center of rotation skinning in URP: per-vertex centres precomputed on the GPU in the editor, compute-shader skinning; linear blend vs CoR on a figure with blended arm weights. | |
| [GPU 3D Smoke](Assets/Techniques/gpu-3d-smoke) | Grid smoke solver in compute shaders (128 cubed, Jacobi pressure, vorticity confinement) with a slab and a walking figure as moving obstacles; URP ray-marched volume. | |
| [Capsule Occlusion](Assets/Techniques/capsule-occlusion) | Analytic ambient occlusion from capsules on a character's bones, clustered per froxel with a compute shader; URP port with on/off, self-occlusion and debug views. | |
| [2D Interactive Water](Assets/Techniques/interactive-water-2d) | Side-view interactive water: a 1D wave equation solved in a compute shader, bodies crossing the surface push it, screen-space reflection and refraction; URP port with solver on/off and reflection on/off views. | |
| [Variance Shadow Maps](Assets/Techniques/variance-shadow-maps) | Variance shadow maps: depth and depth squared rendered from the sun, blurred in two passes, Chebyshev bound per pixel, six cascades; URP port with plain depth compare and blur-size comparisons. | |
| [SPH Fluid on the GPU](Assets/Techniques/sph-fluid-gpu) | Particle SPH liquid in a compute shader: uniform-grid neighbour search, density, pressure and viscosity forces, with a forces-off switch and a viscosity comparison. | |
| [FLIP Fluid: PIC vs FLIP](Assets/Techniques/flip-fluid) | Particle-grid liquid in compute shaders: particle to grid, pressure and density projection, grid back to particles with a flipness blend; PIC vs FLIP and coarse vs fine grid side by side. | |
| [Per-Light Baked Shadows, Live Light Changes](Assets/Techniques/per-light-baked-shadows) | Fixed lights whose shadows are raytraced once per light into a visibility bit per texel; colour, pulse and flicker change at runtime. Real-time unshadowed vs precomputed, steady vs live, side by side. | |
| [Strand Hair Simulation: Rigid vs Simulated](Assets/Techniques/strand-hair-sim) | 30,000 GPU-simulated hair strands on a scanned head: length, local shape and root rotation constraints, sphere and capsule collision, Kajiya-Kay style strand shading. Hair locked to the head vs simulated, side by side. | |
| [Subsurface Scattering and Transmission](Assets/Techniques/sss-transmission) | Back-lit wax, jade and a leaf: wrapped diffuse tinted by a subsurface colour plus a view-dependent transmission term scaled by thickness, in a small URP shader. Both off vs both on, wrap only vs wrap + transmission, and a thickness sweep. | |
| [Bitonic Pixel Sort on the GPU](Assets/Techniques/bitonic-pixel-sort) | Pixel sorting in a compute shader: each row (or column) is split into runs by a brightness threshold and every run is sorted with a bitonic sorting network in groupshared memory. | |
| [Weighted Blended Order-Independent Transparency](Assets/Techniques/order-independent-transparency) | Weighted blended OIT in URP: glass is accumulated into a weighted colour target and a revealage target with order-independent blend states, then composited over the opaque scene in one pass, next to the default sorted transparent queue. | |
| [Camera-Space Planar Shadow Map](Assets/Techniques/planar-shadow-map) | Shadows for a flat ground plane drawn in the main camera's screen space: casters are slid along the light onto the ground and projected with the main camera, so nearly every shadow-map texel lands on visible ground. Shown next to URP's cascaded shadows. | |
| [Billboard Cards vs Blade Meshes](Assets/Techniques/grass-drawing-methods) | The same grass field drawn two ways with GPU instancing: 14,000 alpha-tested cards that face the camera plane vs 200,000 seven-triangle blade meshes, same lit shader, same sun and shadows. No geometry shader version (Metal does not support them). | |
| [Improved Box-Projected Reflections](Assets/Techniques/box-projected-reflections) | One reflection probe in a closed room, sampled three ways: plain cubemap, Unity's default box projection, and box projection with distance-based contact hardening (sharper reflections where the reflected wall is close). URP shader, split-screen and mip-level view. | |
| [Deep Shadow Maps](Assets/Techniques/deep-shadow-map) | Shadows through fibres: per-texel fragment lists (depth + opacity, up to 16 per texel) built by a compute shader, resolved per pixel on the floor and the fibres. Compared with an ordinary one-depth shadow map built from the same data. | |
| [Dual Paraboloid Reflections](Assets/Techniques/dual-paraboloid-reflections) | Two paraboloid maps (front and back hemisphere) packed in one texture instead of a six-face cubemap, compared side by side with a cubemap of the same resolution. | |
| [GeoAO: Vertex Ambient Occlusion](Assets/Techniques/geo-ao) | Per-vertex ambient occlusion computed once at start from 256 orthographic depth views, compared with no AO and with URP's screen-space AO when the occluder is off screen. | |
| [Distance-Field AO and Soft Shadows](Assets/Techniques/dfao) | Two-level distance fields (a local SDF per prop plus a global field rebuilt every frame) give ambient occlusion and sphere-traced soft shadows, compared with a URP shadow map. | |
| [Bent Normal and AO Baking](Assets/Techniques/bent-normal-ao) | AO and bent normals baked into one atlas at start from 1024 depth views; reflections occluded along the bent normal, compared with plain AO multiplication. | |
| [Hydraulic Erosion](Assets/Techniques/hydraulic-erosion) | 131,072 raindrop particles erode a 256 x 256 noise height map on the GPU; compared with the raw noise, brush radius 1 vs 3, and a height change view. | |
| [Ray-Marched Mandelbulb](Assets/Techniques/mandelbulb-raymarch) | Mandelbulb ray-marched in one compute shader with a distance-scaled hit threshold, soft shadows and distance AO; 3 vs 15 iterations on a 100x zoom, power morph, source vs added shading. | |
| [Physarum Transport Network](Assets/Techniques/physarum-network) | 8.4 million slime mold agents on the GPU with three trail sensors each; sensing off vs on, sensor angle 22.5 vs 60 degrees, 1 vs 3 species. | |
| [Compute Path Tracer](Assets/Techniques/compute-path-tracer) | GPU path tracer in one compute shader: BVH, glass, mirrors, sample averaging, compared with URP raster | |
| [Wave Function Collapse Town](Assets/Techniques/wave-function-collapse) | Simple tiled WFC solver (C# port) building a 3D town from 13 procedural tiles, with random-tile and options-left views | |
| [Reaction-Diffusion](Assets/Techniques/reaction-diffusion) | Gray-Scott reaction-diffusion in a compute shader with lit two-material display, diffusion-only comparison and three feed/kill pairs | |
| [N-Body Galaxy Collision](Assets/Techniques/nbody-galaxy) | Tiled all-pairs N-body gravity in a compute shader: two galaxies colliding, centre-only vs full N-body, two softening values | |
| [RVO Crowd Avoidance](Assets/Techniques/rvo-crowd-avoidance) | 250 walkers crossing a circle with the RVO2 (ORCA) library: seek + push-apart baseline vs RVO2, short vs long time horizon, overlap and arrival counters | |
| [Procedural Planet](Assets/Techniques/procedural-planet) | Moon heights from ridge noise and crater curves in a compute shader on a welded cube sphere: noise only vs noise + 500 craters, two crater presets, low sun on the terminator | |
| [Runtime Mesh Fracture](Assets/Techniques/mesh-fracture) | Runtime fracture with the OpenFracture library: recursive random plane cuts with filled cut faces, pre-fractured vs fractured at impact, 8 vs 64 pieces with timing | |
| [Mesh deformers as Burst jobs](Assets/Techniques/mesh-deformers) | Twist, bend, ripple and melt chained as Burst jobs on a 247,682-vertex mesh; normals on/off; Burst vs plain C# timing | |
| [Particle life on the GPU](Assets/Techniques/particle-life) | Particle life ported to a compute shader: 8,192 particles, all pairs, symmetric vs asymmetric rule tables | |
| [MLS-MPM: elastic vs fluid](Assets/Techniques/mpm-elastic-fluid) | 2D MLS-MPM on the CPU with Burst: the same particle-grid loop runs a Neo-Hookean elastic solid or a Newtonian fluid, only the stress function changes. | |
| [Particles from a skinned mesh](Assets/Techniques/skinned-mesh-particles) | Smrvfx's skinned mesh baker writes position, normal and velocity textures every frame; a compute particle system spawns from them with inherited surface velocity. | |

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
