# ABPLC Reader Writer

C# library for discovering Allen-Bradley (Rockwell) ControlLogix / CompactLogix PLCs on a network, browsing their tags, and reading tag attribute values.

**Target Framework: .NET Framework 4.8** (compatible with classic .NET Framework projects)

Tags are listed and **grouped by root name**. Structures appear as `Tag.Attribute` members.

**Write support** is planned for a future release.

## Features

- **Network scan** – Scan a CIDR or IP range for devices listening on EtherNet/IP port 44818
- **Tag discovery** – Uses libplctag’s special `@tags` (and `Program:xxx.@tags`)
- **Grouped view** – Tags are grouped by the portion before the first `.`
- **Attribute / value read** – Read current values of tag members

## Requirements

- .NET Framework 4.8
- Network access to the PLC(s) (TCP 44818)
- NuGet packages: `libplctag` (and optionally Spectre.Console if you use the old console UI)

## How to reference from another .NET Framework 4.8 project

1. Add the project to your solution (**Add → Existing Project…** → select `ABPLCReaderWriter.csproj`)
2. In your project: **Add → Project Reference…** → check **ABPLCReaderWriter**
3. Add the required usings:

```csharp
using ABPLCReaderWriter.Models;
using ABPLCReaderWriter.Services;
```

Example:

```csharp
var scanner = new NetworkScanner();
var devices = await scanner.ScanAsync("192.168.1.0/24");

var tagService = new PlcTagService();
var groups = await tagService.ListTagsGroupedAsync(devices[0]);
```

## Project structure

```
ABPLCReaderWriter/
├── Models/
│   ├── PlcDevice.cs
│   └── PlcTagInfo.cs
└── Services/
    ├── NetworkScanner.cs
    └── PlcTagService.cs
```

## Notes

- Converted to a **Class Library** targeting `net48` for compatibility with classic .NET Framework solutions.
- `Parallel.ForEachAsync` and other .NET 6+ APIs were replaced with compatible equivalents.
- Language version locked to C# 7.3 for maximum compatibility.

## License

MIT (this project).  
libplctag is dual-licensed MPL-2.0 / LGPL-2+.

## Disclaimer

PLCs control real equipment. Use only on systems you are authorized to access and always follow site safety procedures.
