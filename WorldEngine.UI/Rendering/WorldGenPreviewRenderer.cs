using Microsoft.Xna.Framework;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.UI.Rendering;

/// <summary>
/// Builds a per-tile thumbnail color buffer for a single worldgen layer, straight from the
/// in-progress <see cref="WorldGenContext"/> (before <see cref="TileGridAssembler"/> has run).
/// Reuses <see cref="OverlayRenderer.GetColor"/> — the same palette functions the live map
/// uses — by feeding it a minimal <see cref="TileDisplayData"/> built from whatever layer
/// results are available so far (per M10 10.1 design decision: don't fork the palette).
/// </summary>
public static class WorldGenPreviewRenderer
{
    private static readonly TileDisplayData Blank = new(
        default, 0, 0, 0, 0, 0, TileStaticFlags.None, default, false, false, Array.Empty<EntityId>());

    /// <summary>Returns per-tile colors for <paramref name="layerIndex"/>, or null if that layer hasn't run yet.</summary>
    public static Color[]? BuildLayerColors(WorldGenContext ctx, int layerIndex)
    {
        int n = ctx.TileCount;
        return layerIndex switch
        {
            0 => ctx.Tectonic is { } t ? BuildTectonic(t, n) : null,
            1 => ctx.Elevation is { } e ? BuildElevation(e, n) : null,
            2 => ctx.Ocean is { } o ? BuildOcean(o, n) : null,
            3 => ctx.River is { } r && ctx.Ocean is { } oc ? BuildRiver(r, oc, n) : null,
            4 => ctx.Magic is { } m ? BuildMagic(m, n) : null,
            5 => ctx.Climate is { } c ? BuildClimate(c, n) : null,
            6 => ctx.Biome is { } b ? BuildBiome(b, n) : null,
            7 => ctx.Resource is { } res && ctx.Biome is { } rb ? BuildResource(res, rb, ctx, n) : null,
            8 => ctx.Poi is { } p && ctx.Biome is { } pb ? BuildPoi(p, pb, n) : null,
            _ => null
        };
    }

    private static Color[] BuildElevation(ElevationResult e, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
            colors[i] = OverlayRenderer.GetColor(Blank with { Elevation = e.Elevation[i] }, OverlayType.Elevation);
        return colors;
    }

    private static Color[] BuildMagic(MagicResult m, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
            colors[i] = OverlayRenderer.GetColor(Blank with { MagicIntensity = m.MagicIntensity[i] }, OverlayType.MagicIntensity);
        return colors;
    }

    private static Color[] BuildClimate(ClimateResult c, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
            colors[i] = OverlayRenderer.GetColor(Blank with { EffectiveTemperature = c.BaseTemperature[i] }, OverlayType.Temperature);
        return colors;
    }

    private static Color[] BuildBiome(BiomeResult b, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
            colors[i] = OverlayRenderer.GetColor(Blank with { Biome = b.Biomes[i] }, OverlayType.Biome);
        return colors;
    }

    private static Color[] BuildOcean(OceanResult o, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            var biome = o.IsOcean[i] ? BiomeType.Ocean : o.IsCoastal[i] ? BiomeType.Beach : BiomeType.Plains;
            colors[i] = OverlayRenderer.GetColor(Blank with { Biome = biome }, OverlayType.Biome);
        }
        return colors;
    }

    private static Color[] BuildRiver(RiverResult r, OceanResult ocean, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            var biome = ocean.IsOcean[i] ? BiomeType.Ocean
                : r.IsLake[i] || r.HasRiver[i] ? BiomeType.CoastalWater
                : BiomeType.Plains;
            colors[i] = OverlayRenderer.GetColor(Blank with { Biome = biome }, OverlayType.Biome);
        }
        return colors;
    }

    private static Color[] BuildResource(ResourceResult res, BiomeResult biome, WorldGenContext ctx, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            var coord = new TileCoord(i % ctx.TileWidth, i / ctx.TileWidth);
            var flags = TileStaticFlags.None;
            if (res.Deposits.TryGetValue(coord, out var deposits))
            {
                flags |= TileStaticFlags.HasDeposit;
                if (deposits.Any(d => d.DepositType is "Obsidian" or "Gold"))
                    flags |= TileStaticFlags.HasRareResource;
            }
            colors[i] = OverlayRenderer.GetColor(Blank with { Biome = biome.Biomes[i], StaticFlags = flags }, OverlayType.Resources);
        }
        return colors;
    }

    private static Color[] BuildPoi(PoiResult poi, BiomeResult biome, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            colors[i] = poi.IsPOICandidate[i]
                ? Color.Gold
                : OverlayRenderer.GetColor(Blank with { Biome = biome.Biomes[i] }, OverlayType.Biome);
        }
        return colors;
    }

    private static Color[] BuildTectonic(TectonicResult t, int n)
    {
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            byte v = t.PlateId[i];
            colors[i] = t.IsVolcanic[i] ? new Color(220, 60, 20)
                : t.IsFaultLine[i] ? new Color(220, 140, 30)
                : new Color(v, v, v);
        }
        return colors;
    }
}
