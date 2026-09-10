namespace TelePilot.Epg.Models;

public sealed record EpgEpisodeNumber
{
    public EpgEpisodeNumber(string value, string? system = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value.Trim();
        System = string.IsNullOrWhiteSpace(system) ? null : system.Trim();
    }

    public string Value { get; }

    public string? System { get; }
}

public sealed record EpgCredit
{
    public EpgCredit(string role, string name, string? characterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Role = role.Trim();
        Name = name.Trim();
        CharacterName = string.IsNullOrWhiteSpace(characterName) ? null : characterName.Trim();
    }

    public string Role { get; }

    public string Name { get; }

    public string? CharacterName { get; }
}

public sealed record EpgRating
{
    public EpgRating(string value, string? system = null, IEnumerable<EpgIcon>? icons = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value.Trim();
        System = string.IsNullOrWhiteSpace(system) ? null : system.Trim();
        Icons = Array.AsReadOnly(icons?.ToArray() ?? []);
    }

    public string Value { get; }

    public string? System { get; }

    public IReadOnlyList<EpgIcon> Icons { get; }
}
