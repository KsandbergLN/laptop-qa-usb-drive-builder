# Laptop QA USB Drive Builder

A compiled native Windows WPF utility that prepares a USB drive with this fixed layout:

The drive is initialized with a GPT partition table.

Use the configuration cog in the upper-left corner to set 1-6 partitions, volume labels, sizes, and FAT32/NTFS/exFAT formats. The last partition always consumes the remaining space. Settings are saved for future launches.

After a USB drive is selected, the partition preview shows the calculated capacity of the final remaining-space partition.
The preview uses proportional widths and distinct colors to make the relative partition sizes easy to compare; very small partitions retain a minimum readable width.
The partition preview remains blank until at least one target drive is selected.

Select one or more USB drive cards to create a sequential build queue. Each drive is revalidated immediately before it is erased, built and verified before the next drive starts, and failures are logged without preventing later queued drives from running.
The Partition Layout section shows a separate proportional strip for every selected drive. Its total height remains fixed, so all strips dynamically become shorter together as more drives are added to the queue.
Hover over any partition segment to see a popup with its drive number, label, calculated size, and filesystem.

The compact Build Summary shows the queued target count, GPT style, and selected content sources; detailed capacity information is kept in the partition layout so the Activity panel has more room.

| Partition | Size | File system |
|---|---:|---|
| `DELL DIAG` | 50 MB | FAT32 |
| `Win11 Boot` | 20 GB | NTFS |
| `IT SUPP` | Remaining space | exFAT |

The app accepts multiple source folders for both `Win11 Boot` and `IT SUPP`. Each selected folder's contents are merged into the root of its destination partition in list order. An optional Windows Setup answer file can also be selected; it is copied to the root of `Win11 Boot` as `Autounattend.xml`.

## Run

Double-click the newest **Laptop QA USB Drive Builder vX.Y.Z.exe** in the `dist` folder. Accept the Windows administrator prompt; disk partitioning requires elevation. No PowerShell window is displayed. New releases keep older versioned executables in the same folder for historical access; the version is also shown in the app footer and executable metadata.

For releases, update `AppVersion`, `AssemblyVersion`, and `FileVersion` in the project file and run `publish.cmd`. It publishes through a staging directory so existing historical executables in `dist` are preserved.

The disk picker only displays disks Windows reports with a USB bus type. Before erasing, the app also checks that the target is still a USB disk and is not the system or boot disk.

## Important

- The selected USB disk is completely erased. This cannot be undone.
- Use a drive larger than 20.3 GB.
- Use **Add Folder** repeatedly to include additional folders. Select a list entry and choose **Remove** to remove it.
- Folder selections copy and merge each folder's contents into the root of the destination partition.
- Protected drive metadata such as `System Volume Information` and `$RECYCLE.BIN` is skipped when a drive root is selected as a source.
- Copy, build, and crash logs are saved under `%LOCALAPPDATA%\LaptopQAUsbBuilder\Logs`.
- This creates the requested partition layout and copies content. It does not modify source files to make a Windows image bootable.
