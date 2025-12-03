# UI Enhancement Plan: Overview & System Info Pages

This document outlines planned enhancements for the Overview and System Info pages to provide users with comprehensive system visibility.

---

## Overview Page Enhancements

The Overview page should provide **quick glances** and **actionable summaries** - not deep technical details.

### 1. Quick Stats Row
**Purpose:** Show at-a-glance counts of key system items  
**Location:** Below the Last Scan / Quick Clean cards  
**Data Source:** Existing ViewModels (Uninstaller, StartupManager)

| Stat | Description | Binding |
|------|-------------|---------|
| Installed Apps | Total applications detected | `Uninstaller.Applications.Count` |
| Startup Items | Programs that run at boot | `StartupManager.Entries.Count` |
| Browser Extensions | Extensions across all browsers | `Uninstaller.BrowserExtensions.Count` |
| Cleanup Items | Files/folders pending cleanup | `Cleanup.TotalItemCount` |

**UI Design:**
```
┌─────────────┬─────────────┬─────────────┬─────────────┐
│   📦 142    │   🚀 12     │   🧩 8      │   🗑️ 47    │
│ Installed   │  Startup    │ Extensions  │  Cleanup    │
│   Apps      │   Items     │             │   Items     │
└─────────────┴─────────────┴─────────────┴─────────────┘
```

---

### 2. RAM Usage Gauge
**Purpose:** Visual memory utilization (matches CPU/GPU gauge style)  
**Location:** Add to System Usage section as third gauge  
**Data Source:** LibreHardware Monitor (already enabled, not extracted)

| Metric | Description |
|--------|-------------|
| Usage % | Current RAM utilization |
| Used | Amount of RAM in use (GB) |
| Available | Free RAM available (GB) |
| Total | Total installed RAM |

**Implementation:**
- Extend `HardwareSnapshot` to include `MemorySnapshot`
- Add `Memory` property to `SystemUsageViewModel`
- Create gauge UI similar to CPU/GPU

---

### 3. Network Status Card
**Purpose:** Quick view of network connectivity  
**Location:** New card next to Quick Stats or below System Usage  
**Data Source:** LibreHardware (network enabled) + `System.Net.NetworkInformation`

| Metric | Description |
|--------|-------------|
| Connection Status | Connected / Disconnected |
| Adapter Name | Active network adapter |
| IP Address | Current IPv4 address |
| Download Speed | Current download rate (KB/s or MB/s) |
| Upload Speed | Current upload rate (KB/s or MB/s) |

**UI Design:**
```
┌─────────────────────────────────────┐
│ 🌐 Network                          │
│ ● Connected via Wi-Fi               │
│ Adapter: Intel Wi-Fi 6 AX200        │
│ IP: 192.168.1.105                   │
│ ↓ 2.4 MB/s    ↑ 156 KB/s           │
└─────────────────────────────────────┘
```

---

### 4. System Uptime Display
**Purpose:** Show how long the system has been running  
**Location:** Small info chip near the top or in Quick Stats  
**Data Source:** `Environment.TickCount64` or WMI `Win32_OperatingSystem.LastBootUpTime`

| Metric | Description |
|--------|-------------|
| Uptime | Duration since last boot (e.g., "2d 5h 23m") |
| Last Boot | Date/time of last restart |

---

### 5. System Health Score
**Purpose:** Single aggregate score (0-100) indicating overall system health  
**Location:** Prominent display, perhaps a large circular gauge  
**Data Source:** Calculated from multiple factors

**Scoring Factors:**
| Factor | Weight | Calculation |
|--------|--------|-------------|
| Disk Space | 25% | Penalize if any drive > 90% full |
| Startup Items | 20% | Penalize if > 15 startup items |
| Cleanup Items | 20% | Penalize based on junk file size |
| RAM Usage | 15% | Penalize if consistently > 85% |
| Storage Health | 20% | Based on SSD life remaining |

**Score Display:**
- 80-100: Excellent (Green)
- 60-79: Good (Light Green)
- 40-59: Fair (Yellow)
- 20-39: Poor (Orange)
- 0-19: Critical (Red)

---

### 6. Recent Activity Log
**Purpose:** Show last few actions taken in the app  
**Location:** Bottom of Overview page  
**Data Source:** Existing `LogEntries` collection in MainViewModel

| Column | Description |
|--------|-------------|
| Time | When the action occurred |
| Action | What was done (Scan, Clean, Uninstall, etc.) |
| Details | Brief description of result |

