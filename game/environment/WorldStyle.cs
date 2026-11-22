using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>The mountain's palette: every material a placed thing may be drawn with.</summary>
public enum Look
{
    /// <summary>Raw rock: the boulders' own textured rock (outcrops, walls, scree, spires).</summary>
    Rock,
    /// <summary>Worked stone: ruins, graves, towers, columns, cairns.</summary>
    Stone,
    Earth,
    Bark,
    /// <summary>Cut and weathered timber: crates, fences, signposts, carts.</summary>
    Wood,
    Plaster,
    /// <summary>Canvas, felt and banners.</summary>
    Cloth,
    /// <summary>Iron and old tools.</summary>
    Metal,
    Wax,
    Foliage,
    FoliageDark,
    FoliageFall,
    /// <summary>Flower petals (heather, dog rose, ochre, pale) and mushroom caps and stems.</summary>
    Heather,
    Rose,
    Ochre,
    Pale,
    CapRed,
    CapTan,
    Stem,
    Water,
}

/// <summary>
/// One style for the whole mountain, the grounded, muted, natural look of the textured ground, the
/// rocks, the generated trees and the carrier (Death Stranding 2 is the reference). Every model an
/// art pack brings (Kenney's flat-coloured kits, Quaternius' village and animals) is drawn with a
/// material from this palette instead of its own: a muted natural colour broken up by a real rock
/// or soil texture (environment/world_natural.gdshader), lit like the terrain. Saturated flat
/// colours and untextured paper polygons don't occur. Animals get muted fur coats and smooth
/// shading. <see cref="IsInStyle"/> is the rule the style test checks every placed thing against.
/// </summary>
public static class WorldStyle
{
    public const string ShaderPath = "res://environment/world_natural.gdshader";
    public const string RockColourPath = "res://art/environment/ground/rock_ground_diff_1k.jpg";
    public const string RockNormalPath = "res://art/environment/ground/rock_ground_nor_gl_1k.jpg";
    public const string SoilColourPath = "res://art/environment/ground/forest_ground_04_diff_1k.jpg";
    /// <summary>Mean brightness of the two ground textures (keeps a tint's level when they shade it).</summary>
    private const float RockMean = 0.42f;
    private const float SoilMean = 0.36f;

    /// <summary>Anything solid taller than this (m) blocks the carrier: rocks, stumps, walls, props.</summary>
    public const float KneeHeight = 0.5f;
    /// <summary>A bush this tall (m) or taller is too dense to wade through; smaller shrubs are not.</summary>
    public const float BigBushHeight = 1.2f;
    /// <summary>The most saturated and brightest a palette colour may be (HSV, in sRGB).</summary>
    public const float MaxSaturation = 0.55f;
    public const float MaxValue = 0.8f;

