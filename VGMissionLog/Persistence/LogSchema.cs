using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using VGMissionLog.Logging;

namespace VGMissionLog.Persistence;

/// <summary>
/// Top-level shape of a <c>&lt;save&gt;.save.vgmissionlog.json</c>
/// sidecar. See spec R3.
///
/// <para>Versioning policy (R3.5):
/// <list type="bullet">
///   <item><b>v1</b>: initial shape — <c>version</c> + flat
///         <c>events[]</c> array of <see cref="ActivityEvent"/>. Shipped
///         version 0.1.0.</item>
///   <item>Additive additions (new nullable fields on events, new event
///         types) stay at v1 — readers tolerate unknown-to-old payloads
///         because <see cref="JsonSerializerSettings.MissingMemberHandling"/>
///         defaults to Ignore.</item>
///   <item>Breaking changes (field removal, semantic shifts) bump
///         <see cref="CurrentVersion"/>; <see cref="LogIO"/> quarantines
///         unsupported versions (or upgrades-on-read when additive).</item>
/// </list></para>
///
/// <para><b>Security:</b> VGMissionLog's records are all primitive /
/// nullable-primitive / <see cref="System.Collections.Generic.IReadOnlyList{T}"/>
/// of flat records, so <c>TypeNameHandling.Auto</c> is <b>not</b> enabled
/// and no <c>ISerializationBinder</c> is needed. That's a deliberate
/// simplification per R3.7 — sidecars travel with saves (cloud-sync,
/// share, modpacks) and removing the polymorphic-deserialization surface
/// removes a whole class of gadget-chain risk.</para>
/// </summary>
internal sealed record LogSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("events")]  ActivityEvent[] Events)
{
    public const int CurrentVersion = 1;

    /// <summary>Shared Newtonsoft settings for read/write. camelCase
    /// on the wire; null fields on events are omitted to keep the
    /// sidecar compact. Indented formatting is used for human-readable
    /// diffs when players share save bundles.</summary>
    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        ContractResolver  = new CamelCasePropertyNamesContractResolver(),
        Formatting        = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        // Enums as strings on the wire ("Accepted"/"Completed"/…) so external
        // tooling reading the sidecar sees human-readable values and the
        // on-disk shape matches what the public API dictionary exposes (via
        // ActivityEventMapper, which uses enum.ToString()). PascalCase matches
        // the C# enum names; AllowIntegerValues stays true (default) so older
        // v1 sidecars written with integer enums still round-trip on read.
        Converters        = { new StringEnumConverter() },
    };
}
