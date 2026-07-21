using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Core;

namespace WorldEngine.UI.Rendering;

/// <summary>
/// SpriteBatch-drawn legend for the active overlay type, displayed in the lower-left corner
/// of the map area. Shows a gradient bar for gradient overlays (Elevation, Temperature, Moisture,
/// MagicIntensity) or a column of color swatches for discrete overlays (Biome, Resources).
/// Territory overlay is skipped (would need live civ data).
/// </summary>
public sealed class OverlayLegend : IDisposable
{
    private Texture2D? _pixel;
    private const int SwatchSize = 12;           // px per discrete color swatch
    private const int GradientWidth = 12;        // px width of gradient bar
    private const int GradientHeight = 80;       // px height of gradient bar
    private const int LegendPadding = 10;        // px inset from left/bottom edges
    private const int SwatchSpacing = 2;         // px gap between swatches
    private const int BackgroundAlpha = 160;     // semi-transparent background

    /// <summary>Call once after GraphicsDevice is available.</summary>
    public void Initialize(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Draws the overlay legend into the SpriteBatch.
    /// Call within a SpriteBatch.Begin() / End() block.
    /// </summary>
    public void Draw(SpriteBatch sb, Rectangle mapRect, OverlayType activeOverlay)
    {
        if (_pixel is null) return;

        // Skip Territory overlay (would require live civ data)
        if (activeOverlay == OverlayType.Territory) return;

        switch (activeOverlay)
        {
            case OverlayType.Elevation or OverlayType.Temperature or OverlayType.Moisture or OverlayType.MagicIntensity:
                DrawGradientLegend(sb, mapRect, activeOverlay);
                break;

            case OverlayType.Biome:
                DrawBiomeLegend(sb, mapRect);
                break;

            case OverlayType.Resources:
                DrawResourcesLegend(sb, mapRect);
                break;
        }
    }

    private void DrawGradientLegend(SpriteBatch sb, Rectangle mapRect, OverlayType overlay)
    {
        if (_pixel is null) return;

        int x = mapRect.X + LegendPadding;
        int y = mapRect.Bottom - LegendPadding - GradientHeight;

        // Semi-transparent background
        var bgRect = new Rectangle(x - 2, y - 2, GradientWidth + 4, GradientHeight + 4);
        sb.Draw(_pixel, bgRect, Color.Black * (BackgroundAlpha / 255f));

        // Draw gradient bar by sampling at discrete steps
        int steps = GradientHeight;
        for (int i = 0; i < steps; i++)
        {
            float t = 1f - (i / (float)steps); // top = high, bottom = low
            byte v = (byte)(t * 255);
            Color color = overlay switch
            {
                OverlayType.Elevation => Greyscale(v),
                OverlayType.Temperature => TempGradient(v),
                OverlayType.Moisture => MoistureGradient(v),
                OverlayType.MagicIntensity => MagicGradient(v),
                _ => Color.Magenta
            };

            var pixelRect = new Rectangle(x, y + i, GradientWidth, 1);
            sb.Draw(_pixel, pixelRect, color);
        }

        // Border
        sb.Draw(_pixel, new Rectangle(x, y, GradientWidth, 1), Color.White * 0.6f);              // top
        sb.Draw(_pixel, new Rectangle(x, y + GradientHeight - 1, GradientWidth, 1), Color.White * 0.6f); // bottom
        sb.Draw(_pixel, new Rectangle(x, y, 1, GradientHeight), Color.White * 0.6f);              // left
        sb.Draw(_pixel, new Rectangle(x + GradientWidth - 1, y, 1, GradientHeight), Color.White * 0.6f); // right
    }

    private void DrawBiomeLegend(SpriteBatch sb, Rectangle mapRect)
    {
        if (_pixel is null) return;

        // Biome swatches: Ocean, TemperateForest, Grassland, Desert, Mountain, Tundra
        BiomeType[] biomesToShow =
        [
            BiomeType.Ocean,
            BiomeType.TemperateForest,
            BiomeType.Grassland,
            BiomeType.Desert,
            BiomeType.Mountain,
            BiomeType.Tundra
        ];

        int x = mapRect.X + LegendPadding;
        int y = mapRect.Bottom - LegendPadding - (biomesToShow.Length * (SwatchSize + SwatchSpacing));

        // Semi-transparent background
        int bgHeight = biomesToShow.Length * SwatchSize + (biomesToShow.Length - 1) * SwatchSpacing;
        var bgRect = new Rectangle(x - 2, y - 2, SwatchSize + 4, bgHeight + 4);
        sb.Draw(_pixel, bgRect, Color.Black * (BackgroundAlpha / 255f));

        // Draw swatches
        for (int i = 0; i < biomesToShow.Length; i++)
        {
            Color biomeColor = GetBiomeColor(biomesToShow[i]);
            int swatchY = y + i * (SwatchSize + SwatchSpacing);
            var swatchRect = new Rectangle(x, swatchY, SwatchSize, SwatchSize);
            sb.Draw(_pixel, swatchRect, biomeColor);

            // Border
            DrawRectBorder(sb, swatchRect, Color.White * 0.4f, 1);
        }
    }

    private void DrawResourcesLegend(SpriteBatch sb, Rectangle mapRect)
    {
        if (_pixel is null) return;

        // Resource swatches: RareResource (purple), Deposit (yellow), None (grey)
        (string Label, Color Color)[] resources =
        [
            ("Rare", new Color(180, 0, 220)),
            ("Deposit", Color.Yellow),
            ("None", new Color(100, 100, 100))
        ];

        int x = mapRect.X + LegendPadding;
        int y = mapRect.Bottom - LegendPadding - (resources.Length * (SwatchSize + SwatchSpacing));

        // Semi-transparent background
        int bgHeight = resources.Length * SwatchSize + (resources.Length - 1) * SwatchSpacing;
        var bgRect = new Rectangle(x - 2, y - 2, SwatchSize + 4, bgHeight + 4);
        sb.Draw(_pixel, bgRect, Color.Black * (BackgroundAlpha / 255f));

        // Draw swatches
        for (int i = 0; i < resources.Length; i++)
        {
            Color color = resources[i].Color;
            int swatchY = y + i * (SwatchSize + SwatchSpacing);
            var swatchRect = new Rectangle(x, swatchY, SwatchSize, SwatchSize);
            sb.Draw(_pixel, swatchRect, color);

            // Border
            DrawRectBorder(sb, swatchRect, Color.White * 0.4f, 1);
        }
    }

    private void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness)
    {
        if (_pixel is null) return;

        // Top
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        // Bottom
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        // Left
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        // Right
        sb.Draw(_pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    // ── Color functions (copied from OverlayRenderer for consistency) ──────────

    private static Color GetBiomeColor(BiomeType b) => b switch
    {
        BiomeType.Ocean            => new Color(0, 50, 160),
        BiomeType.CoastalWater     => new Color(65, 125, 210),
        BiomeType.Beach            => new Color(238, 214, 175),
        BiomeType.Tundra           => new Color(200, 210, 215),
        BiomeType.BorealForest     => new Color(30, 80, 50),
        BiomeType.TemperateForest  => new Color(50, 130, 60),
        BiomeType.TropicalRainforest => new Color(20, 180, 50),
        BiomeType.Grassland        => new Color(140, 195, 80),
        BiomeType.Savanna          => new Color(190, 175, 90),
        BiomeType.Desert           => new Color(220, 150, 60),
        BiomeType.Swamp            => new Color(80, 100, 50),
        BiomeType.HighMountain     => new Color(240, 240, 245),
        BiomeType.Mountain         => new Color(160, 155, 150),
        BiomeType.Hills            => new Color(155, 130, 90),
        BiomeType.Plains           => new Color(200, 195, 140),
        BiomeType.Volcanic         => new Color(140, 30, 20),
        _ => Color.Magenta
    };

    private static Color Greyscale(byte v) => new Color(v, v, v);

    private static Color TempGradient(byte v)
    {
        float t = v / 255f;
        return new Color((int)(t * 255), 0, (int)((1 - t) * 255));
    }

    private static Color MoistureGradient(byte v)
    {
        float t = v / 255f;
        return Color.Lerp(new Color(210, 185, 135), new Color(30, 90, 200), t);
    }

    private static Color MagicGradient(byte v)
    {
        float t = v / 255f;
        return Color.Lerp(Color.Black, new Color(180, 80, 255), t);
    }

    public void Dispose() => _pixel?.Dispose();
}