    /// <summary>
    /// The palette: each look's colour, its texture (rock or soil), how many metres the texture
    /// spans, how strongly it shows, and its roughness and metalness.
    /// </summary>
    private static readonly Dictionary<Look, (Color Tint, bool Soil, float Metres, float Strength, float Rough, float Metal)> Palette = new()
    {
        [Look.Stone] = (new Color(0.62f, 0.6f, 0.56f), false, 1.2f, 1.0f, 0.95f, 0.0f),
        [Look.Earth] = (new Color(0.46f, 0.38f, 0.29f), true, 1.6f, 1.0f, 1.0f, 0.0f),
        [Look.Bark] = (new Color(0.36f, 0.29f, 0.23f), false, 0.6f, 1.0f, 1.0f, 0.0f),
        [Look.Wood] = (new Color(0.5f, 0.41f, 0.31f), true, 0.8f, 0.8f, 0.95f, 0.0f),
        [Look.Plaster] = (new Color(0.66f, 0.62f, 0.55f), false, 1.5f, 0.7f, 1.0f, 0.0f),
        [Look.Cloth] = (new Color(0.48f, 0.42f, 0.33f), true, 0.5f, 0.45f, 1.0f, 0.0f),
        [Look.Metal] = (new Color(0.26f, 0.25f, 0.24f), false, 0.5f, 0.6f, 0.7f, 0.45f),
        [Look.Wax] = (new Color(0.72f, 0.68f, 0.58f), false, 0.3f, 0.2f, 0.8f, 0.0f),
        [Look.Foliage] = (new Color(0.26f, 0.34f, 0.2f), true, 0.4f, 0.6f, 1.0f, 0.0f),
        [Look.FoliageDark] = (new Color(0.17f, 0.25f, 0.17f), true, 0.4f, 0.6f, 1.0f, 0.0f),
        [Look.FoliageFall] = (new Color(0.55f, 0.38f, 0.18f), true, 0.4f, 0.6f, 1.0f, 0.0f),
        [Look.Heather] = (new Color(0.44f, 0.37f, 0.5f), true, 0.3f, 0.4f, 1.0f, 0.0f),
        [Look.Rose] = (new Color(0.56f, 0.34f, 0.31f), true, 0.3f, 0.4f, 1.0f, 0.0f),
        [Look.Ochre] = (new Color(0.62f, 0.52f, 0.3f), true, 0.3f, 0.4f, 1.0f, 0.0f),
        [Look.Pale] = (new Color(0.72f, 0.7f, 0.63f), true, 0.3f, 0.35f, 1.0f, 0.0f),
        [Look.CapRed] = (new Color(0.46f, 0.28f, 0.22f), true, 0.3f, 0.5f, 0.85f, 0.0f),
        [Look.CapTan] = (new Color(0.58f, 0.46f, 0.34f), true, 0.3f, 0.5f, 0.9f, 0.0f),
        [Look.Stem] = (new Color(0.7f, 0.66f, 0.57f), true, 0.3f, 0.4f, 0.9f, 0.0f),
        [Look.Water] = (new Color(0.2f, 0.25f, 0.26f), false, 2.0f, 0.3f, 0.15f, 0.0f),
    };

    private static readonly Dictionary<string, Material> Materials = new();
    private static readonly Dictionary<ulong, Mesh> StyledMeshes = new();
    private static readonly Dictionary<ulong, Mesh> SmoothMeshes = new();
    private static readonly HashSet<ulong> Registered = new();
    private static readonly Dictionary<ulong, string> Names = new();
    private static Shader? _shader;
    private static Texture2D? _rockTexture;
    private static Texture2D? _soilTexture;
    private static Texture2D? _furTexture;
    private static Texture2D? _spotTexture;
    private static StandardMaterial3D? _rock;

    /// <summary>The terrain's own materials and the generated plants (made by the project itself in this style).</summary>
    private static readonly string[] NativeShaders =
    {
        "res://terrain/terrain_ground.gdshader", "res://terrain/forest_grass.gdshader",
        "res://terrain/grass_carpet.gdshader", "res://terrain/foliage_tree.gdshader", ShaderPath,
    };

    private static Shader? NaturalShader()
    {
        if (_shader == null && ResourceLoader.Exists(ShaderPath)) { _shader = ResourceLoader.Load<Shader>(ShaderPath); }
        return _shader;
    }

    private static Texture2D? Load(string path)
    {
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }

    /// <summary>The boulders' textured rock, shared by every rock on the mountain.</summary>
    public static StandardMaterial3D Rock => _rock ??= RockMaterial(RockColourPath, RockNormalPath, 1.5f, new Color(0.5f, 0.5f, 0.52f));

    /// <summary>
    /// The scattered rocks' version of a textured rock material (terrain/rock_blend.gdshader): the same
    /// texture, with the ground they stand on over their tops and at their foot (the ground's colour comes
    /// per instance). Null when the rock has no texture to share.
    /// </summary>
    public static ShaderMaterial? RockBlend(StandardMaterial3D rock, bool single = false)
    {
        string key = "rock blend|" + rock.GetInstanceId() + (single ? "|single" : "");
        if (Materials.TryGetValue(key, out Material? cached) && cached is ShaderMaterial known) { return known; }
        if (rock.AlbedoTexture == null || ResourceLoader.Load<Shader>("res://terrain/rock_blend.gdshader") is not Shader shader) { return null; }
        ShaderMaterial material = new() { Shader = shader, ResourceName = "style:rock" };
        material.SetShaderParameter("rock_colour", rock.AlbedoTexture);
        if (rock.NormalTexture != null) { material.SetShaderParameter("rock_normal", rock.NormalTexture); }
        material.SetShaderParameter("normal_strength", rock.NormalEnabled ? rock.NormalScale : 0.0f);
        material.SetShaderParameter("metres", 1.0f / Mathf.Max(rock.Uv1Scale.X, 0.001f));
        material.SetShaderParameter("tint", new Color(0.5f, 0.5f, 0.52f));
        material.SetShaderParameter("from_instance", single);
        Register(material, "rock");
        Materials[key] = material;
        return material;
    }

