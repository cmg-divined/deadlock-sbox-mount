# Deadlock mount

Mounts Deadlock (Steam app 1422450) into s&box as `mount://deadlock/...`. Deadlock ships compiled
Source 2 resources (`*_c`) in VPKs, which this reads directly. Nothing is extracted to disk.

The format code under `Formats/` uses no s&box types, so it can be compiled and tested outside the
editor. `DeadlockMount.cs` and `Resource/` turn it into s&box resources. Shaders live in the project's
`Assets/shaders/`.

## Layout

| Path | What |
|------|------|
| `DeadlockMount.cs` | The `BaseGameMount`. Opens `game/citadel` and `game/core` `pak01_dir.vpk`, then every map VPK in `game/citadel/maps`, and registers maps, models, materials and textures. |
| `DeadlockMount.VData.cs` | VData lookups (`scripts/*.vdata_c`, with `_base` inheritance), for entities that take their model from a subclass. |
| `DeadlockMount.Heroes.cs`, `DeadlockHeroControlWidget.cs` | The playable heroes (released, with a model and graph) by their in-game names, for the inspector's hero picker. |
| `Resource/ParticleLoader.cs`, `Resource/KeyValuesLoaders.cs` | Particle systems (`vpcf_c`), animation graphs with every file they use (`vnmgraph_c`) and VData as game resources. |
| `DeadlockMount.Surfaces.cs` | Maps Deadlock surface properties (`surfaceproperties.vsurf_c`) to the closest s&box surface. |
| `Resource/ModelLoader.cs`, `ModelImporter*.cs` | Builds the s&box `Model`: meshes per draw call, LODs, skeleton, morphs and the flex rig, physics bodies and joints, attachments and hitboxes. |
| `Resource/MaterialImporter.cs` | Builds materials on the Deadlock shaders from `vmat_c` parameters. |
| `Resource/MapLoader*.cs` | Builds a map as a scene (`mount://deadlock/maps/<name>.scene`): world geometry, collision, entities, lighting (`MapLoader.Lighting.cs`), fog and post processing. |
| `Resource/TextureLoader.cs` | Uploads `vtex_c` mip chains (BCn as is), cubes, arrays, volumes and embedded PNG/JPEG. |
| `Formats/Vpk` | VPK directory and archive reader. |
| `Formats/Resource` | Compiled resource header and block table. |
| `Formats/KeyValues` | Binary KeyValues3 (v1 to v5; uncompressed, LZ4, Zstd). |
| `Formats/Compression` | Managed LZ4, Zstd and meshoptimizer vertex/index decoders. |
| `Formats/Mesh` | MBUF/VBIB and MVTX/MIDX buffers, attribute and packed normal/blend decoding. |
| `Formats/Model` | `vmdl_c` data: skeleton, LOD and mesh group masks, bone remap tables, embedded meshes, morph sets, physics and cloth (PHYS), attachments and hitboxes. |
| `Formats/Texture` | `vtex_c` header and per-mip LZ4/Zstd decompression. |
| `Formats/World` | `vwrld_c` worlds, `vwnod_c` world nodes (scene objects, aggregates and their fragments) and `vents_c` entity lumps. |
| `Formats/Material` | `vmat_c` parameters, and the albedo colour correction and tint matrices. |
| `../../Assets/shaders/deadlock_lighting.hlsl` | Deadlock's deferred lighting pass, as forward shading every Deadlock shader below uses. |
| `../../Assets/shaders/deadlock_pbr.shader` | Port of Deadlock's `pbr.vfx`, including the NPR hero lighting. |
| `../../Assets/shaders/deadlock_glass.shader` | `pbr.vfx` with `F_GLASS`. |
| `../../Assets/shaders/deadlock_environment.shader` | `environment_blend`, `environment_layer` and `environment_simple`. |
| `../../Assets/shaders/deadlock_parallax_layered.shader` | `environment_parallax_layered` (parallax windows). |
| `../../Assets/shaders/deadlock_overlay.shader` | `citadel_overlay` (decals laid on the world as geometry). |
| `../../Assets/shaders/deadlock_postprocess.shader` | Deadlock's `post_process`: exposure, bloom, vignette and its filmic tonemapping. |
| `../../Assets/shaders/deadlock_bloom.shader` | Deadlock's bloom passes (`bloom_downsample`, `gaussian_blur_bloom`). |
| `../../Code/Deadlock/Particles`, `Animation` | Ports of ValveResourceFormat's particle simulation and AnimGraph 2 runtime (Esoterica's), for the game code. |
| `../../Code/Deadlock/` | Game side: the `Deadlock Hero` player (`DeadlockHero.cs`, `DeadlockHeroData.cs`), `Deadlock Animator`, `Deadlock NPR Outline`, `Deadlock Bloom Layer`, `Deadlock Particle Effect`; `DeadlockMaterialExpressions`; the `DeadlockFlexRig` model data, its rule evaluator and the `Deadlock Flexes` component; `DeadlockBodyGroups` choice names and `SetDeadlockBodyGroup`; `DeadlockEntity` (map entity key values); the `Deadlock Map`, `Deadlock Map Lighting` and `Deadlock Post Processing` components. |