**UI Design:**
```
┌─────────────────────────────────────────────────┐
│ 📋 Recent Activity                              │
├─────────────────────────────────────────────────┤
│ 10:23 AM  Scan completed - 47 items found       │
│ 10:15 AM  Quick Clean - Removed 234 MB          │
│ Yesterday  Uninstalled "Old App v1.2"           │
└─────────────────────────────────────────────────┘
```

---

## System Info Page Enhancements

The System Info page should provide **detailed technical specifications** for power users.

### 1. CPU Extended Details
**Purpose:** Complete processor specifications  
**Data Source:** WMI `Win32_Processor`

| Property | WMI Field | Example |
|----------|-----------|---------|
| Name | `Name` | Intel Core i7-12700K |
| Manufacturer | `Manufacturer` | GenuineIntel |
| Cores | `NumberOfCores` | 12 |
| Threads | `NumberOfLogicalProcessors` | 20 |
| Base Clock | `MaxClockSpeed` | 3600 MHz |
| Architecture | `Architecture` | x64 |
| Socket | `SocketDesignation` | LGA1700 |
| L2 Cache | `L2CacheSize` | 12 MB |
| L3 Cache | `L3CacheSize` | 25 MB |

**WMI Query:**
```csharp
SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, 
       MaxClockSpeed, Architecture, SocketDesignation, L2CacheSize, L3CacheSize 
FROM Win32_Processor
```

---

### 2. GPU Extended Details
**Purpose:** Complete graphics card specifications  
**Data Source:** WMI `Win32_VideoController`

| Property | WMI Field | Example |
|----------|-----------|---------|
| Name | `Name` | NVIDIA GeForce RTX 4080 |
| VRAM | `AdapterRAM` | 16 GB |
| Driver Version | `DriverVersion` | 546.33 |
| Driver Date | `DriverDate` | 2024-11-15 |
| Resolution | `CurrentHorizontalResolution` x `CurrentVerticalResolution` | 2560x1440 |
| Refresh Rate | `CurrentRefreshRate` | 165 Hz |
| Video Mode | `VideoModeDescription` | 2560 x 1440 x 32 bits |

**WMI Query:**
```csharp
SELECT Name, AdapterRAM, DriverVersion, DriverDate, 
       CurrentHorizontalResolution, CurrentVerticalResolution, 
       CurrentRefreshRate, VideoModeDescription 
FROM Win32_VideoController
```

---

### 3. RAM / Memory Details
**Purpose:** Detailed memory configuration  
**Data Source:** WMI `Win32_PhysicalMemory` + Performance Counters

| Property | Source | Example |
|----------|--------|---------|
| Total Installed | WMI | 32 GB |
| Slots Used | Count of WMI results | 2 of 4 |
| Speed | `Speed` | 3200 MHz |
| Type | `SMBIOSMemoryType` | DDR4 (26) / DDR5 (34) |
| Form Factor | `FormFactor` | DIMM |
| Manufacturer | `Manufacturer` | Corsair |
| Part Number | `PartNumber` | CMK32GX4M2E3200C16 |

**Per-DIMM Details:**
```
┌─────────────────────────────────────────────────┐
│ 🧠 Memory                                       │
├─────────────────────────────────────────────────┤
│ Total: 32 GB DDR4 @ 3200 MHz                    │
│ Slots: 2 of 4 used                              │
├─────────────────────────────────────────────────┤
│ Slot 1: Corsair 16GB DDR4-3200 (DIMM_A1)       │
│ Slot 2: Corsair 16GB DDR4-3200 (DIMM_B1)       │
│ Slot 3: Empty                                   │
│ Slot 4: Empty                                   │
└─────────────────────────────────────────────────┘
```

**WMI Query:**
```csharp
SELECT Capacity, Speed, Manufacturer, PartNumber, 
       SMBIOSMemoryType, FormFactor, DeviceLocator 
FROM Win32_PhysicalMemory
```

---

### 4. Motherboard Information
**Purpose:** Mainboard and chipset details  
**Data Source:** WMI `Win32_BaseBoard` + LibreHardware

| Property | Source | Example |
|----------|--------|---------|
| Manufacturer | `Manufacturer` | ASUS |
| Model | `Product` | ROG STRIX Z690-A |
| Serial Number | `SerialNumber` | 123456789 |
| BIOS Version | `Win32_BIOS.SMBIOSBIOSVersion` | 2401 |
| BIOS Date | `Win32_BIOS.ReleaseDate` | 2024-08-15 |
| Chipset | Detection logic | Intel Z690 |

**WMI Query:**
```csharp
SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard
SELECT SMBIOSBIOSVersion, ReleaseDate, Manufacturer FROM Win32_BIOS
```

