using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Persistence;

/// <summary>
/// Top-level shape of a <c>&lt;save&gt;.save.vgmissionjournal.json</c> sidecar
/// at v3 (mission-oriented). The array is keyed by mission, not by event;
/// each element is a <see cref="MissionRecord"/> with identity + structure +
/// rewards + timeline.
///
/// <para>v1 sidecars migrate on load via <see cref="V1ToV3Migrator"/>;
/// writes are always v3. No legacy fields.</para>
/// </summary>
internal sealed record JournalSchema(
    [property: JsonProperty("version")]  int Version,
    [property: JsonProperty("missions")] MissionRecord[] Missions)
{
    public const int CurrentVersion = 3;

    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        ContractResolver  = new CamelCasePropertyNamesContractResolver(),
        Formatting        = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        // String enums on the wire. AllowIntegerValues intentionally left
        // at default (true) only because Newtonsoft's StringEnumConverter
        // treats it that way; we never emit integers, so no reader needs it.
        Converters        = { new StringEnumConverter() },
    };
}
