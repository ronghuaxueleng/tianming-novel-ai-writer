namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public class DriveInfoItem
    {
        public string DriveName { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string TotalSize { get; set; } = string.Empty;
        public string UsedSize { get; set; } = string.Empty;
        public string FreeSize { get; set; } = string.Empty;
        public double UsagePercent { get; set; }

        public string MediaType { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = string.Empty;
        public string DiskIcon { get; set; } = string.Empty;
    }

    public class NetworkAdapterItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public string IPv6Address { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string DNS { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string BytesSent { get; set; } = string.Empty;
        public string BytesReceived { get; set; } = string.Empty;
        public bool IsWireless { get; set; }
        public string ConnectionIcon { get; set; } = string.Empty;
    }

    public class GpuInfoItem
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string VideoProcessor { get; set; } = string.Empty;
        public string VideoMemorySize { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public string DriverDate { get; set; } = string.Empty;
        public string VideoModeDescription { get; set; } = string.Empty;
        public string AdapterRAM { get; set; } = string.Empty;
        public string DeviceID { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class MemoryModuleItem
    {
        public string BankLabel { get; set; } = string.Empty;
        public string Capacity { get; set; } = string.Empty;
        public string MemoryType { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
    }

    public class DisplayInfoItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public string WorkingArea { get; set; } = string.Empty;
        public string BitsPerPixel { get; set; } = string.Empty;
        public string RefreshRate { get; set; } = string.Empty;
        public string AspectRatio { get; set; } = string.Empty;
        public string DPI { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string ManufactureYear { get; set; } = string.Empty;
    }
}
