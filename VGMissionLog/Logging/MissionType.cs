namespace VGMissionLog.Logging;

/// <summary>
/// Classified category of a mission. Six well-known kinds plus an open
/// <c>ThirdParty(prefix)</c> variant for missions authored by other mods
/// (e.g. <c>ThirdParty("vganima")</c>).
///
/// Modeled as a value-type record so structural equality works out of the
/// box (<c>evt.MissionType == MissionType.Bounty</c>) and Newtonsoft
/// serializes it as a flat <c>{ kind, prefix }</c> object — no
/// <c>TypeNameHandling.Auto</c> or custom binder needed (spec R3.7).
/// </summary>
public readonly record struct MissionType(string Kind, string? Prefix = null)
{
    public static readonly MissionType Bounty   = new("Bounty");
    public static readonly MissionType Patrol   = new("Patrol");
    public static readonly MissionType Industry = new("Industry");
    public static readonly MissionType Story    = new("Story");
    public static readonly MissionType Generic  = new("Generic");

    public static MissionType ThirdParty(string prefix) =>
        new("ThirdParty", prefix);

    public bool IsThirdParty => Kind == "ThirdParty";

    public override string ToString() =>
        Prefix is null ? Kind : $"{Kind}({Prefix})";
}
