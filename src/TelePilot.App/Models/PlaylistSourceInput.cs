namespace TelePilot.App.Models;

public sealed record PlaylistSourceInput(string Name, Uri Address, bool RefreshOnStartup);
