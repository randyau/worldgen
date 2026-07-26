using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned God Mode panel: paused-only authoring actions + 4 ModalHost dialogs (M8.3.5).
/// <summary>
/// God Mode panel — allows paused-only authoring actions: place artifact, trigger disaster,
/// spawn character, nudge character. Dialogs route through the shared <see cref="ModalHost"/>.
/// </summary>
public sealed class GodModePanel : IToggleablePanel
{
    private readonly ModalHost _modalHost;
    private readonly WeVStack _content = new(UiTheme.Space.Sm);
    private PanelContext _ctx;

    private TileCoord? _targetTileCoord;
    private EntityId?  _targetCharacterId;
    private string?    _targetCharacterName;
    private bool       _isPaused;

    public string Id => "godmode";
    public string Title => "God Mode";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public GodModePanel(ModalHost modalHost) => _modalHost = modalHost;

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    public void Refresh()
    {
        var snapshot = _ctx.Snapshot;
        _targetTileCoord     = snapshot.InspectedTile?.Coord;
        _targetCharacterId   = snapshot.WatchedCharacter?.Id;
        _targetCharacterName = snapshot.WatchedCharacter?.Name;
        _isPaused            = snapshot.IsPaused;

        _content.Clear();

        string statusText = _isPaused ? "Paused — ready" : "Pause to use God Mode";
        var statusColor = _isPaused ? UiTheme.ColorRole.StatePositive : UiTheme.ColorRole.StateWarning;
        _content.Add(new WeText($"Status: {statusText}", color: statusColor));

        var row1 = new WeHStack(UiTheme.Space.Xs);
        row1.Add(MakeButton("Place Artifact",   OpenPlaceArtifactDialog, _isPaused));
        row1.Add(MakeButton("Trigger Disaster", OpenTriggerDisasterDialog, _isPaused));
        _content.Add(row1);

        var row2 = new WeHStack(UiTheme.Space.Xs);
        row2.Add(MakeButton("Spawn Character", OpenSpawnCharacterDialog, _isPaused));
        row2.Add(MakeButton("Nudge Character", OpenNudgeCharacterDialog, _isPaused));
        _content.Add(row2);

        _content.Add(SectionHeader.Build("How to Use"));
        _content.Add(new WeText("  Space  — pause / resume", color: UiTheme.ColorRole.TextMuted));
        _content.Add(new WeText("  Click map tile → sets target for", color: UiTheme.ColorRole.TextMuted));
        _content.Add(new WeText("    Place Artifact / Trigger Disaster / Spawn", color: UiTheme.ColorRole.TextMuted));
        _content.Add(new WeText("  W → Watch panel → select char → Nudge", color: UiTheme.ColorRole.TextMuted));
    }

    // ── Private ─────────────────────────────────────────────────────────────

    private static WeButton MakeButton(string text, Action onClick, bool enabled)
    {
        var btn = new WeButton(text, onClick) { Width = 140, Enabled = enabled };
        return btn;
    }

    private bool CheckPaused() => _isPaused;

    private WeVStack DialogHeader(string targetLabel, string targetDesc)
    {
        var stack = new WeVStack(UiTheme.Space.Sm);
        stack.Add(new WeText($"{targetLabel}: {targetDesc}", color: UiTheme.ColorRole.TextSecondary));
        return stack;
    }

    private void AddConfirmCancel(WeVStack stack, Action onConfirm)
    {
        var btnRow = new WeHStack(UiTheme.Space.Sm);
        var confirm = new WeButton("Confirm", () => { onConfirm(); _modalHost.Close(); });
        var cancel  = new WeButton("Cancel", () => _modalHost.Close(), WeButtonVariant.Ghost);
        btnRow.Add(confirm);
        btnRow.Add(cancel);
        stack.Add(btnRow);
    }

    private void OpenPlaceArtifactDialog()
    {
        if (!CheckPaused()) return;

        string tileDesc = _targetTileCoord.HasValue ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})" : "(no tile selected)";
        var stack = DialogHeader("Target tile", tileDesc);

        var category = new WeDropdown<ArtifactCategory>();
        category.SetItems(Enum.GetValues<ArtifactCategory>());
        category.Selected = ArtifactCategory.Weapon;
        stack.Add(category);

        var nameField = new WeField("Name:", "Custom name (optional)");
        stack.Add(nameField);

        AddConfirmCancel(stack, () =>
        {
            if (_targetTileCoord.HasValue)
            {
                string? name = string.IsNullOrWhiteSpace(nameField.Value) ? null : nameField.Value.Trim();
                _ctx.Commands.Enqueue(new AuthorPlaceArtifact(_targetTileCoord.Value, category.Selected, name));
            }
        });

        _modalHost.Show(stack.Root);
    }

    private void OpenTriggerDisasterDialog()
    {
        if (!CheckPaused()) return;

        string tileDesc = _targetTileCoord.HasValue ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})" : "(no tile selected)";
        var stack = DialogHeader("Target tile", tileDesc);

        var disasterType = new WeDropdown<DisasterType>();
        disasterType.SetItems(Enum.GetValues<DisasterType>());
        disasterType.Selected = DisasterType.Wildfire;
        stack.Add(disasterType);

        AddConfirmCancel(stack, () =>
        {
            if (_targetTileCoord.HasValue)
                _ctx.Commands.Enqueue(new AuthorTriggerDisaster(_targetTileCoord.Value, disasterType.Selected));
        });

        _modalHost.Show(stack.Root);
    }

    private void OpenSpawnCharacterDialog()
    {
        if (!CheckPaused()) return;

        string tileDesc = _targetTileCoord.HasValue ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})" : "(no tile selected)";
        var stack = DialogHeader("Target tile", tileDesc);

        var ancestryField = new WeField("Ancestry ID:", "ancestry id or blank");
        stack.Add(ancestryField);

        AddConfirmCancel(stack, () =>
        {
            if (_targetTileCoord.HasValue)
            {
                string? ancestry = string.IsNullOrWhiteSpace(ancestryField.Value) ? null : ancestryField.Value.Trim();
                _ctx.Commands.Enqueue(new AuthorSpawnCharacter(_targetTileCoord.Value, ancestry));
            }
        });

        _modalHost.Show(stack.Root);
    }

    private void OpenNudgeCharacterDialog()
    {
        if (!CheckPaused()) return;

        string charDesc = _targetCharacterName ?? (_targetCharacterId.HasValue ? $"id {_targetCharacterId.Value.Value}" : "(none)");
        var stack = DialogHeader("Target", charDesc);

        var nudge = new WeDropdown<CharacterNudge>();
        nudge.SetItems(Enum.GetValues<CharacterNudge>());
        nudge.Selected = CharacterNudge.RaiseMorale;
        stack.Add(nudge);

        AddConfirmCancel(stack, () =>
        {
            if (_targetCharacterId.HasValue)
                _ctx.Commands.Enqueue(new AuthorNudgeCharacter(_targetCharacterId.Value, nudge.Selected));
        });

        _modalHost.Show(stack.Root);
    }
}
