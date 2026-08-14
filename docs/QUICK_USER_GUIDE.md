# Laptop QA USB Drive Builder — Quick User Guide

Use this guide when you need to create one or more standardized QA/support USB drives.

## Safety first

**Building permanently erases every selected USB drive.** Check the drive name, number, and capacity before you type `ERASE`.

## 1. Start the app

Open the newest `Laptop QA USB Drive Builder vX.Y.Z.exe` in the `dist` folder. Approve the Windows administrator prompt.

## 2. Select the target drive(s)

Insert the USB drive(s), then:

1. Select **Refresh** if needed.
2. Click each USB drive card you want to build.
3. Hover over a card to see its capacity, serial number, and unique ID.

Selected cards form a queue and are processed in order.

## 3. Check the partition layout

The normal **Defaults** layout is:

- `DELL DIAG` — `50 MB` — `FAT32`
- `Win11 Boot` — `20 GB` — `NTFS`
- `IT SUPP` — `*` — `exFAT`

`*` means “use all remaining space.” Exactly one partition must use `*`.

To change the layout, edit the partition rows on the main screen. Use **+** to add a row, **−** to remove one, and the drag handle to reorder rows. Use the Settings button in the upper-left to edit and save the defaults restored by **Defaults**.

## 4. Add content

On the partition row that should receive content:

- Select **Files** for individual files.
- Select **Folders** for one or more folders.
- Select **XML** on an NTFS partition for an answer file.
- Select **Clear** to remove all content selections for that partition.

Folder contents are copied into the destination partition and merged in the order selected. An answer file is renamed to `Autounattend.xml` at the root of the NTFS partition.

## 5. Build

1. Review the drive cards, partition rows, preview, and selected sources.
2. Type `ERASE` in the confirmation box.
3. Select **Build USB Queue**.
4. Read the final confirmation and select **Yes** only when the targets are correct.

The app rechecks each drive before erasing it, then formats, copies, and verifies it. Do not disconnect drives or close the app while the status shows **Building**.

## 6. Finish and verify

When the queue completes, the dialog shows how many drives succeeded or failed. A failed drive does not stop later queued drives from running.

For a successful drive, safely eject it in Windows before removing it. If a drive fails, keep it connected, note the message, and collect the build log before retrying.

## Common fixes

| Problem | What to do |
|---|---|
| USB drive missing | Select **Refresh**, try another USB port, and confirm Windows sees the drive. |
| Build button disabled | Select a drive and type `ERASE` exactly. |
| Drive too small | Use a larger drive or reduce fixed partition sizes. |
| Source not found | Reconnect the source or choose the file/folder again. |
| Invalid partition settings | Use sizes such as `50 MB` or `20 GB`; ensure exactly one row uses `*`. |

## Logs

Build and crash logs are stored at:

`%LOCALAPPDATA%\LaptopQAUsbBuilder\Logs`