---

### 5. Fan Speeds & Cooling
**Purpose:** Monitor cooling system  
**Data Source:** LibreHardware Monitor (Motherboard sensors)

| Sensor | Description | Example |
|--------|-------------|---------|
| CPU Fan | Processor cooler RPM | 1250 RPM |
| Case Fan 1 | Chassis fan RPM | 800 RPM |
| Case Fan 2 | Chassis fan RPM | 750 RPM |
| GPU Fan | Graphics card fan RPM | 0 RPM (idle) |
| Pump | AIO/Custom loop pump RPM | 2400 RPM |

**UI Design:**
```
┌─────────────────────────────────────────────────┐
│ 🌀 Cooling                                      │
├─────────────────────────────────────────────────┤
│ CPU Fan      ████████░░░░  1250 RPM            │
│ Case Fan 1   █████░░░░░░░   800 RPM            │
│ Case Fan 2   █████░░░░░░░   750 RPM            │
│ GPU Fan      ░░░░░░░░░░░░     0 RPM (Idle)     │
└─────────────────────────────────────────────────┘
```

---

### 6. Power Rail Voltages
**Purpose:** Monitor PSU voltage stability  
**Data Source:** LibreHardware Monitor (Motherboard sensors)

| Rail | Nominal | Acceptable Range |
|------|---------|------------------|
| +3.3V | 3.3V | 3.14V - 3.47V |
| +5V | 5.0V | 4.75V - 5.25V |
| +12V | 12.0V | 11.4V - 12.6V |
| VBAT | 3.0V | 2.5V - 3.5V |
| CPU Vcore | Variable | Depends on load |

---

### 7. Windows Details
**Purpose:** Operating system information  
**Data Source:** Registry + WMI `Win32_OperatingSystem`

| Property | Source | Example |
|----------|--------|---------|
| Edition | Registry | Windows 11 Pro |
| Version | Registry | 23H2 |
| Build | Registry | 22631.4460 |
| Install Date | WMI | 2023-10-15 |
| Registered Owner | Registry | John Doe |
| Product ID | Registry | 00330-80000-00000-AA123 |

**Registry Path:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`

**WMI Query:**
```csharp
SELECT Caption, Version, BuildNumber, InstallDate, 
       RegisteredUser, SerialNumber 
FROM Win32_OperatingSystem
```

---

### 8. Boot Information
**Purpose:** System boot and uptime details  
**Data Source:** WMI + Registry

| Property | Source | Example |
|----------|--------|---------|
| Last Boot Time | WMI `LastBootUpTime` | 2024-12-01 08:15:23 |
| Uptime | Calculated | 1d 14h 32m |
| Boot Mode | Registry/Firmware | UEFI |
| Secure Boot | `Confirm-SecureBootUEFI` | Enabled |
| Fast Startup | Registry | Enabled |

---

### 9. Network Adapters (Detailed)
**Purpose:** Complete network configuration  
**Data Source:** WMI `Win32_NetworkAdapterConfiguration` + LibreHardware

| Property | Description |
|----------|-------------|
| Adapter Name | Network interface name |
| Status | Enabled / Disabled / Connected |
| Type | Ethernet / Wi-Fi / Virtual |
| MAC Address | Physical address |
| IPv4 Address | Current IP |
| IPv6 Address | Current IPv6 |
| Subnet Mask | Network mask |
| Default Gateway | Router address |
| DNS Servers | DNS configuration |
| DHCP Enabled | Yes / No |
| Link Speed | 1 Gbps / 2.4 Gbps |
| Bytes Sent | Total data sent |
| Bytes Received | Total data received |

**WMI Query:**
```csharp
SELECT Description, MACAddress, IPAddress, IPSubnet, 
       DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled 
FROM Win32_NetworkAdapterConfiguration 
WHERE IPEnabled = TRUE
```

---

### 10. Battery Information (Laptops)
**Purpose:** Battery health and status  
**Data Source:** WMI `Win32_Battery` + LibreHardware

| Property | Description | Example |
|----------|-------------|---------|
| Status | Charging / Discharging / Full | Discharging |
| Charge Level | Current percentage | 78% |
| Health | Estimated health % | 92% |
| Design Capacity | Original capacity | 60 Wh |
| Full Charge Capacity | Current max capacity | 55.2 Wh |
| Cycle Count | Charge cycles | 234 |
| Time Remaining | Estimated time left | 4h 23m |
| Power Source | AC / Battery | Battery |

**WMI Query:**
```csharp
SELECT EstimatedChargeRemaining, BatteryStatus, 
       DesignCapacity, FullChargeCapacity 