    /// <summary>A textured rock material (TerrainStreamer's settings may point at other textures); it joins the palette.</summary>
    public static StandardMaterial3D RockMaterial(string colourPath, string normalPath, float metres, Color fallback)
    {
        string key = "rock|" + colourPath + "|" + normalPath + "|" + metres;
        if (Materials.TryGetValue(key, out Material? cached) && cached is StandardMaterial3D known) { return known; }
        StandardMaterial3D material = new() { AlbedoColor = fallback, Roughness = 1.0f, ResourceName = "style:rock" };
        if (Load(colourPath) is Texture2D colour)
        {
            material.AlbedoColor = Colors.White;
            material.AlbedoTexture = colour;
            material.Uv1Triplanar = true;
            material.Uv1WorldTriplanar = true;
            material.Uv1Scale = Vector3.One / Mathf.Max(metres, 0.1f);
            if (Load(normalPath) is Texture2D normal)
            {
                material.NormalEnabled = true;
                material.NormalTexture = normal;
                material.NormalScale = 0.5f;
            }
        }
        Register(material, "rock");
        Materials[key] = material;
        return material;
    }

    /// <summary>The palette's material for a look (Rock is the boulders' textured rock).</summary>
    public static Material For(Look look)
    {
        return look == Look.Rock ? Rock : Natural(look, null, 0.0f);
    }

    /// <summary>
    /// A palette look in a builder's own shade (hand-built signs, camps, carts and cairns): the
    /// look's texture and finish with this colour, muted first when it is too bright or saturated.
    /// </summary>
    public static Material Tinted(Look look, Color tint)
    {
        Color muted = Muted(tint) ? tint : Mute(tint);
        string key = "tint|" + look + "|" + muted.ToHtml(false);
        if (Materials.TryGetValue(key, out Material? cached)) { return cached; }
        var swatch = Palette.TryGetValue(look, out var found) ? found : Palette[Look.Stone];
        Material material = Make(muted, swatch.Soil, swatch.Metres, swatch.Strength, swatch.Rough, swatch.Metal, null, 0.0f, false, look.ToString().ToLowerInvariant());
        Materials[key] = material;
        return material;
    }

    /// <summary>A plain solid box the carrier bumps into (for hand-built things that look solid), centred at <paramref name="centre"/>.</summary>
    public static StaticBody3D Solid(Node3D parent, Vector3 size, Vector3 centre, Vector3 rotationDegrees = default)
    {
        StaticBody3D body = new() { Name = "Solid", Position = centre, RotationDegrees = rotationDegrees };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
        return body;
    }

    /// <summary>A light or glow, not a surface: embers, beacons, highlights (the style rule leaves them alone).</summary>
    public static bool IsEffect(Material? material)
    {
        return material is BaseMaterial3D standard && (standard.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded
            || standard.EmissionEnabled || standard.Transparency != BaseMaterial3D.TransparencyEnum.Disabled);
    }

    /// <summary>A look shaded by a model's own colour atlas (its light and dark only).</summary>
    private static Material Natural(Look look, Texture2D? atlas, float atlasStrength)
    {
        if (look == Look.Rock && atlas == null) { return Rock; }
        string key = look + "|" + (atlas?.GetInstanceId() ?? 0) + "|" + atlasStrength;
        if (Materials.TryGetValue(key, out Material? cached)) { return cached; }
        var swatch = Palette.TryGetValue(look, out var found) ? found : Palette[Look.Stone];
        if (look == Look.Rock) { swatch = (new Color(0.56f, 0.54f, 0.5f), false, 1.5f, 1.0f, 1.0f, 0.0f); }
        Material material = Make(swatch.Tint, swatch.Soil, swatch.Metres, swatch.Strength, swatch.Rough, swatch.Metal, atlas, atlasStrength, false, look.ToString().ToLowerInvariant());
        Materials[key] = material;
        return material;
    }

