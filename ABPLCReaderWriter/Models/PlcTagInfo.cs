namespace ABPLCReaderWriter.Models;

/// <summary>
/// Represents a tag discovered from the PLC (@tags special tag).
/// Tags can be atomic or structures; attributes appear as tag.attribute.
/// </summary>
public class PlcTagInfo
{
    public uint Id { get; set; }
    public ushort Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort Length { get; set; }
    public uint[] Dimensions { get; set; } = Array.Empty<uint>();

    public string RootName
    {
        get
        {
            var idx = Name.IndexOf('.');
            return idx > 0 ? Name[..idx] : Name;
        }
    }

    public string? AttributeName
    {
        get
        {
            var idx = Name.IndexOf('.');
            return idx > 0 ? Name[(idx + 1)..] : null;
        }
    }

    public bool IsStructureMember => AttributeName != null;

    public string TypeDescription
    {
        get
        {
            // Common CIP type codes (simplified)
            return Type switch
            {
                0xC1 => "BOOL",
                0xC2 => "SINT",
                0xC3 => "INT",
                0xC4 => "DINT",
                0xC5 => "LINT",
                0xCA => "REAL",
                0xCB => "LREAL",
                0xD0 => "STRING",
                _ when (Type & 0x8000) != 0 => "STRUCT/UDT",
                _ => $"0x{Type:X4}"
            };
        }
    }

    public override string ToString() => $"{Name} [{TypeDescription}]";
}
