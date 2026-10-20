# Deadlock mount for s&box

A mount that lets s&box load stuff from your Deadlock install: heroes, props, maps, materials, particles and animations. I made it so I could mess around with Deadlock content in s&box's Sandbox game mode and in the editor. Spawn heroes, ragdoll them, walk around the maps, that kind of thing.

It's not a Deadlock remake and I'm not trying to make one. There are no abilities, guns, items, shop, NPC AI, lanes or multiplayer, and I don't plan on adding any of that. The hero controller is just there so you have something to run around the maps with.

Nothing from the game is in this repo. The mount reads the files straight out of your own Deadlock install at runtime, so you need to own the game and have it installed on Steam. Please don't use this to rip assets and reupload them, or to publish s&box packages with Deadlock content in them.

If anyone at Valve wants this taken down or changed, open an issue and I'll do it or contact me on discord via .divined

## What you can do with it

In the editor or the Sandbox game:

- Spawn any of the game's models from the asset browser. That's 10k+ of them, including all 43 heroes, troopers, bosses and props, with their skins and body groups.
- Ragdoll characters. They come with their actual ragdoll setup from the game, so just add `ModelPhysics` next to the `SkinnedModelRenderer`.
- Change hero expressions through body groups, e.g. `renderer.SetDeadlockBodyGroup( "head", "smug" )`. Troopers and a few unreleased heroes also have face morphs you can play with using the `Deadlock Flexes` component.
- Play any of the ~17k particle effects with the `Deadlock Particle Effect` component.
- Play the game's animation graphs on its models with the `Deadlock Animator` component.

Maps:

- All 20 maps load through the `Deadlock Map` component (`dl_hideout`, `dl_midtown`, `new_player_basics` and so on). You get the geometry, collision, props, baked lighting, lights, fog, post processing and particles, as the map was made.

Playing as a hero:

- Drop a `Deadlock Map` in a scene and hit play, or put a `Deadlock Hero` component on a GameObject, and pick a hero.
- You can run, sprint, crouch, jump and double jump, dash, dash jump, slide, mantle and wall jump. There's light and heavy melee too, but it doesn't hit anything.
- The movement values come from the hero and ability data in the game files, so it should feel close.
- Controls are WASD, Space to jump, Shift to dash, Ctrl to crouch/slide and Q to melee (tap for light, hold for heavy).

## Building it

You need Deadlock and s&box installed, plus the .NET 10 SDK.

1. Clone the repo.
2. Run `Build\build-mount.cmd`. It finds s&box through the `FACEPUNCH_ENGINE` environment variable, or falls back to the default Steam folder. If yours is somewhere else: `dotnet build Build\MountPackage\deadlock.csproj -c Release -p:SboxPath="D:\your\sbox"`
3. Copy `Build\mount\deadlock` into your s&box `mount` folder, so you end up with `sbox\mount\deadlock\deadlock.dll`.
4. Launch s&box and turn on the Deadlock mount.

If you want to work on the mount itself, open `deadlockmount.sbproj` in the s&box editor instead. Take the built copy out of `sbox\mount` first though, otherwise you'll have two mounts called "deadlock" and the old one wins.

## Known issues

- `dl_midtown` is huge (around 37 million vertices) and still runs badly. It uses about 5 GB of VRAM with the editor open, and its textures load one mip lower so it fits.
- The Sandbox game mode support is new and I haven't tested it much yet. If a map loads without its lighting or particles, that's probably why.
- Cloth isn't simulated, some shader features are still missing, and a few particle renderer types (lights, models) aren't supported.
- Deadlock is still in development, so game updates might break things now and then.

## Credits

- Valve, for Deadlock and Source 2. All the game content is theirs.
- Facepunch, for s&box and its mount API.
- [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) (MIT). The file format readers follow its work, and the particle simulation and animation graph runtime in `Code/Deadlock/Particles` and `Code/Deadlock/Animation` are ported from it, with its licence kept next to them. Its versions of Deadlock's shaders were the reference for mine.
- [Esoterica](https://github.com/BobbyAnguelov/Esoterica) by Bobby Anguelov (MIT), which the animation graph runtime comes from.
- [meshoptimizer](https://github.com/zeux/meshoptimizer) by Arseny Kapoulkine (MIT), for the vertex and index decoding, ported via ValveResourceFormat.
- [LZ4](https://github.com/lz4/lz4) and [Zstandard](https://github.com/facebook/zstd), whose compression formats the decoders are written from.

## Legal

Deadlock, Valve and Source are trademarks of Valve Corporation, and s&box is Facepunch Studios'. This is a fan project and isn't affiliated with or endorsed by either of them. It doesn't include any Valve assets and it's not for commercial use.