    private static Material Make(Color tint, bool soil, float metres, float strength, float rough, float metal,
        Texture2D? atlas, float atlasStrength, bool objectSpace, string name, Texture2D? detail = null, float mean = 0.0f)
    {
        Shader? shader = NaturalShader();
        Material result;
        if (shader == null)
        {
            result = new StandardMaterial3D { AlbedoColor = tint, Roughness = rough, Metallic = metal };
        }
        else
        {
            ShaderMaterial material = new() { Shader = shader };
            material.SetShaderParameter("tint", tint);
            Texture2D? texture = detail ?? (soil ? _soilTexture ??= Load(SoilColourPath) : _rockTexture ??= Load(RockColourPath));
            if (texture != null)
            {
                material.SetShaderParameter("detail", texture);
                material.SetShaderParameter("detail_strength", strength);
            }
            else
            {
                material.SetShaderParameter("detail_strength", 0.0f);
            }
            material.SetShaderParameter("detail_mean", mean > 0.0f ? mean : soil ? SoilMean : RockMean);
            material.SetShaderParameter("detail_metres", metres);
            material.SetShaderParameter("roughness_value", rough);
            material.SetShaderParameter("metallic_value", metal);
            material.SetShaderParameter("object_space", objectSpace);
            if (atlas != null && atlasStrength > 0.0f)
            {
                material.SetShaderParameter("source", atlas);
                material.SetShaderParameter("source_strength", atlasStrength);
            }
            result = material;
        }
        result.ResourceName = "style:" + name;
        Register(result, name);
        return result;
    }

    private static void Register(Material material, string name)
    {
        Registered.Add(material.GetInstanceId());
        Names[material.GetInstanceId()] = name;
    }

    /// <summary>The palette entry's name for a registered material ("" when it isn't one).</summary>
    public static string NameOf(Material? material)
    {
        return material != null && Names.TryGetValue(material.GetInstanceId(), out string? name) ? name : "";
    }

    /// <summary>
    /// The style rule: a material belongs to the palette (and its colour is muted), or it is one of
    /// the terrain's own materials, or the generated plants' vertex colours. Anything else (an art
    /// pack's own flat colours, an unlisted material) is out of style; <paramref name="why"/> says how.
    /// </summary>
    public static bool IsInStyle(Material? material, out string why)
    {
        why = "";
        if (material == null)
        {
            why = "no material (the model's own colours)";
            return false;
        }
        if (Registered.Contains(material.GetInstanceId()))
        {
            Color tint = material is ShaderMaterial shader ? shader.GetShaderParameter("tint").AsColor()
                : material is BaseMaterial3D standard && standard.AlbedoTexture == null ? standard.AlbedoColor : new Color(0.4f, 0.4f, 0.4f);
            if (!Muted(tint))
            {
                why = NameOf(material) + " is too bright or saturated (" + tint.ToHtml(false) + ")";
                return false;
            }
            return true;
        }
        if (material is ShaderMaterial native && native.Shader != null && System.Array.IndexOf(NativeShaders, native.Shader.ResourcePath) >= 0)
        {
            return true;
        }
        if (material is BaseMaterial3D generated && generated.VertexColorUseAsAlbedo && generated.AlbedoTexture == null
            && (generated.ResourcePath.StartsWith("res://art/environment/forest/") || generated.ResourcePath.Length == 0) && generated.AlbedoColor.V >= 0.99f)
        {
            // The generated grass tufts and the terrain's vertex-coloured ground (colours set by the generator).
            return true;
        }
        why = "unlisted material " + material.GetType().Name + " '" + material.ResourceName + "' " + material.ResourcePath;
        return false;
    }

    /// <summary>A palette colour: not too saturated and not too bright (sRGB HSV).</summary>
    public static bool Muted(Color colour)
    {
        return colour.S <= MaxSaturation + 0.001f && colour.V <= MaxValue + 0.001f;
    }

    /// <summary>A colour brought into the palette: much less saturated, never glaring white, never pure black.</summary>
    public static Color Mute(Color colour)
    {
        float s = Mathf.Min(colour.S * 0.5f, 0.42f);
        float v = Mathf.Clamp(colour.V * 0.72f, 0.07f, 0.62f);
        Color muted = Color.FromHsv(colour.H, s, v);
        if (colour.S < 0.12f)
        {
            // Greys and whites lean a little warm, like wool, bone and weathered paint.
            muted = new Color(v, v * 0.96f, v * 0.88f);
        }
        return muted;
    }