The parsing follows ValveResourceFormat, and the meshoptimizer decoders are ported from its port.
Both projects are MIT licensed.

## Status

- Models: geometry, normals and tangents, two UV sets, vertex color, skinning, skeleton and LODs. Cloth isn't
  simulated (see the limits), so the vertices of a cloth's simulated nodes (`$cloth_m0p12`, bones the cloth compiler
  puts at the skeleton's root that nothing animates) skin to what the cloth hangs from instead: the static nodes the
  fewest rods away (its pins at a waist or shoulder), each with its own skinning (`m_SkelParents` and
  `m_CtrlSoftOffsets`). Coats, skirts and capes follow the body as if skinned to it.
  Every model in the game decodes (6474 models, every LOD).
- Textures: every format Deadlock uses. Every texture decodes (15085, all mips).
- Materials: `pbr.vfx` (3187 materials: heroes and props) is ported. Albedo correction and tint, tint mask,
  vertex colour, metalness, roughness, packed normals, AO, self illumination with fresnel, texture scroll and
  transforms, alpha test (with the angle fade, vertex alpha and the alpha boost that thickens foliage with
  distance), front face normals on back faces, translucency, additive, unlit, vertex jitter (`F_JITTER_VERTICES`:
  ghosts, flames, ivy, cloth), depth written before blending (ghosts, smoke) and per material fog amounts (sky
  clouds). `black.vfx` and `black_unlit.vfx` are unlit black.
- Material dynamic expressions (a `vmat`'s DynamicParams, the game's bytecode) run through
  `DeadlockMaterialExpressions`: constant ones replace their parameter, `time()` ones run every frame (the
  scrolling water), and `$ent_random` ones (sign letters) give each placed object its own copy. Expressions
  reading other attributes (set by game code) keep the material's own value.
- Bloom amount override (`F_OVERRIDE_BLOOM_AMOUNT`: lamps, candles, neon, window facades): `Deadlock Bloom Layer`
  draws those models again into s&box's quarter resolution bloom layer, their colour times the amount and self
  illumination mask, which the bloom takes in without a threshold, as the game's G-buffer bloom mask does.
