using static ToolFleet.Domain.Tests.Builders;

namespace ToolFleet.Domain.Tests;

public class FirmwareReleaseTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void Release_with_a_file_keeps_its_size_and_fingerprint()
    {
        var release = new FirmwareRelease("MWD Pulser", "4.2.0", "", Now, new FirmwareFile("mp-4.2.0.bin", 2048, Sha, "abc/mp-4.2.0.bin"));

        Assert.True(release.HasFile);
        Assert.Equal(2048, release.FileSizeBytes);
        Assert.Equal(Sha, release.FileSha256);
    }

    [Fact]
    public void Baseline_release_has_no_file()
    {
        Assert.False(Release().HasFile);
    }

    [Theory]
    [InlineData(0, Sha)]                     // empty file
    [InlineData(10, "not-a-hash")]           // not a SHA-256
    [InlineData(10, "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")] // upper case
    public void Bad_file_details_are_rejected(long size, string sha)
    {
        Assert.Throws<DomainException>(() =>
            new FirmwareRelease("MWD Pulser", "4.2.0", "", Now, new FirmwareFile("mp.bin", size, sha, "abc/mp.bin")));
    }
}
