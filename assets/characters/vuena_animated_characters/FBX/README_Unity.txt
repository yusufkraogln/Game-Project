UNITY NOTES - 48 Blocky Characters
==================================

Files in this folder
    blocky_NN_name.fbx            48 rigged characters (22-bone Mixamo-named skeleton, 30 animation takes)
    blocky_palette.png            shared colour palette (base colour)
    blocky_palette_emissive.png   glow mask (optional emission)

Import steps (Unity 2021 LTS or newer, Built-in / URP / HDRP)
 1. Copy this whole folder into Assets/ . Keep the PNG files in the same folder as the FBX files.
 2. Palette texture: select blocky_palette.png in the Project window and set
        Filter Mode = Point (no filter), Compression = None, Generate Mip Maps = off.
 3. Select the FBX files (select all 48 at once if you like) > Inspector:
        Model tab:     Scale Factor 1, Convert Units on  (the model is already 1 unit = 1 metre, about 1.8 m tall)
        Rig tab:       Animation Type = Humanoid (retargetable) or Generic (exact original motion) > Apply
        Animation tab: 30 takes (idle, walk, run, jump, fall, wave, walk_back, strafe_left, strafe_right, sprint, crouch_idle, crouch_walk,
                       punch, kick, sword_slash, shoot, block, hit_react, death, die_idle, idle_look_around, cheer, dance, clap,
                       sit_down, sit_idle, interact, push, climb, swim).
                       Tick Loop Time for the looping ones: idle, walk, run, fall, wave, walk_back, strafe_left, strafe_right, sprint,
                       crouch_idle, crouch_walk, shoot, block, die_idle, idle_look_around, cheer, dance, clap, sit_idle, push, climb, swim.
                       Leave Loop Time off for the one-shots: jump, punch, kick, sword_slash, hit_react, death, sit_down, interact.
        Materials tab: Location = Use External Materials (Legacy) or Use Embedded Materials, then
                       assign blocky_palette.png as the Base Map / Albedo of the material "BlockyPalette".
                       To make the lights glow, assign blocky_palette_emissive.png to the Emission slot and enable Emission.
 4. Create ONE material from your render pipeline's Lit shader with the palette as base map, and reuse it on
    all characters (they all use the same UVs and the same texture).
 5. Drag an FBX into the scene. For a quick test create an Animator Controller, add the clips you need as states
    and transition between them with a "speed" parameter (idle < 0.1 <= walk < 3 <= run).

Humanoid note
    The skeleton uses the standard Mixamo bone names, so Unity's Humanoid auto-mapping works and you can play
    Mixamo / Humanoid animations on these characters. The rest pose has the arms hanging down (A-pose style, not a
    T-pose); if Unity asks, open Configure... and press "Pose > Enforce T-Pose" before applying.

Notes
    * Skinning is rigid: each part follows exactly one bone (blocky toy look).
    * Characters face +Z (Unity forward) with the origin at the feet.
