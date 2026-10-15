# G15 visual prototype

These original AI-generated raster assets are a replaceable visual test for the isometric client. They are not Blackmarsh source art. The character sheets are transparent 2×2 grids (southwest, southeast, northwest, northeast); Godot selects quadrants with `AtlasTexture`. Movement, attack, hit and prone motion are client-side transformations of the still frames. The simulation remains authoritative for movement, light and combat.

Generated with Codex's built-in image generation tool. Prompt set:

- `Characters/paladin-directions.png`: one original human paladin in chain mail, blue cloak, long sword and shield; four consistent full-body isometric directions in a precise transparent 2×2 grid; late-1990s hand-painted fantasy RPG atmosphere; neutral pose, aligned boots; no floor, text or watermark.
- `Characters/guard-directions.png`: one original middle-aged village guard in helmet, mail and muted green tabard with spear; the same four-view transparent sheet, scale, lighting and constraints.
- `Characters/farmer-directions.png`: one original village farmer in ochre and earth-green work clothes, unarmed; the same four-view transparent sheet, scale, lighting and constraints.
- `Ground/road.png`: seamless top-down material swatch of worn cobblestones and packed earth; muted hand-painted low-fantasy style; no props, perspective, text or watermark.
- `Ground/wood.png`: seamless top-down material swatch of worn amber-brown inn floorboards, with scratches and grain; the same style and constraints.
- `Ground/grass.png`: seamless top-down material swatch of grass and compacted earth for a village yard; the same style and constraints.
- `Ground/stone-wall.png`: seamless front-on material swatch of weathered limestone masonry with mortar and slight moss; the same style and constraints.

The sheets are prototypes, not animation frame sheets. Future art can replace each asset without changing Sim data or movement rules.

The optional 3D walk experiment is available in the village with **P**. It uses `Characters/paladin-3d-walk-8x8.png`: 8 facings and 8 walking phases rendered from one articulated Godot character. The source scene is `Scenes/Paladin3DPrototype.tscn` and its code is `Scripts/Paladin3DPrototype.cs`. Press P again to restore the original painted sprite. This low-poly model tests motion continuity and facing; it is not the final art direction.

The source PNGs are stored at 512 pixels on their longest side. The client scales character quadrants to roughly 50 screen pixels wide and samples material patches in proportion to each texture's dimensions.
