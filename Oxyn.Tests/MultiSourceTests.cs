using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class MultiSourceTests
{
    [Fact]
    public void Bridge_BytesBecomeGigabytes_AndMegabytes()
    {
        var capacity = new BridgeSensor("/memory/dimm/1/data/50", "Corsair", "Memory", "Capacity", "Data", 17179869184f).Normalized();
        Assert.Equal(16f, capacity.Value, 3);
        var vram = new BridgeSensor("/gpu/0/smalldata/1", "GPU", "GpuNvidia", "GPU Memory Used", "SmallData", 1725997000f).Normalized();
        Assert.InRange(vram.Value, 1645, 1647); // Mo
        var gb = new BridgeSensor("/ram/data/0", "RAM", "Memory", "Memory Used", "Data", 10.8f).Normalized();
        Assert.Equal(10.8f, gb.Value); // déjà en Go : inchangé
    }

    [Fact]
    public void Catalog_SkipsLimitsAndResolution()
    {
        Assert.Null(SensorCatalog.FromBridge(new BridgeSensor("/m/1/t/3", "Corsair", "Memory", "Thermal Sensor High Limit", "Temperature", 55)));
        Assert.Null(SensorCatalog.FromBridge(new BridgeSensor("/m/1/t/1", "Corsair", "Memory", "Temperature Sensor Resolution", "Temperature", 0.25f)));
        Assert.NotNull(SensorCatalog.FromBridge(new BridgeSensor("/m/1/t/0", "Corsair", "Memory", "DIMM #1", "Temperature", 32.2f)));
    }

    [Fact]
    public void Block_AllSources_InOrder_AndCopyIsIndependent()
    {
        var b = new BuilderBlock { Source = "dimm1", MoreSources = new() { "dimm3", "" }, Combine = "Sum" };
        Assert.Equal(new[] { "dimm1", "dimm3" }, b.AllSources());
        var copy = b.Copy();
        copy.MoreSources!.Add("x");
        Assert.Equal(2, b.MoreSources.Count);
    }
}