- Face flexes: every mesh morph (position and normal deltas from the MRPH atlas) becomes an s&box morph of
  the same name, on every LOD. 111 meshes in 82 models have them: troopers, medics, WIP heroes and props.
  The flex controllers and the rules that turn them into morph weights are stored on the model as
  `DeadlockFlexRig` game data, and the `Deadlock Flexes` component runs them: add it next to the
  `SkinnedModelRenderer` and set controllers (`jawOpen`, the troopers' `health`...) in its list or with `Set( name, value )`.
  Morphs can also be set directly on the renderer, bypassing the rules.
- Physics: the PHYS block's bodies (spheres, capsules, hulls, meshes, compounds), with their bone, bind pose, mass,
  damping and surface, and its joints. Characters get their ragdoll: Deadlock's conical joints become ball joints
  with the same frames, swing cone, twist range and friction; revolute, weld and prismatic joints become hinge,
  fixed and slider joints. Add `ModelPhysics` next to the `SkinnedModelRenderer` to ragdoll a character. Props get
  their collision.
- Body groups, from mesh group names like `head_@1_#&smug`: every choice of every group (empty ones too), with
  Deadlock's defaults. Heroes use them for facial expressions (fencer's head: `base`, `smug`, `cocky`...) and
  hidden weapons. s&box names every runtime choice "mesh", so the real names are stored as `DeadlockBodyGroups`
  model data; pick one with `renderer.SetDeadlockBodyGroup( "head", "cocky" )` (or by index in the inspector).
  Meshes outside any body group go in a one-choice `autodefault` group, as Deadlock does, because a mesh added to
  every group would claim every choice's bit and break choosing.
- Each mesh goes into the model once, with a mask of the LODs and body group choices it shows in (`ModelBuilder`'s
  private masked `AddMesh`, by reflection): s&box converts a skinned mesh's bone indices every time the mesh is
  added, so one added per LOD or choice ended up with them divided by the number of adds. Fencer, Celeste and Geist,
  whose bodies are in two choices of a body group, animated on the wrong bones (right only in the bind pose).
- Attachments (weapon hands, muzzles, ability cast points...) and hitbox sets, tagged with their hit group
  (`head`, `chest`, `stomach`, ...) like the Source mounts do.
- Maps: all 20 in `game/citadel/maps` load as scenes, built the way the game builds them.
  - Scene objects: every placed model with its transform (including scale and mirroring), tint, skin, LOD
    override, shadow flag and material overrides.
  - Aggregates (the merged world geometry): every fragment's draw call at its own transform, with its tint and
    LOD levels, baked into world space. The game culls each fragment on its own, and an aggregate holds one
    material's fragments from all over the map, so they're regrouped into 2048 unit cells (one scene object
    each, with a mesh per material; fragments spanning several cells get their own), which frustum and shadow
    culling can skip. Aggregates are read on all cores and upload as they finish.
- Vertices are kept in the formats the game stores them in (48 bytes, against 88 as floats): the 32 bit packed
  tangent frame, 16 bit lightmap coordinates, byte colours and vertex paint. The mount's shaders read them
  through `Assets/shaders/deadlock_vertexinput.hlsl` and `deadlock_vertex.hlsl`, which do s&box's vertex
  processing with the game's decoding; every packed frame in dl_midtown round-trips to the same normal and
  tangent. Skinned meshes keep float normals and tangents too, for s&box's compute skinning. Meshes whose
  material isn't one of the mount's (the default material standing in for a missing one, skies) keep the full
  float layout s&box's shaders read.
- Lighting: the shaders find the cubemap/probe volumes and the lights around a point through a grid over the map
  (`DeadlockMapLighting` builds it) instead of visiting all of them per pixel (166 volumes and 33 lights in
  dl_midtown); each cell lists them in the same order, so the result doesn't change.
- Particle systems sleep like the game's: undrawn for their `m_flNoDrawTimeToGoToSleep` (8 s by default), they
  stop simulating until they're drawn again.
  - Collision: `world_physics.vmdl`, one static collider per collision attribute, tagged with what it interacts
    as (`solid`, `playerclip`, `npcclip`, `sky`, `window`, `citadel_blockcamera`...).
  - Entities: every one becomes a GameObject named after it, carrying a `DeadlockEntity` component with its
    class, every key value and its outputs, parented like the map parents them. On top of that: props render
    and collide (`prop_physics` simulates), solid brushes block, other brush models are trigger volumes,
    breakable props get their VData model, `env_sky` becomes a `SkyBox2D` (sky.vfx materials map to s&box's
    sky shader), spawn points become `SpawnPoint`s, and the lights the game lights in realtime (dynamic and
    stationary, plus the sun) become light components with Deadlock's colour, brightness (2^EV for light2
    entities), range, cones, shadows, fog and diffuse/specular/transmissive contribution.
