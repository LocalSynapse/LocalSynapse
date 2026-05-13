using LocalSynapse.Core.Models;
using Xunit;

namespace LocalSynapse.Core.Tests;

public class LicenseTierTest
{
    [Fact]
    public void Default_Value_Is_Free()
    {
        LicenseTier tier = default;
        Assert.Equal(LicenseTier.Free, tier);
    }

    [Fact]
    public void Has_Exactly_Two_Values()
    {
        var values = Enum.GetValues<LicenseTier>();
        Assert.Equal(2, values.Length);
        Assert.Contains(LicenseTier.Free, values);
        Assert.Contains(LicenseTier.Pro, values);
    }

    [Fact]
    public void Integer_Values_Are_Stable_For_Serialization()
    {
        Assert.Equal(0, (int)LicenseTier.Free);
        Assert.Equal(1, (int)LicenseTier.Pro);
    }

    [Fact]
    public void Is_Independent_From_RuntimeMode()
    {
        Assert.NotEqual(typeof(LicenseTier), typeof(RuntimeMode));
    }
}
