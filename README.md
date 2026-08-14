# Laptop QA USB Drive Builder

A native Windows WPF utility for erasing USB drives, creating a configurable GPT partition layout, and copying standardized support content to each partition.

## Default layout

The factory defaults are:

| Partition | Size | File system |
|---|---:|---|
| `DELL DIAG` | 50 MB | FAT32 |
| `Win11 Boot` | 20 GB | NTFS |
| `IT SUPP` | `*` (all remaining space) | exFAT |

`*` may be used on exactly one partition in any position. Fixed sizes accept MB or GB values, such as `50 MB` and `20 GB`.

## Partition configuration

Open the three-bar menu in the upper-left corner to manage the default partition layout. Defaults can contain 1-6 partitions with configurable volume labels, sizes, and FAT32, NTFS, or exFAT formats.

Default editing is locked initially to prevent accidental changes. Unlock it with the lock icon, make the required changes, and choose **Save**. The main-screen **Defaults** button restores the saved default layout without changing the factory fallback values.

Partitions can be added with the green `+`, removed with their red `-`, and reordered using the two-bar drag handles. Config removal and reordering controls are disabled while defaults are locked.

## Adding content

Every partition on the main screen can receive its own files and folders. Folder contents are merged into the root of the destination partition, while selected files are copied directly to that root.

NTFS partitions also show an **XML** button for selecting an answer file. The selected file is copied to the partition root as `Autounattend.xml`; the button turns bright green when a file is attached.

Content selections stay with their partition when the partition is reordered. Hover over the content controls to review the selected paths, or use **Clear** to remove all content selections from that partition.

## Selecting and building USB drives

The drive picker shows disks that Windows reports with a USB bus type. Select one or more drive cards to create a sequential build queue. Each selected drive is revalidated immediately before it is erased, partitioned, populated, and verified. A failure on one drive is logged without preventing later queued drives from running.

Before building, enter `ERASE` in the confirmation field. Every partition and file on each selected target is permanently removed.

The Partition Layout card remains blank until a drive is selected. It then displays proportional, color-coded partition segments using each drive's calculated capacity. Multiple selected drives share the available height dynamically. Hover over a segment to see its drive number, label, calculated size, and file system.

## Appearance and language

The configuration menu includes Light, Dark, and AMOLED themes and the same 12-language set used by Laptop QA V2. Theme changes preview live, and saved theme and language preferences persist between launches.

## Run

Double-click the newest **Laptop QA USB Drive Builder vX.Y.Z.exe** in the `dist` folder and accept the administrator prompt. Disk partitioning requires elevation. The WPF application performs storage operations without displaying a PowerShell window.

The version appears in the app footer and executable metadata. Historical versioned executables can coexist in the shared `dist` folder.

For operating instructions, see the [Quick User Guide](docs/QUICK_USER_GUIDE.md). For support ownership, troubleshooting, and escalation details, see the [Technician Handoff](docs/TECHNICIAN_HANDOFF.md).

## Build and publish

The project targets .NET 8 for Windows:

```powershell
dotnet build .\LaptopQaUsbBuilder.csproj -c Release
```

For a versioned release, update `AppVersion`, `AssemblyVersion`, and `FileVersion` in `LaptopQaUsbBuilder.csproj`, then run:

```powershell
.\publish.cmd
```

The publish script uses a staging directory and places the versioned executable in `dist` without deleting historical builds.

## Safety and logs

- The app initializes every selected target as GPT.
- The selected USB disks are completely erased; this cannot be undone.
- Targets are checked again before erasure and rejected if Windows reports them as boot, system, non-USB, or changed since selection.
- Sources stored on a queued target disk are rejected before building.
- Protected metadata such as `System Volume Information` and `$RECYCLE.BIN` is skipped when a drive root is used as a source.
- FAT32 sizes and volume-label lengths are validated against Windows limits.
- Copy, build, and crash logs are saved under `%LOCALAPPDATA%\LaptopQAUsbBuilder\Logs`.
- The app creates partitions and copies content; it does not modify source files to make a Windows image bootable.