- Playing a map: mounted map scenes are read only, and pressing play on one plays the map as mounted, dropping
  anything added in the editor (a player, a camera). To play a map, add a `Deadlock Map` component to a scene of
  your own and set its map name (`dl_midtown`...): like s&box's `MapInstance`, it loads the map's objects as
  unsaved children, visible in the editor and loaded again when the game starts, next to your player and camera.
- Heroes (`F_USE_NPR_LIGHTING`) use Deadlock's NPR model: toon diffuse per light, the banded ambient
  "bounce" with the transmissive colour on the unlit side, held to fixed brightnesses after exposure (exposure
  control), and the masked rim light, hidden where the depth just outside the silhouette is close. In a map
  they're lit by its Deadlock lighting (probes, cubemaps, the sun and light2 lights), elsewhere by s&box's lights.
- NPR outlines: `Deadlock Hero` adds a `Deadlock NPR Outline`, which draws the model again through a hidden bone
  merged renderer with outline copies of its NPR materials (`F_NPR_OUTLINE`): pushed out along the normals by the
  material's outline mask, only the back faces around the silhouette. Maps add it to props with outlined NPR
  materials (shrines, buttons, candles), and world geometry with them gets an outline model per cell. Materials with a solid outline
  (`F_SOLID_COLOR_OUTLINE`, e.g. Abrams' orange, Drifter's dark red) multiply what's behind by their tint after
  the opaque scene; the rest are lit with the albedo darkened towards the game's outline colours.
- Map lighting, as Deadlock's deferred lighting pass does it (`deadlock_lighting.hlsl`, from the decompiled
  `deferred_lighting` compute shader): world geometry samples the second order harmonic lightmaps (with the ZH3
  sharpening) and its stationary lights' baked shadows; everything else the light probe ambient cube atlas.
  Reflections come from the box (or sphere) projected cubemap array, blended front to back by volume and
  normalized against the baked light. The sun uses its baked shadow channel times s&box's cascades; light2 lights
  (`light_barn`, `light_omni2`, `light_rect`) are built as the game's barn light faces (frustum, illumination box,
  distance, angle and skirt fades, barn shape or cookie, precomputed shadow atlas). The mount gathers it all into
  the map's `Deadlock Map Lighting` component, which hands it to the shaders as scene attributes, in the map's own
  space so the map can be placed anywhere.
- World shaders: `environment_blend` (up to three layers, reveal masks, borders, UV and world overlays, biplanar
  layers), `environment_layer`, `environment_simple`, `environment_parallax_layered` windows, `citadel_overlay`
  decals and `pbr` glass (what's behind blurred over the game's nine tap disc and refracted, and self
  illumination), each writing what the game's G-buffer holds before being lit. Aggregate fragments placed with a
  scale get their texture coordinates scaled the way the game's vertex shader scales them by model scale.
- Post processing: a map's `post_processing_volume` becomes a post process volume with `Deadlock Post Processing`
  (exposure fixed by the volume, the `.vpost`'s tonemapping curve, bloom and vignette). It replaces s&box's Tone
  Mapping and Bloom, so a camera shouldn't have them too. The bloom is the game's (`deadlock_bloom.shader`): the
  screen thresholded at a quarter of its size, halved four times, each level blurred with its four tap Gaussian
  and summed back up by the `.vpost`'s blur weights. The vignette's constants are packed as the game packs them.
- Fog: `env_gradient_fog`, `env_cubemap_fog`, `env_volumetric_fog_volume` and the fog controller's baked fog
  irradiance go on s&box's Source 2 fog (gradient, cubemap and volumetric).
- Particles: every `.vpcf` simulates with a port of ValveResourceFormat's particle code (all 17670 systems match
  its output, apart from the fixes below) and renders sprites, trails and ropes with `deadlock_particle.shader`.
  `info_particle_system` entities play in maps; the `Deadlock Particle Effect` component plays any system. Beyond
  ValveResourceFormat: `C_OP_ClampVector`, `C_OP_BoxConstraint`, `C_OP_SetControlPointFieldToScalarExpression`, and
  `C_OP_CycleScalar` reading its set method (it was dropped, so "scale initial value" cycles replaced the value).
