using System;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

public class LogPathResolverTests
{
    [Fact]
    public void From_StandardSavePath_AppendsSuffix()
    {
        Assert.Equal("MySave.save.vgmissionjournal.json",
            JournalPathResolver.From("MySave.save"));
    }

    [Fact]
    public void From_NonSaveExtension_StillAppendsSuffix()
    {
        // Spec: "Handles .save vs non-.save inputs." — the resolver shouldn't
        // assert any particular vanilla extension.
        Assert.Equal("someRandomPath.vgmissionjournal.json",
            JournalPathResolver.From("someRandomPath"));
    }

    [Fact]
    public void From_AlreadyHasSuffix_IsIdempotent()
    {
        var once  = JournalPathResolver.From("MySave.save");
        var twice = JournalPathResolver.From(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void From_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JournalPathResolver.From(null!));
    }

    [Fact]
    public void IsSidecar_LiveSidecar_IsTrue()
    {
        Assert.True(JournalPathResolver.IsSidecar("MySave.save.vgmissionjournal.json"));
    }

    [Fact]
    public void IsSidecar_QuarantineFile_IsFalse()
    {
        // Quarantine files carry forensic value but must not be swept by
        // DeadSidecarSweeper; IsSidecar returns false so sweep skips them.
        Assert.False(JournalPathResolver.IsSidecar(
            "MySave.save.vgmissionjournal.corrupt.20260423230000.json"));
    }

    [Fact]
    public void IsSidecar_UnrelatedFile_IsFalse()
    {
        Assert.False(JournalPathResolver.IsSidecar("MySave.save"));
        Assert.False(JournalPathResolver.IsSidecar("MySave.save.peermod.json")); // a peer mod's sidecar
        Assert.False(JournalPathResolver.IsSidecar(""));
    }

    [Fact]
    public void BaseSavePathFrom_StripsSuffix()
    {
        Assert.Equal("MySave.save",
            JournalPathResolver.BaseSavePathFrom("MySave.save.vgmissionjournal.json"));
    }

    [Fact]
    public void BaseSavePathFrom_NoSuffix_ReturnsInputUnchanged()
    {
        Assert.Equal("not-a-sidecar.txt",
            JournalPathResolver.BaseSavePathFrom("not-a-sidecar.txt"));
    }

    [Fact]
    public void QuarantineName_InsertsUtcTimestampBeforeJsonSuffix()
    {
        var ts = new DateTime(2026, 4, 23, 23, 0, 0, DateTimeKind.Utc);
        var q  = JournalPathResolver.QuarantineName("MySave.save.vgmissionjournal.json", ts);

        Assert.Equal("MySave.save.vgmissionjournal.corrupt.20260423230000.json", q);
    }

    [Fact]
    public void QuarantineName_NonSidecarInput_StillProducesQuarantineFilename()
    {
        // Defensive — if the caller hands in the vanilla save path by mistake,
        // we should still produce something that won't collide.
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var q  = JournalPathResolver.QuarantineName("MySave.save", ts);

        Assert.Equal("MySave.save.vgmissionjournal.corrupt.20260101000000.json", q);
    }

    [Fact]
    public void RoundTrip_From_BaseSavePathFrom_IsIdentity()
    {
        Assert.Equal("MySave.save",
            JournalPathResolver.BaseSavePathFrom(JournalPathResolver.From("MySave.save")));
    }
}