FROM Win32_Battery
```

---

### 11. Audio Devices
**Purpose:** Sound hardware information  
**Data Source:** WMI `Win32_SoundDevice`

| Property | Description |
|----------|-------------|
| Device Name | Sound card/chip name |
| Manufacturer | Audio chip maker |
| Status | OK / Error |
| Default Playback | Current output device |
| Default Recording | Current input device |

---

### 12. Display / Monitors
**Purpose:** Connected display information  
**Data Source:** WMI `Win32_DesktopMonitor` + `Win32_VideoController`

| Property | Description | Example |
|----------|-------------|---------|
| Monitor Name | Display model | Dell S2722DGM |
| Resolution | Native resolution | 2560 x 1440 |
| Refresh Rate | Current rate | 165 Hz |
| Connection | Interface type | DisplayPort |
| HDR Support | HDR capability | Yes |
| Screen Size | Diagonal inches | 27" |

---

### 13. Security Status
**Purpose:** System security overview  
**Data Source:** WMI + PowerShell + Registry

| Property | Description |
|----------|-------------|
| Windows Defender | Enabled / Disabled |
| Real-time Protection | On / Off |
| Last Scan | Date of last scan |
| Definitions | Virus definitions date |
| Firewall (Domain) | On / Off |
| Firewall (Private) | On / Off |
| Firewall (Public) | On / Off |
| TPM Version | 1.2 / 2.0 / Not Present |
| TPM Status | Ready / Not Ready |
| BitLocker | Encrypted drives |

---

### 14. Windows Update
**Purpose:** Update status information  
**Data Source:** WMI `Win32_QuickFixEngineering` + COM API

| Property | Description |
|----------|-------------|
| Last Update | Most recent update date |
| Pending Updates | Count of available updates |
| Update History | Recent 5 updates installed |
| Auto Update | Enabled / Disabled |

---

### 15. Process Statistics
**Purpose:** System resource summary  
**Data Source:** `System.Diagnostics` + Performance Counters

| Metric | Description |
|--------|-------------|
| Running Processes | Total process count |
| Total Threads | System-wide thread count |
| Handle Count | Open handles |
| Commit Charge | Committed memory |
| Cached Memory | File cache size |
| Paged Pool | Kernel paged memory |
| Non-Paged Pool | Kernel non-paged memory |

---

### 16. Page File Information
**Purpose:** Virtual memory configuration  
**Data Source:** WMI `Win32_PageFileUsage`

| Property | Description | Example |
|----------|-------------|---------|
| Location | Page file path | C:\pagefile.sys |
| Allocated Size | Current size | 16 GB |
| Current Usage | Amount in use | 2.3 GB |
| Peak Usage | Maximum used | 8.1 GB |
| System Managed | Auto or manual | Yes |

---

## Implementation Priority

### Phase 1 (High Value, Low Effort)
1. ✅ Quick Stats Row (Overview)
2. ✅ RAM Usage Gauge (Overview)
3. ✅ Uptime Display (Overview)
4. ✅ CPU Extended Details (System Info)
5. ✅ GPU Extended Details (System Info)
6. ✅ Windows Details (System Info)

### Phase 2 (Medium Effort)
7. Network Status Card (Overview)
8. RAM/DIMM Details (System Info)
9. Network Adapters Full (System Info)
10. Boot Information (System Info)
11. Fan Speeds (System Info)

### Phase 3 (Lower Priority)
12. System Health Score (Overview)
13. Recent Activity Log (Overview)
14. Motherboard Info (System Info)
15. Battery Info (System Info)
16. Security Status (System Info)
17. Audio/Display Info (System Info)

---

## Data Source Summary

| Source | Used For |
|--------|----------|
| **LibreHardware Monitor** | CPU/GPU/RAM usage, temperatures, fan speeds, voltages, network bandwidth |
| **WMI (System.Management)** | Hardware specs, Windows info, network config, battery |
| **Registry** | Windows version, boot mode, product info |
| **System.Diagnostics** | Process counts, performance counters |
| **System.Net.NetworkInformation** | Network adapter status, IP addresses |
| **Environment Class** | Machine name, OS version, uptime |

---

## Notes

- All WMI queries should be wrapped in try-catch for systems where data may not be available
- LibreHardware Monitor requires admin privileges for some sensors
- Battery section should only display on laptops (detect via `Win32_Battery` presence)
- Fan speeds may not be available on all motherboards
- Consider caching WMI data to avoid repeated queries (refresh on demand)
