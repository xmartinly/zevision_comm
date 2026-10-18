# Zevision User Guide

Zevision communicates with the INFICON IMC300 over RS232 or TCP (Ethernet). It sends protocol commands and saves, restores and edits the instrument configuration.

> This software is for communication testing and configuration management. Do not operate the instrument while a configuration is being restored or edited.

Version 0.10.0 · INFICON · Author martin.liang@inficon.com

## Installation

1. Extract the release package to a writable folder (for example `D:\Zevision`) and run `Zevision.exe`; there is no setup program.
2. The program needs .NET Framework 4.7.2 or later. Windows 10 (1803 or later) and Windows 11 include it. If Windows 7 SP1, Windows 8.1 or an older Windows 10 reports that .NET Framework is missing, run `dotnet\ndp48-x86-x64-allos-enu.exe` from the release package (the .NET Framework 4.8 offline installer: no internet connection needed, requires administrator rights, may ask for a restart), then start the program again.

## Language

The program uses Chinese when Windows is set to Chinese or the region is China, and English otherwise.

Click the **EN** / **中** button at the right of the **Data display** header to switch the language. After confirmation the program saves its settings and restarts automatically (an open connection is closed; connect again after the restart). The choice is stored as `"language"` in `settings.json` (`"zh"`, `"en"`; `"auto"` selects automatically).

## Connecting

In **Connection** on the left, click the **RS232** or **TCP** tab. On connect, the program opens a serial or network connection according to the selected tab.

**RS232 (serial)**

1. Connect the PC to the IMC300 with a serial cable (or a USB serial adapter).
2. Select the **Port**. After plugging a USB serial adapter in or out, click the refresh button next to the port.
3. The **Baud rate** must match device setting G1303 (factory default 115200). The format is fixed at 8N1.
4. **Checksum** and **Length bytes** must match device settings G1304 and G1305.

**TCP (Ethernet)**

1. Connect the PC to the Ethernet port of the IMC300 with a network cable. The PC and the device must be on the same subnet.
2. Enter the device **IP address** (device G1402). The port is fixed at 2101. The device Ethernet protocol type (G1406) must be Zevision.
3. **Checksum** and **Length bytes** must match device settings G1407 and G1408.
4. TCP is much faster than serial: reading the full configuration (about 116 KB) no longer takes about 10 s.

Then click **Connect**. While connected, the connection settings are locked; disconnect to change them. If checksum or length bytes do not match the device settings, the device cannot recognize commands.

Hover over a setting to see its description. The connection settings are saved when the program closes and restored the next time it starts.

## Sending commands

- Type a command in the box at the bottom and press Enter or click **Send**, e.g. `H01`. The delimiters `{ }`, the length and the checksum are added automatically. The command part is converted to upper case when sending (data after `:` is unchanged, e.g. `um0101:Aluminium` is sent as `UM0101:Aluminium`); the input box shows the converted text and keeps it, so it can be edited and sent again.
- Separate several commands with `;`, or put one per line (Shift+Enter starts a new line), to send them in one frame, e.g. `QG01;QG1304;QG1305`. The program shows "command = data" for each one.
- The help command `?` can be sent manually. The full `?` reply is about 116 KB and takes about 10 s over RS232; no other commands can be sent meanwhile. Group help such as `?M` (materials) or `?F01` (film 1) returns only that part. To save the complete configuration, use **Save**.
- The quick command buttons send common commands directly.

### Send options (left of the input box; can be changed while connected)

- **Display format**: HEX, ASCII (text, default) or Binary. Applies to data sent and received afterwards; lines already shown do not change.
- **Repeat send**: when checked, **Send** repeats the command in the input box at the **Interval** (100–2000 ms): after each reply (or timeout) the program waits for the interval and sends again. The **Send** button becomes **Stop**; click **Stop**, uncheck the box or disconnect to stop. While repeating, quick commands and Save/Restore/Edit are not available.

## Data display

- TX is data sent, RX is data received, ERR is an error.
- The display scrolls automatically while it is at the bottom. Scrolling up pauses this; scrolling back to the bottom resumes it.
- Select lines and press Ctrl+C to copy, Ctrl+A to select all. **Clear** clears the display and the counters.

## Instrument configuration

Available while connected:

| Button | Function |
|---|---|
| **Save** | Reads the current device configuration (about 10 s over RS232) and saves it as a JSON file, by default in the `recipe` folder of the program directory |
| **Restore** | Choose a configuration file; it is compared with the device, and after confirmation the differing settings are written and read back to verify them |
| **Edit** | First choose the source in the drop-down on the left. **Device data**: read the current device configuration, edit it and write to the device only. **Saved configuration**: choose a configuration file, edit it, write to the device and save the file |

- Configuration files carry an MD5 checksum; files edited by hand are refused. Use **Edit** to change a configuration.

### Edit configuration window

