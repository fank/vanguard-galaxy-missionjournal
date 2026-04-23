using System;
using VGMissionLog.Persistence;
using Xunit;

namespace VGMissionLog.Tests.Persistence;

public class LogPathResolverTests
{
    [Fact]
    public void From_StandardSavePath_AppendsSuffix()
    {
        Assert.Equal("MySave.save.vgmissionlog.json",
            LogPathResolver.From("MySave.save"));
    }

    [Fact]
    public void From_NonSaveExtension_StillAppendsSuffix()
    {
        // Spec: "Handles .save vs non-.save inputs." — the resolver shouldn't
        // assert any particular vanilla extension.
        Assert.Equal("someRandomPath.vgmissionlog.json",
            LogPathResolver.From("someRandomPath"));
    }

    [Fact]
    public void From_AlreadyHasSuffix_IsIdempotent()
    {
        var once  = LogPathResolver.From("MySave.save");
        var twice = LogPathResolver.From(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void From_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LogPathResolver.From(null!));
    }

    [Fact]
    public void IsSidecar_LiveSidecar_IsTrue()
    {
        Assert.True(LogPathResolver.IsSidecar("MySave.save.vgmissionlog.json"));
    }

    [Fact]
    public void IsSidecar_QuarantineFile_IsFalse()
    {
        // Quarantine files carry forensic value but must not be swept by
        // DeadSidecarSweeper; IsSidecar returns false so sweep skips them.
        Assert.False(LogPathResolver.IsSidecar(
            "MySave.save.vgmissionlog.corrupt.20260423230000.json"));
    }

    [Fact]
    public void IsSidecar_UnrelatedFile_IsFalse()
    {
        Assert.False(LogPathResolver.IsSidecar("MySave.save"));
        Assert.False(LogPathResolver.IsSidecar("MySave.save.peermod.json")); // a peer mod's sidecar
        Assert.False(LogPathResolver.IsSidecar(""));
    }

    [Fact]
    public void BaseSavePathFrom_StripsSuffix()
    {
        Assert.Equal("MySave.save",
            LogPathResolver.BaseSavePathFrom("MySave.save.vgmissionlog.json"));
    }

    [Fact]
    public void BaseSavePathFrom_NoSuffix_ReturnsInputUnchanged()
    {
        Assert.Equal("not-a-sidecar.txt",
            LogPathResolver.BaseSavePathFrom("not-a-sidecar.txt"));
    }

    [Fact]
    public void QuarantineName_InsertsUtcTimestampBeforeJsonSuffix()
    {
        var ts = new DateTime(2026, 4, 23, 23, 0, 0, DateTimeKind.Utc);
        var q  = LogPathResolver.QuarantineName("MySave.save.vgmissionlog.json", ts);

        Assert.Equal("MySave.save.vgmissionlog.corrupt.20260423230000.json", q);
    }

    [Fact]
    public void QuarantineName_NonSidecarInput_StillProducesQuarantineFilename()
    {
        // Defensive — if the caller hands in the vanilla save path by mistake,
        // we should still produce something that won't collide.
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var q  = LogPathResolver.QuarantineName("MySave.save", ts);

        Assert.Equal("MySave.save.vgmissionlog.corrupt.20260101000000.json", q);
    }

    [Fact]
    public void RoundTrip_From_BaseSavePathFrom_IsIdentity()
    {
        Assert.Equal("MySave.save",
            LogPathResolver.BaseSavePathFrom(LogPathResolver.From("MySave.save")));
    }
}