- Particle lights: `C_OP_RenderStandardLight`, `C_OP_RenderOmni2Light` and `C_OP_RenderLightBeam` place lights as
  particles.dll builds them (read with IDA): colour from the tint quantised to bytes and blended by the renderer's
  blend type, then the engine's sRGB table; standard lights' classic attenuation (old style from the falloff
  linearity, new style through the fifty and zero percent distances), range cut where they fall below 0.001 and
  ended there; omni2 and beam lights as light2 omni and tube lights in lumens or candelas, the map's units. The
  `DeadlockParticleLights` scene system gathers them every frame: the Deadlock shaders read them from a buffer of
  their own while a map's Deadlock lighting is on, and everything s&box shades gets them as s&box lights (which
  only fall off with the square of distance, so linear ones are matched at 100 units).
- Animation: heroes play their AnimGraph 2 graphs (`hero.vnmgraph+<hero>`, with every child graph and clip) through
  a port of the AnimGraph 2 runtime, matching ValveResourceFormat's poses exactly. The `Deadlock Animator`
  component plays a model's graph and takes its parameters (`forward_speed`, `base_action`...).
- Playing as a hero: `Deadlock Map`'s Spawn Hero (Drifter by default; None to turn it off) spawns a `Deadlock Hero`
  at one of the map's spawn points when the game starts, or add `Deadlock Hero` to a GameObject yourself. Pick
  the hero by its in-game name. It moves like the game with the hero's own VData: run speed, out of combat sprint,
  acceleration, crouch, jump and air jump, ground and air dash (stamina, distance, duration, the dash curve), dash
  jump (its timing window, 19 m), slide (held crouch, the dash slide lockout and fail speed, activation speeds, the
  game's flat, slope and uphill friction, slope acceleration, camera steering), mantles (the game's four heights and
  rise times), wall jumps (reach by speed, out speed by where the keys point, lateral speed, fatigue on the upward
  speed recovering over 1.25 s, the stamina regeneration penalty, the wall attach pose) and melee (Melee action, Q:
  a tap swings a light attack, alternating the two swings; holding charges a heavy one at the charge move speed
  that strikes by itself; both lunge along their movement curve, slow the hero along their speed bonus curve and
  have the game's cooldowns). The movement code follows the game's own (server.dll, through Binary Ninja), with its
  convars and class defaults: ground acceleration of at least citadel_min_accel_speed (400); in the air a constant
  drag (citadel_air_drag_min 0.2, like friction) and air control that adds at most 80 units/s along the keys
  (50 above 800 units/s) at sv_airaccelerate 10, so momentum carries; air jumps set the full jump speed upward and
  add up to 200 units/s along the keys; air dashes turn gravity off, steer toward the dash with air control's cap
  raised to the dash speed (falling to 20% over it, 60% acceleration) and leave the hero its momentum when they end
  (then a short post drag over 12 m/s), one per airtime; with no keys a dash goes backward; ground dashes start
  from a standstill and follow their curve; slide jumps in a row drag from the second; the first wall jump in the air is free, the next
  cost half a stamina; dash jumps give 30% more air control for 0.25 s; melee turns heavy after 0.55 s held. The movement abilities' particles play
  (dash, slide, air jump, dash jump, wall jump), with control points bound as their children read them, and a
  crosshair with stamina pips can be drawn on the camera (Show Hud). For testing, Infinite Stamina is on. The graph gets the
  game's parameters: speeds relative to the body, look heading and pitch, movement type, aim set, out of combat,
  and actions (`ground_jump`, `Ground_Dash`, `turn_left_90`...) for one update each, as the game's graphs expect.
  Turning in place plays the turn animation and turns the body with its root motion. The camera sits over the
  right shoulder. Keys: move, Jump, Run (dash), Duck (crouch, slide), Attack1/2 (leave out of combat for a while).
  Changing the hero while playing swaps it in place. All 43 playable heroes' graphs run (Nurse Harrow ships
  without her main graph and isn't offered).
- Cosmic veils (`cosmic_veil.vfx`) are ported (`deadlock_cosmic_veil.shader`): what's behind them blurred and
  jittered through animated smoke, grey from the front, hue shifted from behind. Their collision only blocks sight
  in the game, so it's loaded as a trigger and heroes walk through.
- Materials are created at runtime, and s&box reads a texture a created material doesn't bind as its error
  checkerboard, so every texture a material leaves unset gets its shader's default.
- dl_midtown's `*spawn_block_brush` walls, which keep heroes in base until the match starts, aren't loaded: maps
  load as they are during a match.
- `generic` still falls back to `deadlock_pbr` (3 small materials on the maps).

## Known limits

- NPR settings (light wrap, step sharpness, rim strength, outline colours...) are engine-wide values in Deadlock,
  not part of materials. The defaults in `deadlock_pbr.shader` are the game's, from `materials/dev/npr_dummy.vmat`.
- The game keeps hero outlines off the hero with the stencil; here outline pixels are dropped where a surface sits
  just behind them (`g_flNprOutlineSelfDepth`), so an outline can show over a hero part a few units behind another.
- Not ported from `pbr.vfx` yet: detail textures, sheen, advanced translucency, cloak and status effects, and
  stepped NPR specular (off in the game's NPR settings). None of these are on the maps' materials but for a few
  tiny detail textured ones.
- The game lays translucent depth down in a pass of its own before blending (`F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING`).
  s&box has no order between two translucent draws, so these write depth while blending instead: a layer drawn
  after a nearer one is kept out, one drawn before it still shows.
- The game's camera clip capsule (the `r_citadel_clip_sphere_*` convars), which dithers away world geometry
  between the camera and the hero, isn't ported: its values are set by game code.
- Decals: the game keeps them off environment layers that don't accept overlays (`g_bAcceptStaticOverlays2`/`3`,
  per pixel by the blend) and off translucent surfaces; here they land on everything under them.
- Post processing: the colour correction LUT isn't applied (every map's is identity), and midtown's
  `2392_post_processing` volume, which lets the exposure adapt within half a stop, stays at its middle.
