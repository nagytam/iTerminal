namespace iTerminal.Core.Data;

public class WeatherInfo
{
    public required string LocationName { get; set; }
    public required double TemperatureC { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }
    public DateTime SourceDateTime { get; set; }
}
