namespace iTerminal.Core.Data;

public class CachedWeatherInfo
{
    public int Id { get; set; }
    public required string LocationName { get; set; }
    public required double TemperatureC { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }
    public DateTime SourceDateTime { get; set; }
    public DateTime CachedAtUtc { get; set; }
}