- **Information** tab: device information, process status, object counts, and the current state of sensors, sources, inputs and outputs. With **Device data** the values come from the configuration just read; click **Refresh** for current values.
- **Settings** tab: one tab per category (System / Materials / Films / Layers / Processes / Sensors / Sources / I/O / Logic), grouped per object inside. It shows the current device values (Device data) or the values in the file (Saved configuration).
  - With **Device data** the window stays open after a successful write, so you can keep editing. No configuration file is created or changed; use **Save** to save one.
  - The Materials, Films, Layers and Processes tabs have **New** and **Delete** with both sources. With Device data they change the device only; with a saved configuration they change the device and the configuration file, which is saved immediately (a new object gets the first number unused on both; deleting an object that is not on the device removes it from the file only; adding or removing process layers, film pre/post conditions or layer films requires the same number of items in that list on the device and in the file, otherwise restore the file first):
    - **New** creates one with the first free number (with M01 and M03 present it creates M02) using the device defaults, then reads the tab again and expands the new object with all its settings checked, so you can edit them and save.
    - **Delete** (right of each object's title) deletes it from the device after confirmation; it cannot be undone. Only the current tab is read again.
    - References are checked first (a process contains layers, a layer contains films, a film contains a material): a material used by a film, a film used by a layer, a layer used by a process, or a process used as the active or linked process cannot be deleted. The message lists the referencing settings (owner, name, ID and value); change them to another object (or 0 = none) and save, then delete.
    - New film: the material ID is set to the first material and all the film's settings are checked, so you can edit them and save. Without materials the material ID keeps the device default.
    - New layer: a film entry (the first film) is added and all the layer's settings (name, film, rate, final thickness) are checked, so you can edit them and save. Nothing is added when there is no film (add one later with **Add rate/thickness**).
    - New process: the first layer is added and all the process's settings are checked. Nothing is added when there is no layer.
    - Process layers: each process header has a **Layer** drop-down (the existing layers) and an **Add** button. Choose a layer and click **Add**; after confirmation it is appended to the end of the list on the device (written and read back), and the Processes tab is read again. Each layer row ends with **×**, which removes that layer. The device can only remove the last item, so removing a layer in the middle moves the following layers up one place (each written and verified) and then removes the last item.
    - Film pre/post conditions: the film header has **+Pre Condition**, **−Pre Condition**, **+Post Condition** and **−Post Condition**. **+** appends one to the end of the list on the device with the device defaults; **−** removes the last one (disabled when the list is empty). The Films tab is read again afterwards (about 5 s) so you can check and edit the new one.
    - Layer rate and thickness: on the device a layer's rate and final thickness belong to each film in the layer (layer → film list → film ID, rate, final thickness). A new layer has an empty film list, so it shows only its name; the group shows a hint and an **Add rate/thickness** button (same as **+Film** in the header). After adding an item, check and edit its film, rate and final thickness; **−Film** removes the last one.
    - These adds/removes act on the device immediately (no Save needed) and are not available while settings are checked.
  - Working in the window:
    - Click a setting's value to check it and start editing (the input box gets the focus with its text selected, so typing replaces it); checking the box also moves the focus to the input box.
    - Shortcuts: **Ctrl+S** saves, **Ctrl+F** jumps to the filter box, **Esc** closes the window (cancels writing while writing).
    - When you close the window with modified settings that have not been saved, it asks whether to discard them; settings that are only checked do not count.
    - Each category tab keeps its own scroll position, also when the page is rebuilt after New, Delete or Save.
    - Not available while settings are checked (save or uncheck them first); the device must be Ready (idle).
    - The program checks the result and reports it if the new object did not appear, the deleted one is still there, or other objects changed.
  - Each row is "name (unit) | value | ID". In English the name is the description the device reports in its help table. Hover over a row to see the range or options.
  - Check a row to edit it. Settings with fixed options (such as on/off or a material) use a drop-down; others are typed in.
  - Numbers are written with the number of decimal places of the original value (5.89 is written as 5.890). Times use hours:minutes:seconds, e.g. 0:01:30.
  - Invalid values are shown in red; hover over them to see why. Saving is not possible while a value is invalid.
- Process control, system time and communication settings cannot be edited.

### Restore

- Order: system settings → materials → films → layers → processes. Materials, films etc. deleted on the device are re-created.
- The confirmation lists the objects to create, the settings to change, and the settings that will not be written (with the reason).
- Restoring stops at the first error; settings already written are not rolled back.
- Each phase is limited to 60 s; click **Cancel** at any time.
- Communication settings (G13/G14) and process control (C) are never restored.

## Files (in the program directory)

| File | Contents |
|---|---|
| `settings.json` | Connection settings and language |
| `recipe\*.json` | Instrument configuration files |
| `logs\error.txt` | Error log (appended; moved to `error.1.txt` above 5 MB) |
| `logs\capture_*.txt` | While "Save communication data" is on: the data display content of the connection (in the display format selected at the time), appended after each exchange, for diagnosing communication problems |

If the program directory is not writable, these files are stored in `Documents\Zevision\` instead.

## Troubleshooting

- **Timed out waiting for a response**: check the port and baud rate (for TCP: the IP address), whether checksum/length bytes match the device, and the cable.
- **Cannot connect to IP:2101**: check the network cable, the IP address, and that the PC and the device are on the same subnet. The connection times out after 5 s without a response.
- **Connection lost**: the cable was unplugged or the port is used by another program. Connect again.
- **Starting the program again opens no new window**: only one instance can run; the open window is brought to the front.
- When reporting a problem, attach `logs\error.txt`. For communication problems, turn on "Save communication data" (select HEX as the display format for byte-level analysis), reproduce the problem and attach the matching `capture_*.txt`.
