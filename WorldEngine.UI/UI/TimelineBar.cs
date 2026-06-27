using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

/// <summary>
/// Timeline scrubber bar drawn via SpriteBatch at the bottom of the map area.
/// Shows event density heatmap and allows scrubbing to any historical year.
/// The ScrubLabel is a Myra Label — add it to the root overlay panel in Game1.
/// </summary>
public sealed class TimelineBar : IDisposable
{
    private Texture2D? _pixel;
    private Dictionary<int, int> _eventsByDecade = new();
    private int _maxBucketCount;
    private int _scrubYear = -1;

    /// <summary>Current scrub year, or -1 if not scrubbing.</summary>
    public int ScrubYear => _scrubYear;

    /// <summary>True while the player is actively scrubbing.</summary>
    public bool IsScrubbing => _scrubYear >= 0;

    /// <summary>
    /// Myra Label shown above the scrub handle. Add to the root overlay panel in Game1.
    /// Positioned via Left/Top in screen coordinates each Update call.
    /// </summary>
    public readonly Label ScrubLabel = new() { Text = "", Visible = false, TextColor = Color.White };

    /// <summary>Call once after GraphicsDevice is available.</summary>
    public void Initialize(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Loads event-count-per-decade data from IHistoryQuery for the density heatmap.
    /// Call whenever the sim year advances significantly (e.g. every 50 sim-years).
    /// </summary>
    public void LoadEventBuckets(IHistoryQuery history, int currentYear)
    {
        _eventsByDecade  = history.GetEventCountByDecade(1, currentYear);
        _maxBucketCount  = _eventsByDecade.Values.DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Processes mouse input within barRect. Call from Game1.HandleInput each frame.
    /// </summary>
    public void Update(int currentYear, MouseState mouse, MouseState prevMouse, Rectangle barRect)
    {
        if (barRect.Width <= 0 || currentYear <= 0) return;

        bool inBar = barRect.Contains(mouse.X, mouse.Y);

        if (mouse.LeftButton == ButtonState.Pressed && inBar)
        {
            float fraction = (float)(mouse.X - barRect.X) / barRect.Width;
            _scrubYear = Math.Clamp((int)(fraction * currentYear) + 1, 1, currentYear);

            // Position the Myra label above the scrub handle
            ScrubLabel.Text    = $"Year {_scrubYear}";
            ScrubLabel.Visible = true;
            ScrubLabel.Left    = mouse.X - 25;
            ScrubLabel.Top     = barRect.Y - 20;
        }
        else if (mouse.LeftButton == ButtonState.Released && prevMouse.LeftButton == ButtonState.Pressed)
        {
            _scrubYear         = -1;
            ScrubLabel.Visible = false;
        }
    }

    /// <summary>
    /// Draws the density heatmap bar and scrub handle into the SpriteBatch.
    /// Begin/End the SpriteBatch around this call.
    /// </summary>
    public void Draw(SpriteBatch sb, Rectangle barRect, int currentYear)
    {
        if (_pixel is null) return;

        // Background
        sb.Draw(_pixel, barRect, Color.DimGray * 0.85f);

        // Density heatmap — shade each decade bucket from light→dark based on event density
        if (_eventsByDecade.Count > 0 && currentYear > 0)
        {
            foreach (var (decade, count) in _eventsByDecade)
            {
                float x0  = (float)decade          / currentYear;
                float x1  = (float)(decade + 10)   / currentYear;
                int   px0 = barRect.X + (int)(x0 * barRect.Width);
                int   px1 = barRect.X + (int)(x1 * barRect.Width);
                int   w   = Math.Max(1, px1 - px0);

                float density = _maxBucketCount > 0 ? (float)count / _maxBucketCount : 0f;
                // Light gray (few events) → dark slate gray (many events)
                byte gray = (byte)(200 - (int)(density * 140));
                sb.Draw(_pixel, new Rectangle(px0, barRect.Y, w, barRect.Height), new Color(gray, gray, gray));
            }
        }

        // Scrub handle — white vertical line
        if (_scrubYear >= 0 && currentYear > 0)
        {
            float fraction = (float)(_scrubYear - 1) / currentYear;
            int   handleX  = barRect.X + (int)(fraction * barRect.Width);
            sb.Draw(_pixel, new Rectangle(handleX, barRect.Y, 2, barRect.Height), Color.White);
        }

        // Left/right border lines
        sb.Draw(_pixel, new Rectangle(barRect.X,              barRect.Y, 1, barRect.Height), Color.Gray);
        sb.Draw(_pixel, new Rectangle(barRect.Right - 1, barRect.Y, 1, barRect.Height), Color.Gray);
    }

    public void Dispose() => _pixel?.Dispose();
}
