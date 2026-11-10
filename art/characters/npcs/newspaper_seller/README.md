# The newspaper seller

`newspaper_seller.glb`: a woman in a polka-dot shirt and a button skirt (Tripo-generated, rigged on
Mixamo's 65-bone `mixamorig:` skeleton), supplied by the director on 2026-11-10 as `ian anim.zip`
(one FBX and its `.fbm` textures, no animations of its own). About 0.98 m tall as imported, shown at
1.62 m; rests in a T-pose facing +Z.

Converted with Blender 5.1: the FBX's material wired Tripo's maps to the wrong inputs (roughness to
specular, metallic to roughness), so it was rebuilt (base colour, normal, roughness, metallic), the
4K base colour cut to 2K and the other maps to 1K, and the whole exported as a JPEG-textured GLB
(2.5 MB instead of 69 MB). Godot extracts the three textures next to it on import.

How it's used: `narrative/NewspaperSeller.cs` plays the Universal Animation Library's idle, talking,
walk and jog clips on a hidden mannequin and `player/RigFollower` copies its pose onto her (Mixamo
pairs, the same as the carrier's Mixamo body). The bundle of papers in her left hand is a box on a
`BoneAttachment3D`. Debug scene: `debug/NewspaperSellerLab.tscn`.
