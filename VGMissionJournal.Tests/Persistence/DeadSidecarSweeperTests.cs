using System;
using System.IO;
using System.Linq;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

public class DeadSidecarSweeperTests : IDisposable
{
    private readonly string _tmpDir;

    public DeadSidecarSweeperTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "vgmissionjournal-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private string FilePath(string name) => Path.Combine(_tmpDir, name);
    private void   Touch(string name)    => File.WriteAllText(FilePath(name), "");

    [Fact]
    public void Sweep_NonExistentDir_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(_tmpDir, "does-not-exist");

        Assert.Empty(DeadSidecarSweeper.Sweep(nonExistent));
    }

    [Fact]
    public void Sweep_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(DeadSidecarSweeper.Sweep(null!));
        Assert.Empty(DeadSidecarSweeper.Sweep(""));
    }

    [Fact]
    public void Sweep_EmptyDir_ReturnsEmpty()
    {
        Assert.Empty(DeadSidecarSweeper.Sweep(_tmpDir));
    }

    [Fact]
    public void Sweep_PairedSidecar_IsPreserved()
    {
        Touch("Alpha.save");
        Touch("Alpha.save.vgmissionjournal.json");

        var deleted = DeadSidecarSweeper.Sweep(_tmpDir);

        Assert.Empty(deleted);
        Assert.True(File.Exists(FilePath("Alpha.save.vgmissionjournal.json")));
    }

    [Fact]
    public void Sweep_OrphanSidecar_IsDeleted()
    {
        Touch("Orphan.save.vgmissionjournal.json");   // no paired .save

        var deleted = DeadSidecarSweeper.Sweep(_tmpDir);

        Assert.Single(deleted);
        Assert.Contains("Orphan.save.vgmissionjournal.json", deleted[0]);
        Assert.False(File.Exists(FilePath("Orphan.save.vgmissionjournal.json")));
    }

    [Fact]
    public void Sweep_QuarantineFile_IsNeverDeleted()
    {
        // Quarantine files carry forensic value — even if the paired save
        // is long gone, keep them around for post-mortem inspection.
        Touch("Beta.save.vgmissionjournal.corrupt.20260101000000.json");

        var deleted = DeadSidecarSweeper.Sweep(_tmpDir);

        Assert.Empty(deleted);
        Assert.True(File.Exists(FilePath("Beta.save.vgmissionjournal.corrupt.20260101000000.json")));
    }

    [Fact]
    public void Sweep_PeerSidecars_AreIgnored()
    {
        // Peer mods' sidecars share the save dir but aren't ours.
        Touch("Gamma.save.peermod.json");
        Touch("Gamma.save");

        var deleted = DeadSidecarSweeper.Sweep(_tmpDir);

        Assert.Empty(deleted);
        Assert.True(File.Exists(FilePath("Gamma.save.peermod.json")));
    }

    [Fact]
    public void Sweep_MixedState_DeletesOnlyOrphanLive()
    {
        Touch("Paired.save");
        Touch("Paired.save.vgmissionjournal.json");
        Touch("Orphan.save.vgmissionjournal.json");
        Touch("Quarantine.save.vgmissionjournal.corrupt.20260101000000.json");
        Touch("PeerMod.save.peermod.json"); // no .save; still must not touch

        var deleted = DeadSidecarSweeper.Sweep(_tmpDir);

        Assert.Single(deleted);
        Assert.Contains("Orphan.save.vgmissionjournal.json", deleted[0]);
        // Everything else stays.
        Assert.True(File.Exists(FilePath("Paired.save.vgmissionjournal.json")));
        Assert.True(File.Exists(FilePath("Quarantine.save.vgmissionjournal.corrupt.20260101000000.json")));
        Assert.True(File.Exists(FilePath("PeerMod.save.peermod.json")));
    }
}
