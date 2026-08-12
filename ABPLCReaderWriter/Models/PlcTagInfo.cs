using System;

namespace ABPLCReaderWriter.Models
{
    /// <summary>
    /// Represents a tag discovered from the PLC (@tags special tag).
    /// Tags can be atomic or structures; attributes appear as tag.attribute.
    /// </summary>
    public class PlcTagInfo
    {
        public uint Id { get; set; }
        public ushort Type { get; set; }
        public string Name { get; set; }
        public ushort Length { get; set; }
        public uint[] Dimensions { get; set; }

        public PlcTagInfo()
        {
            Name = string.Empty;
            Dimensions = new uint[0];
        }

        public string RootName
        {
            get
            {
                var idx = Name.IndexOf('.');
                // C# 7 compatible
                return idx > 0 ? Name.Substring(0, idx) : Name;
            }
        }

        public string AttributeName
        {
            get
            {
                var idx = Name.IndexOf('.');
                return idx > 0 ? Name.Substring(idx + 1) : string.Empty;
            }
        }

        public bool IsStructureMember
        {
            get { return !string.IsNullOrEmpty(AttributeName); }
        }

        public string TypeDescription
        {
            get
            {
                // Classic switch for C# 7
                switch (Type)
                {
                    case 0xC1: return "BOOL";
                    case 0xC2: return "SINT";
                    case 0xC3: return "INT";
                    case 0xC4: return "DINT";
                    case 0xC5: return "LINT";
                    case 0xCA: return "REAL";
                    case 0xCB: return "LREAL";
                    case 0xD0: return "STRING";
                    default:
                        if ((Type & 0x8000) != 0)
                            return "STRUCT/UDT";
                        return string.Format("0x{0:X4}", Type);
                }
            }
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}]", Name, TypeDescription);
        }
    }
}
