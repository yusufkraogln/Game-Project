# Free Low-Poly Characters (CC0): 8 rigged blocky characters

Eight rigged, animated low-poly characters, free and public domain: Hero, Knight, Wizard, Astronaut, Pirate, Robot, Zombie and Princess. They all share one 22-bone skeleton with Mixamo-compatible bone names and come with 10 animations each. GLB and FBX, ready for Godot, Unity, Unreal Engine, Blender and three.js. No credit needed, commercial use is fine.

![Cover: the 8 free blocky characters](Preview/cover_630x500.png)

![Six of the 10 clips on the hero: idle, walk, run, jump, fall and wave](Preview/six_clips.png)

These are eight of the characters from my bigger [48 Rigged Characters + 30 Animations](https://vuena.itch.io/blocky-characters-48) pack. Because they share one skeleton and the same clip names, your code does not change when you swap a hero for a knight. Put one in your game in a few minutes and see whether you like the style.

## What is inside

| | |
| --- | --- |
| Characters | 8: hero, knight, wizard, pirate, astronaut, robot, zombie, princess |
| Files | 8 GLB (`GLB/`) + 8 FBX (`Unity/`) + palette textures + a small Godot 4 sample project (`Godot/`) |
| Rig | one shared 22-bone humanoid skeleton, Mixamo-compatible bone names |
| Animations | 10 per character, 30 fps, in place: `idle`, `walk`, `run`, `jump`, `fall`, `wave`, `cheer`, `dance`, `punch`, `death` |
| Size | 2466 to 4628 triangles each, one shared material, one 256x256 palette texture |
| Scale | 1 unit = 1 m, about 1.8 m tall, origin at the feet, facing +Z |

```
GLB/        8 .glb   (web, Godot, Unreal, Blender)
Unity/      8 .fbx + palette textures + README_Unity.txt
Godot/      small Godot 4 project (Up/Down cycle the clips)
Textures/   blocky_palette.png (+ emissive mask)
Preview/    cover, a thumbnail per character, a clip strip
characters.csv, CHANGELOG.txt, PACK-NOTES.txt
LICENSE     the CC0 1.0 legal text
LICENSE.txt the dedication that shipped inside the itch.io zip
```

| | | | |
| --- | --- | --- | --- |
| ![hero](Preview/thumbnails/blocky_01_hero.png) | ![knight](Preview/thumbnails/blocky_03_knight.png) | ![wizard](Preview/thumbnails/blocky_05_wizard.png) | ![pirate](Preview/thumbnails/blocky_06_pirate.png) |
| ![astronaut](Preview/thumbnails/blocky_07_astronaut.png) | ![robot](Preview/thumbnails/blocky_08_robot.png) | ![zombie](Preview/thumbnails/blocky_09_zombie.png) | ![princess](Preview/thumbnails/blocky_19_princess.png) |

## Using them

- **Godot 4:** drop a `.glb` into your project (or open `Godot/` as a project, Godot 4.2+). glTF clips import without looping, so set Loop Mode to Linear on the looping ones. `Godot/demo.gd` lists them and shows how.
- **Unity:** import the `.fbx` and choose Rig = Humanoid or Generic (see `Unity/README_Unity.txt`).
- **Unreal Engine:** import the `.fbx`, or the `.glb` with a glTF importer.
- **Blender:** import the `.glb`.
- **three.js / Babylon.js:** load the `.glb` with GLTFLoader or SceneLoader and play the clips by name.

## Good to know

- Skinning is rigid by design (no joint blending), which gives the blocky toy look. There is no facial animation, and the rest pose has the arms hanging down (not a T-pose).
- The GLB files are validated with the Khronos glTF Validator, and the FBX files are exported from Blender.
- The Godot sample project has not been tested in the Godot editor yet. The GLB files themselves are plain glTF and import normally.
- The lights, the floor and the backdrop in the pictures are not in the files. I rendered them in Blender with lights I added.

## Licence

**CC0 1.0 Universal (public domain).** Use the eight characters in anything, commercial or not, with no credit and no need to ask me. The waiver covers only the files in this repo. The paid pack has its own royalty-free licence.

## How they were made, honestly

I build the characters procedurally with my own Blender Python code, and I wrote that code with AI assistance. The meshes and textures themselves are not AI-generated.

## Want the whole cast?

The full pack is **[48 Rigged Characters + 30 Animations on itch.io](https://vuena.itch.io/blocky-characters-48)**: 40 more characters (ninja, viking, king, vampire, clown, diver, elf, skeleton, alien and more) and 30 animations, including walk_back, strafe, sprint, crouch, kick, sword slash, shoot, block, hit reaction, clap, sit, climb and swim. GLB and FBX, with a Godot sample project and a Blender file. The current price is on its page. It is also part of the [Vuena All-in-1](https://vuena.itch.io/complete-collection). Buying it is what keeps me making these, and this free sample stays free either way.

- Free sample on itch: https://vuena.itch.io/blocky-characters-free-sample
- Browse every model: https://models.popyca.com
- All packs: https://vuena.itch.io
- A tiny Godot 4 project that uses these characters: [Vuena/blocky-pets-godot-starter](https://github.com/Vuena/blocky-pets-godot-starter)
