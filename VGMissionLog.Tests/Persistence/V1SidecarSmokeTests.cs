using System.IO;
using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;
using Xunit;

namespace VGMissionLog.Tests.Persistence;

public class V1SidecarSmokeTests
{
    private static string FixturePath =>
        Path.Combine("Fixtures", "sample-v1.vgmissionlog.json");

    [Fact]
    public void RealV1Sidecar_MigratesWithoutError()
    {
        var raw = File.ReadAllText(FixturePath);
        var v3  = V1ToV3Migrator.Migrate(raw);

        Assert.Equal(LogSchema.CurrentVersion, v3.Version);
        Assert.NotEmpty(v3.Missions);

        foreach (var m in v3.Missions)
        {
            Assert.NotEmpty(m.Timeline);
            Assert.Equal(TimelineState.Accepted, m.Timeline[0].State);
            Assert.False(string.IsNullOrEmpty(m.MissionInstanceId));
        }
    }

    [Fact]
    public void RealV1Sidecar_AllMigratedMissionsHaveConsistentIdentity()
    {
        var raw = File.ReadAllText(FixturePath);
        var v3  = V1ToV3Migrator.Migrate(raw);

        // Every mission should have a non-empty subclass and non-null level
        foreach (var m in v3.Missions)
        {
            Assert.False(string.IsNullOrEmpty(m.MissionSubclass));
            Assert.True(m.MissionLevel >= 0);
        }

        // No duplicate MissionInstanceId
        var ids = v3.Missions.Select(m => m.MissionInstanceId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