- The bloom layer is 10 bit; materials write their bloom as a fraction of `DeadlockPostProcess.BloomLayerRange` (64),
  so brighter bloom clips there.
- s&box vertices take 8 bit bone indices, so at most 256 bones can skin vertices. With cloth bones skinned to
  their anchors, three hero models (`abrams`, `mcginnis`, `ratking`) still skin with more. Their most influential bones
  (and parents) get the first 256 slots and the rest skin to their nearest kept parent: identical in bind pose, but
  those bones won't deform on their own once animated.
- At most 4 bone weights per vertex; the strongest are kept.
- Only meshes in the default mesh groups are imported; body groups and material groups (skins) are not.
- Not yet imported: material groups (skins).
- Hero values still estimated on `Deadlock Hero`: the collision hull, the camera offset and how long attacking
  keeps a hero in combat (the game counts damage instead). Movement runs once per frame rather than on the game's
  64 tick, and leaves out what modifiers from items and abilities change (slows, bonus speeds, gravity scales).
  Melee doesn't hit anything yet. Not emulated: corner boosts, ziplines, rope climbing, parry dash cancels,
  down dashes, surface jump factors,
  abilities and weapons. Twist and helper bones that the graph's skeleton doesn't have (bone constraints in the
  game) hold their bind pose under their parents.
