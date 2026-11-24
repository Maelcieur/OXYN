using System.Text.Json;
using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class ModuleDesignTests
{
    [Theory]
    [InlineData("gpu"), InlineData("cpu"), InlineData("ram"), InlineData("disk"), InlineData("network")]
    public void HardwareModules_HaveFiveLayouts_ClassicFirst(string id)
    {
        var presets = DesignPresets.For(id);
        Assert.Equal(5, presets.Count);
        Assert.Equal(ModuleDesign.Classic, presets[0].Id);
        Assert.True(presets[0].Create().IsEmpty);
        foreach (var preset in presets.Skip(1))
        {
            var d = preset.Create();
            Assert.False(d.IsClassic);
            Assert.NotEmpty(d.Blocks);
            Assert.Equal(d.Blocks.Count, d.Blocks.Select(b => b.Id).Distinct().Count());
            foreach (var b in d.Blocks)
            {
                Assert.True(BuilderBlock.TypeLabels.ContainsKey(b.Type), b.Type);
                Assert.InRange(b.X + b.Width, 0, d.Width + 1);
                Assert.InRange(b.Y + b.Height, 0, d.Height + 1);
            }
        }
    }

    [Fact]
    public void OtherModules_OnlyClassic()
        => Assert.Single(DesignPresets.For("weather"));

    [Fact]
    public void Design_RoundTripsInModuleAppearance()
    {
        var a = new ModuleAppearance { Design = DesignPresets.For("gpu")[1].Create() };
        a.Design.Blocks.Add(new BuilderBlock { Type = "Graph", Source = SensorCatalog.CpuTemp, HistorySeconds = 120, Fill = false });

        var back = JsonSerializer.Deserialize<ModuleAppearance>(JsonSerializer.Serialize(a))!;
        Assert.Equal("Sheet", back.Design!.Layout);
        var graph = back.Design.Blocks.Last();
        Assert.Equal(120, graph.HistorySeconds);
        Assert.False(graph.Fill);
        Assert.Null(JsonSerializer.Deserialize<ModuleAppearance>("{}")!.Design); // ancienne config : disposition d'origine
    }

    [Fact]
    public void Classic_WithAddedElement_IsNotEmpty()
    {
        var d = new ModuleDesign();
        Assert.True(d.IsEmpty);
        d.Blocks.Add(new BuilderBlock { Type = "Graph" });
        Assert.False(d.IsEmpty);
        Assert.True(d.IsClassic);
    }
}
