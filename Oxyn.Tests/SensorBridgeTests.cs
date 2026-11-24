using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class SensorBridgeTests
{
    private const string Sample = """
    {"v":1,"t":1764000000000,"sensors":[
      {"id":"/amdcpu/0/temperature/2","hw":"AMD Ryzen 7 9800X3D","hwType":"Cpu","name":"Core (Tctl/Tdie)","type":"Temperature","value":45.25},
      {"id":"/lpc/nct6687d/0/fan/2","hw":"Nuvoton NCT6687D","hwType":"SuperIO","name":"System Fan #1","type":"Fan","value":1030},
      {"id":"/memory/dimm/1/temperature/0","hw":"Corsair - CMK32GX5M2E6000Z36 (#1)","hwType":"Memory","name":"DIMM #1","type":"Temperature","value":31.5},
      {"id":"/bad","hw":"x","hwType":"Cpu","name":"sans valeur","type":"Temperature"},
      {"id":"/gpu-nvidia/0/load/0","hw":"NVIDIA GeForce RTX 5070 Ti","hwType":"GpuNvidia","name":"D3D 3D","type":"Load","value":3}
    ]}
    """;

    [Fact]
    public void Parse_ReadsSensorsAndSkipsInvalidOnes()
    {
        var list = SensorBridgeReader.Parse(Sample);
        Assert.Equal(4, list.Count);
        Assert.Equal(45.25f, list[0].Value);
        Assert.Equal("SuperIO", list[1].HardwareType);
        Assert.Empty(SensorBridgeReader.Parse("""{"v":1}"""));
    }

    [Fact]
    public void Parse_ConvertsBytesToGigabytesAndMegabytes()
    {
        var list = SensorBridgeReader.Parse("""
        {"v":1,"sensors":[
          {"id":"/ram/data/0","hw":"Total Memory","hwType":"Memory","name":"Memory Used","type":"Data","value":14189550000},
          {"id":"/gpu-amd/0/smalldata/1","hw":"AMD Radeon(TM) Graphics","hwType":"GpuAmd","name":"GPU Memory Total","type":"SmallData","value":536870900},
          {"id":"/ram/data/1","hw":"Generic Memory","hwType":"Memory","name":"Memory Used","type":"Data","value":13.2},
          {"id":"/gpu-nvidia/0/smalldata/0","hw":"NVIDIA","hwType":"GpuNvidia","name":"GPU Memory Total","type":"SmallData","value":16303}
        ]}
        """);
        Assert.Equal(13.2f, list[0].Value, 1);   // octets → Go
        Assert.Equal(512f, list[1].Value, 0);    // octets → Mo
        Assert.Equal(13.2f, list[2].Value, 2);   // déjà en Go (LibreHardwareMonitor 0.9.6)
        Assert.Equal(16303f, list[3].Value, 0);  // déjà en Mo
    }

    [Fact]
    public void FromBridge_KeepsLibreHardwareMonitorIdsAndFrenchNames()
    {
        var readings = SensorBridgeReader.Parse(Sample).Select(SensorCatalog.FromBridge).OfType<SensorReading>().ToList();
        Assert.Equal(3, readings.Count); // compteur D3D ignoré, comme sans le pont
        Assert.Equal("/amdcpu/0/temperature/2", readings[0].Id);
        Assert.Equal("°C", readings[0].Unit);
        Assert.Equal("tr/min", readings[1].Unit);
        Assert.Equal("System Fan #1", readings[1].Name);
        Assert.Equal("Température · DIMM #1", readings[2].Name);
    }
}