- Particle renderers for models, sounds and blobs, `C_OP_RenderDeferredLight`, `C_OP_LockToBone`, path particle
  ropes (midtown's cables) and snapshots aren't supported yet. Particle lights don't cast shadows or light fog.
- Deadlock's distance field AO (an 8 bit screen texture its NPR and pbr shaders read) isn't ported.
- s&box has 17 surfaces to Deadlock's 100 or so; each maps to the nearest by name, base surface or keyword.
  Collision attribute tags (`Citadel_BlockCamera`, `passbullets`...) aren't carried over.
- Hulls of more than 44 points are simplified (IVR) so s&box can cook them, which can shrink them slightly.
- Bodies without shapes (a `root_motion` body on some heroes) are dropped; attachments that blend several bones
  (4 in the game) keep their strongest bone; hitboxes on unnamed bones (a few neutral creatures) are skipped.
- Map lighting differences from the game: lights that the game shadows dynamically (`castshadows 1` without a
  precomputed shadow) aren't shadowed, apart from the sun; overlays are lit by the probes rather than the
  lightmap of the surface under them; the D_AREA_LIGHTS rect light shading and the AO / distance field occlusion
  passes aren't ported. Three values the game sets from its engine are estimates: how much rough specular comes
  from the lightmap (`LightmapSpecular`), the vignette's constants and the bloom's normalization.
- Volumetric fog is s&box's (Source 2's) rather than Deadlock's own `cs_volumetric_fog`, and uses s&box's camera
  defaults for draw distance and fade.
- Mount changes take a full s&box restart to show: models and materials stay cached.
- Mount meshes hold the game's compact vertices, which only the mount's shaders decode: a material of your own
  put on a mounted model (MaterialOverride) shades it with wrong normals and lightmap coordinates.
- Scene object fade distances have no s&box equivalent. light_rect lights are drawn as 90 degree spot lights.
- Map VPKs share s&box's single mount namespace: where community (bhop) maps bundle a file under a path the
  base content or another map also uses, one version serves all of them.
- dl_midtown is big: its world is about 37 million vertices once its fragments are placed (2 GB of VRAM with
  their indices, at 48 bytes per vertex), and its material textures are about 4 GB. Its world geometry has no
  lower LODs to fall back on (the map compiler only gives fragments LOD 0), so instead dl_midtown loads textures
  larger than 1024 one mip down, from its own copies of its materials and models (`TextureLevels` in `MapLoader.cs`,
  `DeadlockMount.Reduced.cs`), which takes them to about 1.25 GB. Its lightmaps, cubemaps, probes and fog keep
  their full detail, as does everything outside the map.
- Cloth (`m_pFeModel`, 88 models) isn't simulated: s&box has no cloth solver (its native code only keeps Source 2's
  schema types, and physics bones only cover bone-merged accessories), so it would take a custom simulation. Until
  then cloth follows its pins (`ModelImporter.Cloth.cs`): it moves with the body but doesn't swing, hang or collide.
  The nodes' own animated pose isn't used, as long skirts skinned to it swing out with the legs (the game barely pulls
  cloth toward it). Simulated bones inside the skeleton (tails, chains, ponytails) are left to the animation graph or
  their bind pose under their parents. Simulating it means taking that out and driving the `$cloth` bones.
- Morph wrinkle maps and speed (the `.w` channels of the morph bundles) are not used. Flex rules run on the
  CPU every frame, so only the component (not animation) drives controllers for now.
- Legacy `VKV3` KeyValues and `vmesh_c` referenced meshes are not supported (Deadlock's main VPKs use neither).

## Next steps

1. Dynamic shadows for barn lights, area light shading, Deadlock's own volumetric fog.
2. Port `generic` and the remaining `pbr.vfx` features.
3. Model particles, deferred particle lights, path particle ropes, distance field AO.
4. Hero bone constraints (twist bones), ziplines, the sprint particle (needs particles on model bones).
