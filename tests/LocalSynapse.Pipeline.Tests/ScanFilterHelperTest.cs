using Xunit;
using LocalSynapse.Pipeline.Scanning;

namespace LocalSynapse.Pipeline.Tests;

public class ScanFilterHelperTest
{
    [Theory]
    [InlineData("Windows")]
    [InlineData("node_modules")]
    [InlineData("$Recycle.Bin")]
    public void IsExcludedFolder_ExcludesSystemFolders(string folderName)
    {
        Assert.True(ScanFilterHelper.IsExcludedFolder(folderName));
    }

    [Theory]
    [InlineData("Documents")]
    [InlineData("Projects")]
    [InlineData("Reports")]
    public void IsExcludedFolder_AllowsNormalFolders(string folderName)
    {
        Assert.False(ScanFilterHelper.IsExcludedFolder(folderName));
    }

    [Fact]
    public void IsGuidOrHashFolder_DetectsGuidFolders()
    {
        Assert.True(ScanFilterHelper.IsGuidOrHashFolder("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
    }

    [Fact]
    public void IsGuidOrHashFolder_AllowsNormalFolders()
    {
        Assert.False(ScanFilterHelper.IsGuidOrHashFolder("My Documents"));
    }

    [Fact]
    public void ShouldSkipFile_SystemFile_ReturnsTrue()
    {
        Assert.True(ScanFilterHelper.ShouldSkipFile(FileAttributes.System, 1000));
    }

    [Fact]
    public void ShouldSkipFile_TooSmall_ReturnsTrue()
    {
        Assert.True(ScanFilterHelper.ShouldSkipFile(FileAttributes.Normal, 5));
    }

    [Fact]
    public void ShouldSkipFile_TooLarge_ReturnsTrue()
    {
        Assert.True(ScanFilterHelper.ShouldSkipFile(FileAttributes.Normal, 600_000_000));
    }

    [Fact]
    public void ShouldSkipFile_NormalFile_ReturnsFalse()
    {
        Assert.False(ScanFilterHelper.ShouldSkipFile(FileAttributes.Normal, 50_000));
    }

    [Fact]
    public void IsCloudPlaceholder_OfflineFile_ReturnsTrue()
    {
        Assert.True(ScanFilterHelper.IsCloudPlaceholder(FileAttributes.Offline));
    }

    [Fact]
    public void IsCloudPlaceholder_NormalFile_ReturnsFalse()
    {
        Assert.False(ScanFilterHelper.IsCloudPlaceholder(FileAttributes.Normal));
    }
}
