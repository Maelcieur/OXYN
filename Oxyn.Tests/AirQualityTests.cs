using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class AirQualityTests
{
    [Fact]
    public void ParseAirQuality_ReadsIndexAndPollutants()
    {
        var air = WeatherService.ParseAirQuality(
            """{"current":{"european_aqi":34,"pm2_5":8.2,"pm10":14.9,"nitrogen_dioxide":11.0,"ozone":52.3}}""", "Rouen, France");
        Assert.NotNull(air);
        Assert.Equal(34, air!.Aqi);
        Assert.Equal(8.2, air.Pm25!.Value, 3);
        Assert.Equal("Correct", WeatherService.AirLevel(air.Aqi).Label);
        Assert.Null(WeatherService.ParseAirQuality("""{"current":{"european_aqi":null}}""", ""));
    }
}
