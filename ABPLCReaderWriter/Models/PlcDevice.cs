using System;

namespace ABPLCReaderWriter.Models
{
    public class PlcDevice
    {
        public string IpAddress { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public PlcDevice()
        {
            IpAddress = string.Empty;
            Name = string.Empty;
            Path = "1,0";
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name))
                    return "PLC @ " + IpAddress;
                return Name + " (" + IpAddress + ")";
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
