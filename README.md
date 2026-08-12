# ABPLC Reader Writer

C# tool for discovering Allen-Bradley (Rockwell) ControlLogix / CompactLogix PLCs on a network, browsing their tags, and reading tag attribute values.

Tags are listed and **grouped by root name**. Structures appear as `Tag.Attribute` members, matching the common CIP symbolic addressing style.

**Write support** is planned for a future release.

## Features

- **Network scan** – Scan a CIDR or IP range for devices listening on EtherNet/IP port 44818
- **Manual PLC entry** – Add any IP + CIP path (default `1,0`)
- **Tag discovery** – Uses libplctag’s special `@tags` (and `Program:xxx.@tags`) to retrieve the controller tag database
- **Grouped view** – Tags are grouped by the portion before the first `.` so structures and their members stay together
- **Attribute / value read** – Select a group and read the current value of every member
- Interactive console UI with selection lists (Spectre.Console) that behave like dropdowns

## Requirements

- .NET 8 SDK or later
- Network access to the PLC(s) (TCP 44818)
- Windows, Linux, or macOS (libplctag native binaries are included)

## Quick Start

```bash
git clone https://github.com/SlyEch0/ABPLC-Reader-Writer.git
cd ABPLC-Reader-Writer/ABPLCReaderWriter
dotnet run
```

### Typical workflow

1. **Scan network** – enter e.g. `192.168.1.0/24`
2. Optionally probe which candidates respond to `@tags`
3. **Select PLC**
4. **List / browse tags** – choose a root name from the selection list
5. **Read attributes** – choose the same (or another) group to see live values

## Architecture

```
ABPLCReaderWriter/
├── Models/
│   ├── PlcDevice.cs          # IP, Path, friendly name
│   └── PlcTagInfo.cs         # Id, Type, Name, Dimensions + helpers
├── Services/
│   ├── NetworkScanner.cs     # Parallel TCP 44818 scan + CIDR/range expansion
│   └── PlcTagService.cs      # @tags listing, grouping, value reads
└── Program.cs                # Interactive Spectre.Console UI
```

## Notes & Limitations

- Designed primarily for Logix family (ControlLogix / CompactLogix). Micro800 / SLC / PLC-5 support is limited by libplctag.
- The `TagInfoPlcMapper` used for listing is currently marked `[Obsolete]` in libplctag.NET (the mapper system is being redesigned). It still works; a future version will switch to the new API or a custom decoder.
- Value interpretation for complex UDTs is best-effort (hex dump fallback). Full UDT template parsing is a natural next step.
- No write operations yet – the structure is ready for a `WriteTagValueAsync` method.

## Roadmap

- [ ] Write support for atomic types and selected structure members
- [ ] Cache type information from listing to improve value decoding
- [ ] Optional UDT template (`@udt/nnn`) expansion
- [ ] Export tag list / values to CSV or JSON
- [ ] Optional WinForms / WPF front-end (services are already UI-agnostic)

## License

MIT (this project).  
libplctag is dual-licensed MPL-2.0 / LGPL-2+.

## Disclaimer

PLCs control real equipment. Incorrect reads are usually harmless; incorrect writes can cause injury, equipment damage, or production loss. Use only on systems you are authorized to access and always follow site safety procedures.
