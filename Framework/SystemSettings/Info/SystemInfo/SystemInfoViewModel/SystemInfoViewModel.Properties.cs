using TM.Framework.Common.ViewModels;
using System.Windows.Input;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public partial class SystemInfoViewModel
    {
        private string _osName = string.Empty;
        public string OSName
        {
            get => _osName;
            set { _osName = value; OnPropertyChanged(nameof(OSName)); }
        }

        private string _osVersion = string.Empty;
        public string OSVersion
        {
            get => _osVersion;
            set { _osVersion = value; OnPropertyChanged(nameof(OSVersion)); }
        }

        private string _osArchitecture = string.Empty;
        public string OSArchitecture
        {
            get => _osArchitecture;
            set { _osArchitecture = value; OnPropertyChanged(nameof(OSArchitecture)); }
        }

        private string _computerName = string.Empty;
        public string ComputerName
        {
            get => _computerName;
            set { _computerName = value; OnPropertyChanged(nameof(ComputerName)); }
        }

        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(nameof(UserName)); }
        }

        private string _cpuName = string.Empty;
        public string CPUName
        {
            get => _cpuName;
            set { _cpuName = value; OnPropertyChanged(nameof(CPUName)); }
        }

        private int _cpuCores;
        public int CPUCores
        {
            get => _cpuCores;
            set { _cpuCores = value; OnPropertyChanged(nameof(CPUCores)); }
        }

        private int _cpuLogicalProcessors;
        public int CPULogicalProcessors
        {
            get => _cpuLogicalProcessors;
            set { _cpuLogicalProcessors = value; OnPropertyChanged(nameof(CPULogicalProcessors)); }
        }

        private string _cpuArchitecture = "未知";
        public string CPUArchitecture
        {
            get => _cpuArchitecture;
            set { _cpuArchitecture = value; OnPropertyChanged(nameof(CPUArchitecture)); }
        }

        private string _cpuBaseFrequency = "未知";
        public string CPUBaseFrequency
        {
            get => _cpuBaseFrequency;
            set { _cpuBaseFrequency = value; OnPropertyChanged(nameof(CPUBaseFrequency)); }
        }

        private string _cpuMaxFrequency = "未知";
        public string CPUMaxFrequency
        {
            get => _cpuMaxFrequency;
            set { _cpuMaxFrequency = value; OnPropertyChanged(nameof(CPUMaxFrequency)); }
        }

        private string _cpuL1Cache = "未知";
        public string CPUL1Cache
        {
            get => _cpuL1Cache;
            set { _cpuL1Cache = value; OnPropertyChanged(nameof(CPUL1Cache)); }
        }

        private string _cpuL2Cache = "未知";
        public string CPUL2Cache
        {
            get => _cpuL2Cache;
            set { _cpuL2Cache = value; OnPropertyChanged(nameof(CPUL2Cache)); }
        }

        private string _cpuL3Cache = "未知";
        public string CPUL3Cache
        {
            get => _cpuL3Cache;
            set { _cpuL3Cache = value; OnPropertyChanged(nameof(CPUL3Cache)); }
        }

        private string _cpuVirtualization = "未知";
        public string CPUVirtualization
        {
            get => _cpuVirtualization;
            set { _cpuVirtualization = value; OnPropertyChanged(nameof(CPUVirtualization)); }
        }

        private string _totalMemory = string.Empty;
        public string TotalMemory
        {
            get => _totalMemory;
            set { _totalMemory = value; OnPropertyChanged(nameof(TotalMemory)); }
        }

        public RangeObservableCollection<MemoryModuleItem> MemoryModules { get; } = null!;

        private string _totalVirtualMemory = "未知";
        public string TotalVirtualMemory
        {
            get => _totalVirtualMemory;
            set { _totalVirtualMemory = value; OnPropertyChanged(nameof(TotalVirtualMemory)); }
        }

        private string _availableVirtualMemory = "未知";
        public string AvailableVirtualMemory
        {
            get => _availableVirtualMemory;
            set { _availableVirtualMemory = value; OnPropertyChanged(nameof(AvailableVirtualMemory)); }
        }

        private string _pageFileSize = "未知";
        public string PageFileSize
        {
            get => _pageFileSize;
            set { _pageFileSize = value; OnPropertyChanged(nameof(PageFileSize)); }
        }

        private string _screenResolution = string.Empty;
        public string ScreenResolution
        {
            get => _screenResolution;
            set { _screenResolution = value; OnPropertyChanged(nameof(ScreenResolution)); }
        }

        private int _screenCount;
        public int ScreenCount
        {
            get => _screenCount;
            set { _screenCount = value; OnPropertyChanged(nameof(ScreenCount)); }
        }

        public RangeObservableCollection<DriveInfoItem> DriveInfos { get; } = null!;
        public RangeObservableCollection<NetworkAdapterItem> NetworkAdapters { get; } = null!;
        public RangeObservableCollection<GpuInfoItem> GpuInfos { get; } = null!;

        public RangeObservableCollection<DisplayInfoItem> Displays { get; } = null!;

        private string _motherboardManufacturer = "未知";
        public string MotherboardManufacturer
        {
            get => _motherboardManufacturer;
            set { _motherboardManufacturer = value; OnPropertyChanged(nameof(MotherboardManufacturer)); }
        }

        private string _motherboardProduct = "未知";
        public string MotherboardProduct
        {
            get => _motherboardProduct;
            set { _motherboardProduct = value; OnPropertyChanged(nameof(MotherboardProduct)); }
        }

        private string _motherboardVersion = "未知";
        public string MotherboardVersion
        {
            get => _motherboardVersion;
            set { _motherboardVersion = value; OnPropertyChanged(nameof(MotherboardVersion)); }
        }

        private string _motherboardSerialNumber = "未知";
        public string MotherboardSerialNumber
        {
            get => _motherboardSerialNumber;
            set { _motherboardSerialNumber = value; OnPropertyChanged(nameof(MotherboardSerialNumber)); }
        }

        private string _biosManufacturer = "未知";
        public string BiosManufacturer
        {
            get => _biosManufacturer;
            set { _biosManufacturer = value; OnPropertyChanged(nameof(BiosManufacturer)); }
        }

        private string _biosVersion = "未知";
        public string BiosVersion
        {
            get => _biosVersion;
            set { _biosVersion = value; OnPropertyChanged(nameof(BiosVersion)); }
        }

        private string _biosReleaseDate = "未知";
        public string BiosReleaseDate
        {
            get => _biosReleaseDate;
            set { _biosReleaseDate = value; OnPropertyChanged(nameof(BiosReleaseDate)); }
        }

        private string _biosSmbiosVersion = "未知";
        public string BiosSmbiosVersion
        {
            get => _biosSmbiosVersion;
            set { _biosSmbiosVersion = value; OnPropertyChanged(nameof(BiosSmbiosVersion)); }
        }

        public ICommand RefreshCommand { get; } = null!;
        public ICommand ExportCommand { get; } = null!;
    }
}