    /// <summary>Which look a model's material becomes, from its name (the art packs name theirs), its model's file and its colour.</summary>
    public static Look LookOf(string materialName, string modelPath, out float atlasStrength)
    {
        atlasStrength = 0.0f;
        string name = materialName.ToLowerInvariant();
        string file = modelPath.GetFile().ToLowerInvariant();
        bool mushroom = file.Contains("mushroom");
        if (name.StartsWith("mi_"))
        {
            atlasStrength = 0.5f;
            if (name.Contains("plaster")) { return Look.Plaster; }
            if (name.Contains("brick") || name.Contains("rock") || name.Contains("stone")) { return Look.Stone; }
            if (name.Contains("metal")) { return Look.Metal; }
            if (name.Contains("vine") || name.Contains("leaf")) { return Look.FoliageDark; }
            return Look.Wood;
        }
        switch (name)
        {
            case "grass":
            case "leafsgreen":
            case "corn":
                return Look.Foliage;
            case "leafsdark":
                return Look.FoliageDark;
            case "leafsfall":
                return Look.FoliageFall;
            case "dirt":
            case "dirtdark":
                return Look.Earth;
            case "stone":
            case "stonedark":
                return Look.Stone;
            case "woodbark":
            case "woodbarkdark":
            case "woodbirch":
                return Look.Bark;
            case "wood":
            case "wooddark":
            case "woodinner":
                return Look.Wood;
            case "water":
                return Look.Water;
            case "colorred":
            case "colorreddark":
                return mushroom ? Look.CapRed : file.Contains("tent") ? Look.Cloth : Look.Rose;
            case "colortan":
                return Look.CapTan;
            case "colorpurple":
                return Look.Heather;
            case "coloryellow":
                return Look.Ochre;
            case "colorwhite":
                return Look.Pale;
            case "_defaultmat":
                return mushroom ? Look.Stem : file.Contains("statue") || file.Contains("column") ? Look.Stone : Look.Pale;
        }
        // The colour-atlas kits (graveyard, castle, fantasy town, survival): one look per model, by what it is.
        atlasStrength = 0.6f;
        return RoleOf(file);
    }

    /// <summary>What an atlas-coloured model is made of, from its file name.</summary>
    public static Look RoleOf(string file)
    {
        string[] stone = { "stone", "wall", "grave", "altar", "pillar", "tower", "rocks", "column", "statue", "fountain", "crypt", "brick", "debris.glb", "rock" };
        string[] cloth = { "tent", "bedroll", "flag", "banner", "pennant", "canvas", "stall-red", "stall-green" };
        string[] metal = { "lantern", "shovel", "axe", "pick", "iron", "metal", "bottle", "canister", "pan", "pot" };
        string[] wax = { "candle" };
        foreach (string word in wax) { if (file.Contains(word)) { return Look.Wax; } }
        foreach (string word in cloth) { if (file.Contains(word)) { return Look.Cloth; } }
        foreach (string word in metal) { if (file.Contains(word)) { return Look.Metal; } }
        foreach (string word in stone) { if (file.Contains(word)) { return Look.Stone; } }
        return Look.Wood;
    }

    /// <summary>The material a model's surface is drawn with: the generated plants keep theirs; everything else its palette look.</summary>
    public static Material StyleFor(Material? source, string modelPath)
    {
        if (source != null && IsInStyle(source, out _))
        {
            return source;
        }
        string name = source?.ResourceName ?? "";
        Look look = LookOf(name, modelPath, out float atlasStrength);
        Texture2D? atlas = null;
        if (atlasStrength > 0.0f && source is BaseMaterial3D standard && standard.AlbedoTexture != null)
        {
            atlas = standard.AlbedoTexture;
        }
        else
        {
            atlasStrength = 0.0f;
        }
        return Natural(look, atlas, atlasStrength);
    }

