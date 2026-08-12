namespace ABPLCReaderWriter.Models;

public class PlcDevice
{
    public string IpAddress { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = "1,0";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"PLC @ {IpAddress}" : $"{Name} ({IpAddress})";

    public override string ToString() => DisplayName;
}
