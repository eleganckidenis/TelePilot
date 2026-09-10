namespace TelePilot.App.Accessibility;

public enum FocusTarget
{
    ChannelList,
    PlaylistSourceSelector,
    SearchBox,
    PlayerControls,
    PrimaryDialogAction,
    ProgramList,
}

public sealed record FocusRequest(
    FocusTarget Target,
    string? ItemId = null,
    bool PreserveExistingFocus = false);

public interface IFocusCoordinator
{
    event EventHandler<FocusRequest>? FocusRequested;

    void Request(
        FocusTarget target,
        string? itemId = null,
        bool preserveExistingFocus = false);
}

public sealed class FocusCoordinator : IFocusCoordinator
{
    public event EventHandler<FocusRequest>? FocusRequested;

    public void Request(
        FocusTarget target,
        string? itemId = null,
        bool preserveExistingFocus = false)
    {
        FocusRequested?.Invoke(this, new FocusRequest(target, itemId, preserveExistingFocus));
    }
}