    /// <summary>A copy of a mesh with every surface in the palette (made once per mesh; batched plants use it).</summary>
    public static Mesh Styled(Mesh mesh, string modelPath)
    {
        ulong id = mesh.GetInstanceId();
        if (StyledMeshes.TryGetValue(id, out Mesh? styled))
        {
            return styled;
        }
        bool needed = false;
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            if (!IsInStyle(mesh.SurfaceGetMaterial(i), out _)) { needed = true; }
        }
        styled = mesh;
        if (needed && mesh is ArrayMesh source)
        {
            ArrayMesh copy = (ArrayMesh)source.Duplicate();
            for (int i = 0; i < copy.GetSurfaceCount(); i++)
            {
                copy.SurfaceSetMaterial(i, StyleFor(source.SurfaceGetMaterial(i), modelPath));
            }
            styled = copy;
        }
        StyledMeshes[id] = styled;
        return styled;
    }

    /// <summary>Draws every mesh of an instanced model with the palette (set pieces, station props, signs).</summary>
    public static void Restyle(Node root, string modelPath)
    {
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (mesh.Mesh == null) { continue; }
            if (mesh.MaterialOverride != null)
            {
                if (!IsInStyle(mesh.MaterialOverride, out _)) { mesh.MaterialOverride = StyleFor(mesh.MaterialOverride, modelPath); }
                continue;
            }
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                Material? current = mesh.GetSurfaceOverrideMaterial(i) ?? mesh.Mesh.SurfaceGetMaterial(i);
                if (!IsInStyle(current, out _)) { mesh.SetSurfaceOverrideMaterial(i, StyleFor(current, modelPath)); }
            }
        }
    }

    /// <summary>
    /// An animal in the mountain's style: smooth shading instead of paper facets, and each of its
    /// colours (wool, hide, nose, wing) muted into a fur coat with a fine grain, lit like the terrain.
    /// </summary>
    public static void RestyleCreature(Node root)
    {
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (mesh.Mesh == null) { continue; }
            Mesh source = mesh.Mesh;
            mesh.Mesh = Smooth(source);
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                Material? current = mesh.GetSurfaceOverrideMaterial(i) ?? source.SurfaceGetMaterial(i);
                mesh.SetSurfaceOverrideMaterial(i, Coat(current));
            }
            if (mesh.MaterialOverride != null && !IsInStyle(mesh.MaterialOverride, out _))
            {
                mesh.MaterialOverride = Coat(mesh.MaterialOverride);
            }
        }
    }

    /// <summary>A fur coat from an animal's own colour (muted), with a fine grain in the animal's own space.</summary>
    public static Material Coat(Material? source)
    {
        if (source != null && Registered.Contains(source.GetInstanceId())) { return source; }
        Color colour = new(0.5f, 0.45f, 0.4f);
        Texture2D? atlas = null;
        if (source is BaseMaterial3D standard)
        {
            colour = standard.AlbedoColor;
            atlas = standard.AlbedoTexture;
        }
        Color tint = atlas != null ? new Color(0.5f, 0.45f, 0.4f) : Mute(colour);
        string key = "coat|" + tint.ToHtml(false) + "|" + (atlas?.GetInstanceId() ?? 0);
        if (Materials.TryGetValue(key, out Material? cached)) { return cached; }
        _furTexture ??= FurTexture();
        Material coat = Make(tint, false, 0.35f, 0.4f, 0.95f, 0.0f, atlas, atlas != null ? 0.8f : 0.0f, true, "coat", _furTexture, 0.85f);
        Materials[key] = coat;
        return coat;
    }

    /// <summary>
    /// A fur coat of this colour (muted), with rosettes when <paramref name="spots"/>. <paramref name="tile"/>
    /// is the pattern's repeat in the model's own units (a skinned model is often imported tiny and
    /// scaled up): 0 means metres.
    /// </summary>
    public static Material CoatOf(Color colour, bool spots, float tile = 0.0f)
    {
        Color tint = Mute(colour);
        string key = "coatof|" + tint.ToHtml(false) + "|" + spots + "|" + tile.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        if (Materials.TryGetValue(key, out Material? cached)) { return cached; }
        Texture2D texture = spots ? _spotTexture ??= SpotTexture() : _furTexture ??= FurTexture();
        float metres = tile > 0.0f ? tile : spots ? 0.5f : 0.35f;
        Material coat = Make(tint, false, metres, spots ? 0.75f : 0.4f, 0.95f, 0.0f, null, 0.0f, true, "coat", texture, spots ? 0.95f : 0.85f);
        Materials[key] = coat;
        return coat;
    }

    /// <summary>
    /// A soft, lumpy ball (a mane, a tuft): a sphere whose surface is pushed in and out a little, so it
    /// reads as a mass of fur. Smooth-shaded.
    /// </summary>
    public static Mesh Lump(float radius, int seed)
    {
        SphereMesh sphere = new() { Radius = radius, Height = radius * 2.0f, RadialSegments = 18, Rings = 10 };
        Godot.Collections.Array arrays = sphere.GetMeshArrays();
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            float bump = 1.0f + 0.12f * Mathf.Sin(v.X / radius * 7.0f + seed) * Mathf.Sin(v.Y / radius * 5.0f + seed * 0.7f) * Mathf.Cos(v.Z / radius * 6.0f);
            vertices[i] = v * bump;
            normals[i] = v.Normalized();
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Tangent] = default;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Rosettes: dark rings and blots on a light ground (a cellular noise's cell edges).</summary>
    private static Texture2D SpotTexture()
    {
        FastNoiseLite noise = new()
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = 0.045f,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance,
            FractalType = FastNoiseLite.FractalTypeEnum.None,
        };
        Gradient ramp = new();
        ramp.SetColor(0, new Color(0.35f, 0.33f, 0.31f));
        ramp.SetOffset(0, 0.0f);
        ramp.SetColor(1, Colors.White);
        ramp.SetOffset(1, 0.45f);
        return new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = noise, ColorRamp = ramp };
    }

    private static Texture2D FurTexture()
    {
        FastNoiseLite noise = new() { Frequency = 0.06f, FractalOctaves = 4, FractalGain = 0.55f };
        Gradient ramp = new();
        ramp.SetColor(0, new Color(0.72f, 0.7f, 0.67f));
        ramp.SetColor(1, Colors.White);
        return new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = noise, ColorRamp = ramp };
    }

    /// <summary>
    /// The same mesh with smooth normals: every corner gets the average normal of all faces that
    /// share its position, so a low-poly animal reads as one rounded body instead of folded paper.
    /// Skinning (bones and weights) is kept; made once per mesh.
    /// </summary>
    public static Mesh Smooth(Mesh mesh)
    {
        ulong id = mesh.GetInstanceId();
        if (SmoothMeshes.TryGetValue(id, out Mesh? smooth)) { return smooth; }
        smooth = mesh;
        if (mesh is ArrayMesh source && source.GetBlendShapeCount() == 0)
        {
            ArrayMesh copy = new();
            bool ok = true;
            for (int i = 0; i < source.GetSurfaceCount(); i++)
            {
                if (source.SurfaceGetPrimitiveType(i) != Mesh.PrimitiveType.Triangles) { ok = false; break; }
                Godot.Collections.Array arrays = source.SurfaceGetArrays(i);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                if (normals.Length != vertices.Length) { ok = false; break; }
                Dictionary<Vector3I, Vector3> sums = new();
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3I key = (Vector3I)(vertices[v] * 2000.0f).Round();
                    sums[key] = (sums.TryGetValue(key, out Vector3 sum) ? sum : Vector3.Zero) + normals[v];
                }
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 sum = sums[(Vector3I)(vertices[v] * 2000.0f).Round()];
                    if (sum.LengthSquared() > 0.000001f) { normals[v] = sum.Normalized(); }
                }
                arrays[(int)Mesh.ArrayType.Normal] = normals;
                // Tangents follow the old normals; the coat has no normal map, so they aren't needed.
                arrays[(int)Mesh.ArrayType.Tangent] = default;
                Mesh.ArrayFormat format = source.SurfaceGetFormat(i);
                Mesh.ArrayFormat flags = format & Mesh.ArrayFormat.FlagUse8BoneWeights;
                copy.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
                copy.SurfaceSetMaterial(i, source.SurfaceGetMaterial(i));
                copy.SurfaceSetName(i, source.SurfaceGetName(i));
            }
            if (ok)
            {
                smooth = copy;
            }
        }
        SmoothMeshes[id] = smooth;
        return smooth;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        if (root is MeshInstance3D self) { yield return self; }
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh) { yield return mesh; }
        }
    }

    /// <summary>Kinds of placed thing, for the style and collision tests (and the report's table).</summary>
    public static IReadOnlyCollection<string> PaletteNames
    {
        get
        {
            List<string> names = new() { "rock" };
            foreach (Look look in Palette.Keys) { names.Add(look.ToString().ToLowerInvariant()); }
            names.Add("coat");
            return names;
        }
    }
}
