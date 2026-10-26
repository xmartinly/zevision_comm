# Zevision Software User Manual

| Item | Content |
|---|---|
| Document identifier | ZEV-SUM-01-EN |
| Document version | 1.5 |
| Applicable software | Zevision 1.3.3 (user interface language: English) |
| Prepared by | INFICON |
| Date | 2026-10-26 |

## 1 Scope

### 1.1 Identification

This document applies to the following software:

- Software name: Zevision (window title `IMC300 Communication Test - Zevision 1.3.3`);
- Software version: 1.3.3;
- Contact: martin.liang@inficon.com.

The user manual for the Chinese user interface is ZEV-SUM-01 (docs/USER_GUIDE.md). Its content corresponds to this document section by section.

### 1.2 System overview

Zevision is host software for Windows. It communicates with the INFICON IMC300 thin film controller over an RS232 serial port or Ethernet (TCP) using the Zevision protocol, and is used to:

- send protocol commands and display the data sent and received together with the parsed results;
- save, restore and edit the instrument configuration.

The software is intended for communication testing and configuration management. The instrument shall not be operated while a configuration is being restored or edited.

### 1.3 Document overview

This document follows the content requirements for a Software User Manual (SUM) in GB/T 8567-2006. It describes installation, start-up, operation, data backup, recovery from errors, and messages.

Tailoring: clause 3.6 (security and privacy), clause 4.1.2 (access control) and clause 5.4 (related processing) do not apply and have been removed; all other clauses keep the numbering of GB/T 8567-2006.

## 2 Referenced documents

Table 1  Referenced documents

| Identifier | Title | Location |
|---|---|---|
| GB/T 8567-2006 | Specification for computer software documentation | — |
| ZEV-SUM-01 | Zevision 软件用户手册 (Chinese user manual) | docs/USER_GUIDE.md |
| ZEV-OPS-01 | Zevision 部署与运维手册 (deployment and operations manual) | docs/部署与运维手册.md |
| ZEV-IDD-01 | IMC300 Zevision 通信协议规格书 (protocol specification) | docs/协议规格书.md |
| ZEV-SVD-01 | Zevision 软件版本说明 (version description) | docs/RELEASE.md |
| 074-786-P1A | IMC300 Operating Manual, chapter 7 | docs/zevision_comm.pdf |

## 3 Software summary

### 3.1 Software application

The software provides the following functions:

- connection to one IMC300 over RS232 or TCP; on connection, confirmation that the device answers and determination of the frame format from the actual device settings (handshake on connect);
- sending of single or multiple protocol commands, with display of the data sent and received as ASCII, hexadecimal or binary;
- reading of the device configuration and saving it as a configuration file;
- restoring of a configuration file to the device;
- editing of the current device configuration or of a configuration file, including creating and deleting materials, films, layers, processes and their list items.

### 3.2 Software inventory

Table 2  Contents of the release package

| File or folder | Description |
|---|---|
| `Zevision.exe` | Main program |
| `Zevision.exe.config` | Program configuration (runtime version, location of the `lib` folder) |
| `Texts.json` | Chinese and English texts of messages; when it is missing or damaged, the software shows a message and exits |
| `materials.json` | Material library: the material table of Annex A of the IMC300 operating manual (formula, name, density, Z ratio), selectable in the process editor (see 5.3.12) |
| `使用说明.md`, `User Guide.md` | User manuals (Chinese, English) |
| `lib\` | All DLL files used by the program |
| `dotnet\ndp48-x86-x64-allos-enu.exe` | .NET Framework 4.8 offline installer; included only in the `Zevision-<version>_wDotnet.zip` release package |

### 3.3 Software environment

Table 3  Operating environment

| Item | Requirement |
|---|---|
| Operating system | Windows 7 SP1 or later |
| Runtime | .NET Framework 4.7.2 or later (included in Windows 10 1803 or later and in Windows 11) |
| Interface | RS232 serial port (or a USB serial adapter), or an Ethernet interface on the same subnet as the device |
| Storage | The program directory should be writable; otherwise the data files are stored in `Documents\Zevision\` |
| Device | INFICON IMC300 (verified firmware: 1.8.11) |

Only one instance of the software runs on a computer.

### 3.4 Software organization and overview of operation

The main window has a left and a right area; Table 4 lists their content.

Table 4  Areas of the main window

| Area | Content |
|---|---|
| Left, "Connection" | "RS232" and "TCP" tabs; port and baud rate, or IP address |
| Left, "Protocol options" | "Response timeout (ms)"; switches "Checksum", "Length bytes", "Handshake on connect", "Reconnect automatically", "Save communication data" |
| Left, "Instrument configuration" | "Save", "Restore", "Process" and "Edit" buttons, the drop-down for the data source to edit, operation progress and "Cancel" button |
| Bottom left | "Connect" and "Disconnect" buttons, status line |
| Right, "Data display" | Records of the data sent and received, TX, RX and ERR counters, "Clear" button, language button "中" (or "EN" in the Chinese interface), GitHub icon button (opens the project page `https://github.com/xmartinly/zevision_comm` in the default browser) |
| Bottom right | "Display format", "Repeat send", "Interval (ms)", quick command buttons, command input box, "Send" button |

"Edit" opens the edit configuration window with the "Information" and "Settings" tabs, see 5.3.11.

Hovering the mouse pointer over a setting shows a short description.

### 3.5 Contingencies and alternate states and modes of operation

- When the connection is lost unexpectedly, the software records the reason in the data display and returns to the disconnected state; with "Reconnect automatically" on, it reconnects as described in 5.3.4. A save, restore, edit or repeat send in progress at that time does not resume.
- Restoring a configuration and saving edits stop at the first error; settings already written to the device are not rolled back.
- Each phase of save, restore and edit is limited to 60 s; the operation is cancelled when the limit is reached.
- With "Handshake on connect" off, the software only opens the channel, uses the frame format of the "Checksum" and "Length bytes" switches, and performs no handshake, no resends and no heartbeat. This mode is used to observe the raw replies of the device when the frame formats do not match.

### 3.7 Assistance and problem reporting

- Hovering the mouse pointer over a setting shows a short description.
- A problem report shall include `logs\error.txt`. For communication problems, turn on "Save communication data", reproduce the problem (set "Display format" to HEX for byte-level analysis) and include the matching communication data file (`logs\<serial number>_<date>_<time>.txt`).
- Troubleshooting of the communication link is described in ZEV-OPS-01.

## 4 Access to the software

### 4.1 First-time user

#### 4.1.1 Installation and setup

1. Select the release package: on a computer with .NET Framework 4.7.2 or later (included in Windows 10 1803 and later) use `Zevision-<version>_woDotnet.zip`; otherwise use `Zevision-<version>_wDotnet.zip`, which contains the offline installer.
2. Extract the release package to a writable folder, for example `D:\Zevision`. The software has no setup program.
3. Run `Zevision.exe`.
4. If a message reports that .NET Framework is missing (Windows 7 SP1, Windows 8.1, or Windows 10 earlier than 1803), run `dotnet\ndp48-x86-x64-allos-enu.exe` from the `_wDotnet` release package and start the software again. The installer needs no internet connection, requires administrator rights and may require a restart of the computer.

On the first start the user interface language is determined as described in 5.3.1, and all other settings have their default values; the settings are saved to `settings.json` on exit.

### 4.2 Initiating a session

1. Run `Zevision.exe`.
2. The software restores the settings saved last time; the data display shows a ready message and the status line shows `Not connected`.

When the software is started while it is already running, no new window opens; the existing window is brought to the front.

### 4.3 Stopping and suspending work

- Closing the main window exits the software. While connected, the software first cancels a configuration operation in progress, stops repeat sending and automatic reconnection, disconnects, closes the communication data file and then exits.
- The current settings are saved on exit.
- Switching the user interface language restarts the software (see 5.3.1).

## 5 Processing reference guide

### 5.1 Capabilities

Table 5  Capabilities of the software

| Capability | Description | Clause |
|---|---|---|
| Connection | RS232 or TCP; handshake on connect with the frame format taken from the device; resends after transmission errors; heartbeat when idle; optional automatic reconnection | 5.3.2 to 5.3.5 |
| Sending commands | Single or multiple commands in one frame; optional repeat at an interval | 5.3.6, 5.3.7 |
| Data display | ASCII, hexadecimal, binary; copying; optional saving to a file | 5.3.8 |
| Saving the configuration | Reads the device configuration and saves it as a JSON file with an MD5 checksum | 5.3.9 |
| Restoring the configuration | Compares file and device, writes the differing settings and reads them back; re-creates objects deleted on the device | 5.3.10 |
| Editing the configuration | Edits the current device configuration or a configuration file; creates and deletes objects and list items | 5.3.11 |
| Process editor | Writes materials and processes, saves them locally and uploads them to the device; creates the materials, films, layers and process automatically; lists and deletes the processes and materials on the device | 5.3.12 |

### 5.2 Conventions

- Names of user interface elements are written in quotation marks as shown on the screen, for example "Connect".
- Commands, setting IDs, file names, paths and messages shown by the software are written in code format, for example `H01`, `G1304`, `settings.json`.
- "Device" means the connected IMC300.
- "Frame format" means whether a frame carries a checksum and length bytes. For the RS232 interface it is defined by device settings G1304 (checksum) and G1305 (length), for the Ethernet interface by G1407 and G1408; the two are independent.
- "Click" means a click with the left mouse button.

### 5.3 Processing procedures

#### 5.3.1 User interface language

The user interface is Chinese when the Windows display language is Chinese or the region format is China, and English otherwise.

To switch the language:

1. Click "中" at the right of the "Data display" header ("EN" in the Chinese interface).
2. Confirm in the confirmation box.

The software saves its settings and restarts automatically; an open connection is closed first and has to be re-established after the restart. The language is stored as `language` in `settings.json`, with the values `zh`, `en` or `auto` (selected from the system).

#### 5.3.2 Establishing a connection

RS232 connection:

1. Connect the computer to the IMC300 with a serial cable or a USB serial adapter.
2. Click the "RS232" tab.
3. Select the "Port". After plugging a USB serial adapter in or out, click the refresh button next to the port to rescan.
4. Select the "Baud rate"; it shall match device setting G1303 (factory default 115200 bit/s). The options are 9600, 19200, 38400, 57600 and 115200 bit/s; the format is fixed at 8N1.
5. Click "Connect".

TCP connection:

1. Connect the computer to the Ethernet port of the IMC300 with a network cable; the computer and the device shall be on the same subnet.
2. Click the "TCP" tab.
3. Enter the device "IP address" (device setting G1402). The port is fixed at 2101. The Ethernet protocol type of the device (G1406) shall be Zevision.
4. Click "Connect".

Connection parameters:

- "Response timeout (ms)" offers 500, 1000 (default), 2000 and 5000 ms. It is an idle timeout: a timeout occurs when no new data arrives within this time after sending or after the last data received; large replies that keep arriving do not time out.
- "Checksum" and "Length bytes" correspond to device settings G1304 and G1305 (G1407 and G1408 for TCP). With "Handshake on connect" on, the software adjusts these switches to the actual device settings after connecting.
- A TCP connection attempt fails when there is no response within 5 s.
- TCP is faster than RS232: reading the full configuration (about 116 KB) takes about 10 s over RS232 and considerably less over TCP.

While connected, "Connection" and "Protocol options" are locked; they can be changed after disconnecting.

"Checksum", "Length bytes" and "Save communication data" are stored separately for RS232 and TCP; switching tabs shows the last settings of that connection type. "Handshake on connect" and "Reconnect automatically" are shared by both connection types. All settings are saved on exit.

#### 5.3.3 Handshake on connect

"Handshake on connect" is on by default. When it is on, the software proceeds as follows:

1. After connecting, it sends `H01` to confirm that the device answers and determines the frame format from the actual device settings. If they differ from the switches, the software uses the device settings, updates the switches and shows `The device's frame format differs from the protocol options; now using the device's settings.`.
2. It reads the device model (G03) and the frame settings. If the model is not IMC300, or the settings reported by the device differ from the frame format actually in use, the software shows a notice and continues.
3. When all steps succeed, the status line shows `Connected to …`, and the data display shows the connection parameters and `Device: Zevision IMC300 …`.

The exchange during the handshake is not displayed. If the device does not answer or the frame format cannot be detected, the software does not establish the connection and shows `Handshake failed: …` with the reason.

While connected (handshake on):

- After a transmission error (timeout, invalid frame, or device reply checksum error C, length error L or timeout T), the software resends the command automatically, at most 2 times, and shows `Retry 1/2 of …` each time. Commands that add (A), delete (D) or run an action (R) are not resent.
- When the connection is idle, a hidden heartbeat (`H01`) is sent every 5 s. When the device does not answer 3 heartbeats in a row, the software treats the connection as lost. This detects unplugged serial and network cables.
- After a frame setting command sent from the input box (for example `UG1304:1`) is accepted by the device, the software handshakes again with the new frame format, updates the switches and shows `Now using the device's new frame format: …`. If this handshake fails, the software disconnects.
- When the device answers F (state does not allow the command), the software reads and shows the current device state, for example `Current device state (C09): Process`. The software does not change the device state.

With "Handshake on connect" off, the software only opens the channel, uses the frame format of the switches, and performs no handshake, no resends, no heartbeat and no state query. After the device's frame settings are changed, the software only shows a notice; disconnect, change "Protocol options" and connect again.

#### 5.3.4 Automatic reconnection

"Reconnect automatically" is off by default. When it is on, the software reconnects after an unexpected connection loss at the following intervals until it succeeds: 1 s, 2 s, 4 s, 8 s, 16 s, 32 s, then every 60 s. Each reconnection includes a new handshake.

- While waiting, the status line shows `Connection lost; reconnection attempt n in x s …`, the status dot is orange (green while connected), and "Connection" and "Protocol options" remain locked.
- A failed attempt shows `Reconnection attempt n failed: …` and is not written to the error log.
- A successful reconnection shows `Reconnected.`.
- Clicking "Disconnect" stops automatic reconnection and shows `Automatic reconnection stopped.`.
- A save, restore, edit or repeat send interrupted by the connection loss does not resume.

#### 5.3.5 Disconnecting

Click "Disconnect". The exchange in progress completes, the connection closes, repeat sending stops and the status line shows `Not connected`. "Disconnect" is not available while a save, restore or edit is in progress.

#### 5.3.6 Sending commands

1. Enter a command in the command input box, for example `H01`.
2. Press Enter or click "Send".

Rules:

- The delimiters `{ }`, the length bytes and the checksum are added by the software.
- When sending, the command part of each command is converted to upper case; data after `:` is unchanged, for example `um0101:Aluminium` is sent as `UM0101:Aluminium`. The input box shows the converted text and keeps it, so that it can be edited and sent again. The last text sent from the input box is saved on exit and shown in the input box at the next start; quick commands and edits that were not sent are not saved.
- Multiple commands are separated by `;` (or the full-width `；`) or written one per line (Shift+Enter starts a new line) and sent in one frame, for example `QG01;QG1304;QG1305`. The software shows `command = data` for each command.
- The help command `?` can be sent manually. The full `?` reply is about 116 KB and takes about 10 s over RS232; no other command can be sent meanwhile. Group help (for example `?M`) returns that group only. To save the complete configuration, use "Save" (5.3.9).
- Help commands (`?`, `?M` and so on) are displayed only when sent from the input box. The help commands that the software uses to read the device during save, restore and edit, and their replies, are not displayed and not saved to the communication data file.
- The quick command buttons send the following common commands: `H01`, `H02`, `QG01`, and the frame setting queries of the current connection type (`QG1304` and `QG1305` for RS232, `QG1407` and `QG1408` for TCP).

#### 5.3.7 Send options

The send options are located to the left of the command input box; they can be changed while connected and are saved on exit.

- "Display format": HEX (hexadecimal, 32 bytes per line), ASCII (text; non-printable bytes are shown as `<XX>`; default), Binary (16 bytes per line). The setting applies to data sent and received afterwards; lines already shown do not change.
- "Repeat send": with the box checked, "Send" repeats the command in the input box at the "Interval (ms)"; after each reply or timeout the software waits for the interval and sends again. The interval options are 100, 200, 500, 1000 (default), 1500 and 2000 ms. The "Send" button changes to "Stop"; clicking "Stop", unchecking the box or disconnecting stops the repetition. While repeating, quick commands and save, restore and edit are not available.

#### 5.3.8 Data display

- TX denotes data sent, RX data received, ERR an error and INFO information. Error lines are also written to `logs\error.txt`.
- The display scrolls automatically while it is at the bottom; scrolling up pauses automatic scrolling, scrolling back to the bottom resumes it.
- The header shows TX (frames sent), RX (frames received) and ERR (failed commands). A failed command is a command without a successful response: a command answered with a negative response (`-A`, `-B` and so on), and every command of a frame that received no valid response (timeout, invalid response). Commands that succeed after a resend, the handshake and the heartbeat are not counted. ERR is shown in the error color when greater than 0.
- Select lines and press Ctrl+C to copy them; Ctrl+A selects all. "Clear" clears the display and the TX, RX and ERR counters.
- The display keeps at most 20000 lines; when this is exceeded, the oldest 2000 lines are removed.
- While the software is not in the foreground (another program's window is active, or the main window is minimized), the display is refreshed when the communication data is saved: each time the displayed content grows by 50 KB (the moment the communication data file is written; the same 50 KB count applies when "Save communication data" is off), and, with "Save communication data" on, when the connection ends (including a lost connection). On returning to the foreground all content is shown at once. The TX, RX and ERR counters in the header are refreshed together with the display. Saved communication data and `error.txt` are not affected.
- With "Save communication data" on, the content shown during the connection (in the display format selected at the time) is kept in memory and written to a communication data file in the `logs` folder when it exceeds 50 KB, when the connection ends (including a lost connection) and when the program exits. The file name consists of the device's system serial number (G06) and the date and time of the connection (to the minute), for example `XXXXXXXX_20261021_1337.txt`; with "Handshake on connect" off, or when the device does not return a serial number, the serial number part is `unknown`. A connection within the same minute appends to the same file. When a file exceeds 30 MB, the following content is written to a file named after it with `_1`, `_2`… appended (for example `XXXXXXXX_20261021_1337_1.txt`). Content not yet written is lost if the program terminates abnormally. The switch takes effect on connecting.

#### 5.3.9 Saving the configuration

1. Connect to the device.
2. Click "Save". The software reads the current device configuration (about 10 s over RS232).
3. Confirm the file name and location in the save dialog. The default location is the `configs` folder in the program directory; the default file name is `IMC300_yyyyMMdd_HHmmss.json`.

A successful save shows no message box; the result is recorded in the data display. When reading the device fails or times out, the reason is shown as a red error line in the data display (and written to `logs\error.txt`) without a message box; a failure to write the file shows a message box. The reading phase is limited to 60 s and can be stopped with "Cancel".

#### 5.3.10 Restoring the configuration

1. Connect to the device. The device shall be in the Ready (idle) state and its model shall match the file.
2. Click "Restore" and choose a configuration file.
3. The software reads the device configuration and compares it with the file. The confirmation box lists the objects to be created, the settings to be changed and the settings that will not be written (with the reason, for example a referenced material that does not exist or a crucible number above the number of crucibles of the source).
4. After confirmation, the software writes the settings and reads each of them back for verification.

Rules:

- Write order: system settings, materials, films, layers, processes. Materials, films, layers and processes deleted on the device are re-created from the file; missing list entries are appended at the end of the list.
- Only settings that differ from the device are written. Objects and entries present on the device but not in the file are not deleted.
- Restoring stops at the first error; settings already written are not rolled back.
- The reading phase and the execution phase are each limited to 60 s; time with the confirmation box open is not counted. "Cancel" can be clicked at any time.
- Communication settings (G13, G14) and process control (C) are not restored.
- If the firmware version differs from the file, the confirmation box shows a notice.
- A successful restore shows no message box; the result is recorded in the data display. Problems that leave the device unchanged (reading the device failed or timed out, an invalid configuration file, a different model, the device not Ready) are shown as red error lines in the data display without a message box; write errors, and a restore that finished with settings that could not be written, show a message box.

#### 5.3.11 Editing the configuration

##### 5.3.11.1 Selecting the data source and opening the edit window

1. Select the data source in the drop-down to the left of "Edit":
   - "Device data": the current device configuration is read (about 10 s over RS232, limited to 60 s) and edited; changes are written to the device only, and no configuration file is created or changed;
   - "Saved configuration": a configuration file is chosen and edited; changes are written to the device, and the file is saved when all writes succeed.
2. Click "Edit".

Configuration files carry an MD5 checksum; files edited by hand are refused. Configuration files shall be changed with this function.

Process control, system time and communication settings cannot be edited. Settings that can only be queried are not shown.

##### 5.3.11.2 "Information" tab

The "Information" tab shows device information, RS232 settings, process status (C09 to C21), object counts, and the position and state of sensors, sources, inputs and outputs. With "Device data" the values are those of the configuration read when the window opened; with "Saved configuration" they are read from the device when the window opens. "Refresh" reads them from the device again. The object counts are not updated automatically after New or Delete; "Refresh" updates them.

##### 5.3.11.3 "Settings" tab

The "Settings" tab has one tab per category: System, Materials, Films, Layers, Processes, Sensors, Sources, I/O, Logic. Each category tab groups the settings per object; the group title contains the object name. Tabs with at most 3 groups start expanded. The values shown are the current device values ("Device data") or the values in the configuration file ("Saved configuration").

Table 6  Controls of the "Settings" tab

| Control | Function |
|---|---|
| Row | `name (unit) \| value \| ID`; in English the name is the description the device reports in its help table; hovering shows the ID and the range or options |
| Check box | Checking a row makes it editable; unchecking restores the value |
| "All" | Checks all visible settings of the group |
| "None" | Unchecks all settings of the group and restores their values |
| "Expand all", "Collapse all" | Expands or collapses all groups of the current tab |
| Filter box | Filters by ID, name, device description or value (Ctrl+F); matching groups are expanded automatically when there are at most 20 |
| "Save" | Writes the checked settings to the device and reads them back (Ctrl+S) |
| "Cancel writing" | Cancels while writing (Esc) |
| "Close" | Closes the window (Esc) |

Rules for editing values:

- Clicking the value of a setting checks it and moves the focus to its input box with the text selected, so that typing replaces the value; checking the check box also moves the focus to the input box.
- Settings with fixed options (for example on/off or a material) use a drop-down; other settings are typed in. A current value that is not among the options is shown as `(unknown)`; a reference to an object that does not exist is shown as `(missing)`; the value is kept.
- A film's crucible: the drop-down offers `0 - Any` and 1 to the number of crucibles of the film's source (the source's "Number of Crucibles"; 1 when no source is selected). If the source is changed in the same save, the crucible is checked against the new source before writing; a crucible above the number of crucibles is not written and an error is shown.
- Numbers are written with the number of decimal places of the original value, for example 5.89 is written as 5.890. Times use `H:MM:SS`, for example `0:01:30`.
- Invalid values are shown in red; hovering over them shows the reason. Saving is not possible while a value is invalid. Reserved protocol characters are always checked; type and range are checked only after a value has been modified.

Saving edits:

1. Check and edit the settings.
2. Click "Save" or press Ctrl+S, and confirm in the confirmation box.
3. The software checks the device model and the Ready state, writes the settings in order and reads each of them back for verification.

Results:

- When all writes succeed, the checked settings are unchecked. With "Device data" the window stays open for further edits, and the page is refreshed with the new values (group titles and names in reference drop-downs are updated); with "Saved configuration" the configuration file is saved as well and the window closes.
- On failure the configuration file is unchanged, the window stays open and the settings remain checked.
- The window cannot be closed while writing.
- When the window is closed with modified settings that have not been saved, the software asks whether to discard them; settings that are only checked do not count.
- Each category tab keeps its own scroll position, also when switching tabs or after the page is rebuilt.

##### 5.3.11.4 Creating and deleting objects

The Materials, Films, Layers and Processes tabs provide New and Delete with both data sources:

- with "Device data", only the device is changed;
- with "Saved configuration", the device and the configuration file are changed, and the file is saved immediately. A new object gets the first number unused on both the device and the file; deleting an object that does not exist on the device removes it from the file only.

New (click the "New Material" or corresponding button of the tab):

1. Confirm in the confirmation box.
2. The software creates the object with the first free number (with M01 and M03 present it creates M02) using the device defaults, reads the tab again and expands the new object with all its settings checked, so that they can be edited and saved.

Default references of new objects:

- New film: the material ID is set to the material with the lowest number; without materials, the material ID keeps the device default.
- New layer: one film entry (the film with the lowest number) is added, and the layer's name, film, rate and final thickness are checked; without films, nothing is added, and an entry can be added later with "Add rate/thickness".
- New process: the layer with the lowest number is added; without layers, nothing is added.

Delete (click "Delete" to the right of the object title):

1. Confirm in the confirmation box. Deletion cannot be undone.
2. The software deletes the object from the device and reads the current tab only.

Before deleting, the software checks references (a process contains layers, a layer contains films, a film contains a material). A material used by a film, a film used by a layer, a layer used by a process, or a process used as the active or linked process cannot be deleted; the message box lists the referencing settings (owner, name, ID and value). These settings shall first be changed to another object (or 0 = none) and saved; then the object can be deleted.

##### 5.3.11.5 Adding and removing list items

- Process layers: each process group header has a "Layer" drop-down (the existing layers) and an "Add" button. Selecting a layer and clicking "Add" appends it to the end of the list on the device without further confirmation; the value is written and read back, and the Processes tab is read again. Each layer row ends with a "×" button, which removes that layer. The device can only remove the last item of a list; removing a layer in the middle moves the following layers up one place (each written and verified) and then removes the last item.
- Film pre and post conditions: the film group header has the buttons "+Pre Condition", "−Pre Condition", "+Post Condition" and "−Post Condition". "+" appends one item with the device defaults to the end of the list on the device without confirmation; "−" removes the last item and is not available when the list is empty. Afterwards the Films tab is read again (about 5 s), and the new item can be checked and edited.
- Layer rate and thickness: on the device, a layer's rate and final thickness belong to each film entry of the layer (layer, film list, film ID, rate, final thickness). A new layer has an empty film list; its group shows a hint and an "Add rate/thickness" button (the same as "+Film" in the header). After an entry has been added, its film, rate and final thickness can be checked and edited. "−Film" removes the last entry.

With "Saved configuration", adding or removing process layers, film pre and post conditions or layer films requires the same number of items in that list on the device and in the file; otherwise the file shall be restored first.

##### 5.3.11.6 Common rules

- New, Delete and the adding and removing of list items act on the device immediately; "Save" is not needed.
- These functions are not available while settings are checked; save or uncheck the settings first.
- The device shall be in the Ready (idle) state.
- The software verifies the result and shows a notice if a new object did not appear, a deleted object still exists, or other objects changed.
- Successful saves, New, Delete and list changes show no message box; the result is shown at the bottom of the window. Problems that leave the device and the files unchanged (for example an object that is referenced and cannot be deleted, no free number, reading the device failed) are shown in red at the bottom of the window without a message box; errors while writing to the device or a file show a message box.

#### 5.3.12 Process editor

The process editor simplifies setting up processes on the instrument: materials and processes are written locally in the way operators work and uploaded to the device with "Upload". Each material a process uses corresponds to one material and one film on the device, each step to one layer, and a new process is created whose layer list follows the order of the steps. Materials, films and layers that already exist on the device with the same name and parameters are reused; new ones are created only when the parameters differ (see 5.3.12.4).

##### 5.3.12.1 Opening the window

1. Connect to the device.
2. Click "Process" under "Instrument configuration". The software reads the device configuration (about 10 s) and opens the "Process editor" window.

The window has the pages "Materials", "Processes" and "Device". On the "Materials" and "Processes" pages the local list is on the left ("New", "Delete"); under each item a line shows whether the device has objects of the same name, for example "On the device: M10, F0B" (materials of the same name and the films of the same name that refer to them), "On the device: P0209" or "Not on the device". The selected item is edited on the right. The settings of a material are grouped into "Material", "Sensor and source", "Control", "Delay settings", "Pre conditions" and "Post conditions"; when the window is wide enough, two groups are shown side by side. The pre conditions, post conditions and the steps of a process are tables with one row per item; the button that adds a row is on the right of the group title. The "Device" page lists the processes and materials on the device (see 5.3.12.5). Materials and processes are stored in the `recipes` folder of the program directory (see 5.5).

##### 5.3.12.2 Materials

A material corresponds to a material on the device (density, Z ratio, tooling) and a film of the same name (all other settings). The name is written to the device and is limited to ASCII letters, digits and symbols, at most 30 characters. The Chinese name is taken from the material library, is shown in the Chinese user interface only (for example "Al 铝" in the material list and the material options of the steps), is not written to the device and cannot be entered by hand.

1. On the "Materials" page click "New". A new material has two pre conditions and one post condition; the PID values are P 10, I 0, D 0.
2. Enter the name, density, Z ratio and tooling, or use "Search library", the first box of the "Material" group: typing a keyword (part of the formula, the English name or the Chinese name, not case-sensitive, for example `si`) in the box shows only the matching materials and opens the list; materials whose formula equals the keyword come first, then those whose formula starts with it. Selecting an entry fills in the density, Z ratio and Chinese name, and also the formula as the name when the name is empty or was filled in from the library last time and not changed. The list shows "formula - name". When the name entered is a formula of the material library (case-sensitive, and the formula has only one entry), the software fills in the Chinese name of that entry, and also its density and Z ratio when they are empty or still hold values filled in from an earlier name (typing `SiO2` replaces the values filled in for `Si` on the way). When the name is no longer a formula, the Chinese name, density and Z ratio that were filled in from the name and not changed are cleared; values filled in by selecting from the library are kept when the name is changed.
3. Select the sensor, source and crucible and enter the crystal position. The crucible options are "0 - Any" and 1 up to the number of crucibles of the selected source; sensors and sources that do not exist on the device are shown as "(missing)".
4. Enter the P term, I term, D term and maximum power, and select the max power option (action when the maximum power is reached: 0 - Continue, 1 - Post deposit, 2 - End the process, 3 - Suspend process).
5. Select the delay option under "Delay settings". When it is not "0 - None", the other delay settings are shown: enter the control delay time, shutter delay accuracy and shutter delay timeout, select the transfer sensor ("0 - None" or a sensor of the device) and enter the transfer tooling. With delay option 0 these settings are neither shown nor written to the device.
6. In the "Pre conditions" and "Post conditions" tables, enter the ramp/rise time, ramp power and hold time of each condition; times use the format H:MM:SS. "Add pre condition" and "Add post condition" in the group titles and the "×" at the end of a row change the number of conditions.

A material used by a local process cannot be deleted (explained in red at the bottom of the window). When another material is deleted, the software asks whether to delete the matching data on the device as well and lists, according to the last read of the device, the objects to be deleted: the materials of the same name, the films that refer to them and the layers that use these films (whatever their names).

- "Yes": the software reads the materials, films, layers, processes and sources of the device again (`?M`, `?F`, `?L`, `?P`, `?S`, about 8 s). When a process on the device uses these layers (or other settings refer to them), the referring settings are listed at the bottom of the window and nothing is deleted. When the objects to be deleted differ from those listed in the question, the software asks again. The layers, films and materials are deleted in this order (higher numbers first within each kind) and these groups are read again to verify the deletion; when everything succeeded, the local material is deleted. When the device has no matching data, only the local material is deleted. When the device cannot be read, this is explained at the bottom of the window and nothing is deleted.
- "No": only the local material is deleted.
- "Cancel": nothing is deleted.

The local deletion is written to the files with "Save"; the deletion on the device takes effect at once and cannot be undone.

##### 5.3.12.3 Processes

1. On the "Processes" page click "New" and enter the name.
2. Click "Add step" in the title of the "Steps" group. In the steps table, select a material for each step and enter the rate and final thickness. A process can use several materials; a material can be used in several steps.
3. For a rate ramp, click "+ Rate ramp" of the step and enter the start thickness, ramp time and new rate in the new row below the step.

The rate, final thickness, start thickness and new rate have at most 3 decimal places (for example `5.125`) and shall be in the range of the device; the ramp time uses the format H:MM:SS. An invalid entry turns the input box red and the reason is shown below it (for example "At most 3 decimal places"); "Save" also lists these problems and saves nothing.
4. "↑" and "↓" change the order of the steps; "×" removes a step.
5. Select whether to "Auto Start Next" layer.

Layers created by an upload are named "material name-process name-step number", for example `Al-AR-1` for Al in step 1 of process AR. Steps of a process with the same values share one layer, named after the first of them. Reused layers keep their names on the device. Process names and generated layer names have at most 30 characters (the device stores up to 31, but reading the device configuration returns only the first 30); when a layer name is too long, "Save" lists the step, and the material or process name shall be shortened.

Deleting a process also deletes the processes of the same name on the device:

1. On the "Processes" page select the process and click "Delete". The software reads the processes and layers of the device (`?P`, `?L`, about 2 s).
2. When the linked process of another process (`P02**0A`) is this process, the software lists these settings at the bottom of the window and deletes nothing (not locally either). When this process is the active process of the device (`P01`), it can be deleted: the confirmation says so, the device has no active process afterwards (the device sets `P01` to 0), and the bottom of the window shows `P01` as read after the deletion.
3. The confirmation lists the processes of the same name and the layers only they use, which are deleted on the device; layers that other processes use as well are kept and listed. When the device has no process of this name, only the local deletion is confirmed.
4. After "OK" the software deletes the processes first, then the layers, and reads both groups again to verify the deletion; when everything succeeded, the local process is deleted. Materials and films are not deleted.

A process without a name is deleted locally only, without reading the device. When the device cannot be read (for example because the connection was lost), the software asks whether to delete only the local process; processes of the same name on the device are not affected.

The local deletion is written to the files with "Save"; the deletion on the device takes effect at once and cannot be undone.

##### 5.3.12.4 Saving and uploading

"Save" saves locally only and does not access the device; "Upload" saves first and then uploads the selected process to the device.

1. Click "Save" (Ctrl+S). The software checks all materials and processes: names shall not be empty or repeated, values shall be in the range of the device, and each process shall have at least one step with a material selected. Problems are listed in red at the bottom of the window and nothing is saved; otherwise the content is saved to the `recipes` folder.
2. To upload, select the process on the "Processes" page and click "Upload process name". The software saves as in step 1, reads the device configuration (about 10 s), checks that the selected sensors and sources exist on the device and that the crucible numbers do not exceed the number of crucibles of the sources, and assigns free numbers on the device. Failed checks are explained in red at the bottom of the window and nothing is uploaded.
3. The confirmation lists the materials, films and layers to be created and those to be reused, with their numbers, and the new process; when the device already has a process of the same name, it says that another one is created. "OK" uploads; "Cancel" does not upload (the content is already saved locally).
4. The software creates the objects, appends the list entries (pre and post conditions, sensors, the film of each layer, the layers of the process), writes all settings of the new objects and reads every setting back for verification (about 15 s to 25 s over RS232, about 3 s over Ethernet).

Rules:

- Reuse: a material on the device with the same name, density, Z ratio and tooling is reused. A film of a reused material with the same name and the same settings (number and values of the pre and post conditions, sensor, crystal position, source, crucible, PID, maximum power, max power option, delay option and delay settings) and exactly one sensor is reused. A layer of a reused film with only this film and the same rate, final thickness and rate ramps is reused; its name is not compared (it may have been created by another process or step). Otherwise a new object is created; new layers are named after this process and step. Numbers are compared by value (`5` equals `5.000`).
- Steps of a process with the same material, rate, final thickness and rate ramps share one layer.
- A new process is created on every upload. Reused objects are not written; objects already on the device are not changed. Uploading a process twice creates only a second process.
- The active process (`P01`) is not set.
- The device shall be in the Ready (idle) state.
- "Upload" is not available when no process is selected.
- When the upload fails (a write is rejected, a setting reads back differently from what was entered, settings still cannot be written after creating the objects, a timeout or a cancel), the software reads the device again, deletes the objects this upload created that are still on the device (processes, layers, films, materials; higher numbers first within a group) and reads again to verify. Reused objects and objects that were already on the device are not touched. This is then explained in red at the bottom of the window, recorded in the data display and shown in a message box with the reason and the deleted objects; the "Last upload" is not recorded. When the deletion fails (for example because the connection was lost) or is cancelled, the message box lists the objects that may still be on the device; they can be deleted on the "Device" page after "Refresh". When the device has not been changed yet (reading the device configuration failed or was cancelled), only the bottom of the window explains it.
- Problems that leave the device and the files unchanged (failed checks, reading the device failed, an object that is referenced and cannot be deleted) are shown in red at the bottom of the window without a message box; failures of operations that changed the device or a file show a message box. Errors and exceptions are written to `logs\error.txt`.
- After a successful upload, the process page shows the time and numbers of the "Last upload" (reused objects marked `*`), the data display records one line, and the "Device" page is updated with what was written. Success shows no message box; the result is shown at the bottom of the window. When the upload fails or is cancelled, the "Device" page asks for "Refresh".
- Reading the device configuration is limited to 60 s, and each stage of the upload (creating objects, reading again, writing, verifying) to 60 s; time with the confirmation open is not counted. Editing is not possible during the upload, and "Close" becomes "Cancel upload".
- When the window is closed with unsaved changes, the software asks first.
- The material library is `materials.json` in the program directory (format in Annex A). When the file contains no materials, "Search library" is not available.
- The files are stored in four folders under `recipes`, one UTF-8 JSON file per object, named after the object: `material\name.json` (material: name, Chinese name, density, Z ratio, tooling), `film\name.json` (film of the same name: sensor, source, crucible, pre and post conditions, PID, maximum power, delay settings), `layer\material name-process name-step number.json` (layer: material, rate, final thickness, rate ramps; one file per distinct step of a process, steps of a process with the same values share the file of the first one) and `process\name.json` (process: Auto Start Next, the layer files in order, the last upload). Characters not allowed in file names are replaced by `_`; equal names (not case-sensitive) get `_2`, `_3`… Saving rewrites only files whose content changed and deletes the `.json` files in these four folders that this program read or wrote and that no longer belong to an object (for example a deleted or renamed material); other files placed there while the window is open are kept.

##### 5.3.12.5 Device

The "Device" page is read-only and lists the processes and materials on the device; it does not change the local materials and processes:

- Processes: number and name, and below one line per layer in order, for example `1. L01 L1: F01 L1 (M01 M1), 0.000 Å/s, 1.030 kÅ` (layer, film, material, rate, final thickness). The films of a layer with several films are separated by semicolons; layers or films that do not exist on the device are marked "(missing)".
- Materials: number and name, and below the density, Z ratio and the films that refer to the material.

The top of the page shows the device model and the time of the last update. The page is updated when the window opens, with "Refresh" (reads the device configuration, about 10 s), and after uploads and deletions.

Deleting on the "Device" page:

- One process: click the delete button at the end of its row. The software deletes the process and the layers only it uses; the rules are those of 5.3.12.3.
- One material: click the delete button at the end of its row. The software deletes the material, the films that refer to it and the layers that use these films; nothing is deleted when a process on the device uses these layers, as in 5.3.12.2.
- All processes: click "Delete all" in the title of the process list. The software reads the processes and layers of the device again and deletes all processes and the layers they use; the active process is deleted too, and the device then has no active process.
- All materials: click "Delete all" in the title of the material list. The software reads the device again and deletes all materials, the films that refer to them and the layers that use these films; nothing is deleted while a process on the device uses these layers, so delete all processes first. Films that refer to no material and layers without these films are kept.

The software asks for confirmation first (for "Delete all" the question lists every object to be deleted); when an object is referenced or the device cannot be read, this is explained in red at the bottom of the window. The local materials and processes are not affected; the device status under the local items is updated after the deletion.

### 5.5 Data backup

- Configuration files are stored in the `configs` folder of the program directory (`Documents\Zevision\configs` when the program directory is not writable). A backup consists of a copy of this folder.
- Configuration files are UTF-8 JSON files with an MD5 checksum; files changed by hand are refused.
- Versions earlier than 1.1.0 use the folder `recipe`, versions 1.1.0 to 1.2.0 the folder `recipes`. The first time the software uses these folders, it renames the old folder to `configs`; when both exist, only `recipes` is renamed and `recipe` is kept; when `configs` already exists, the old folders are kept. If renaming fails (for example because a file in it is open in another program), the old files remain in their folder and can be selected in the open dialog. The `recipes` folder is then used by the process editor.
- The materials and processes of the process editor are stored in the `recipes` folder of the program directory (`Documents\Zevision\recipes` when the program directory is not writable), in the four folders `material`, `film`, `layer` and `process` (see 5.3.12.4). A backup consists of a copy of the `recipes` folder.
- `settings.json` holds the connection settings, the send options, the user interface language and the last sent command. When it is damaged, the software uses default values, reports this in the data display and overwrites the file on exit.
- Annex A lists all file locations.

### 5.6 Error, malfunction and emergency recovery

Table 7  Common faults and remedies

| Symptom | Cause | Remedy |
|---|---|---|
| `Handshake failed: The device did not answer H01` | Wrong port, baud rate or IP address; cable not connected; device switched off | Check the port, the baud rate (for TCP the IP address), the cable and the device power |
| `Handshake failed: Cannot detect the device's frame format` | Usually a wrong baud rate | Check device setting G1303; to see the raw data sent by the device, turn off "Handshake on connect" and connect |
| `Timed out waiting for a response` | Wrong port, baud rate or IP address; frame format different from the device; cable problem | Check the port, the baud rate (for TCP the IP address), the checksum and length byte settings, and the cable |
| `Cannot connect to <IP>:2101` | Wrong network cable, IP address or subnet | Check the network cable, the IP address, and that the computer and the device are on the same subnet |
| `Connection lost: …` | Cable unplugged, port used by another program, or 3 heartbeats not answered | Connect again; turn on "Reconnect automatically" for automatic recovery |
| Access to the port is denied when reconnecting right after disconnecting | Windows has not yet released the serial port | Wait 1 s to 2 s and connect again |
| `Current device state (C09): Process` | The device is running a process and refuses changes (F) | Repeat the operation when the device is Ready |
| A configuration file is refused | The file was changed by hand; MD5 check failed | Use an unchanged file; change configurations with "Edit" |
| Starting the software again opens no new window | Only one instance can run | Use the window that has been brought to the front |

If an error interrupts a restore, the settings already written are not rolled back; correct the cause and run the restore again.

### 5.7 Messages

Table 8  Status line messages

| Message | Meaning |
|---|---|
| `Not connected` | No connection |
| `Connecting to <target> …` | The serial port or TCP connection is being opened |
| `Handshaking …` | The device and the frame format are being confirmed |
| `Connected to <target>` | Commands and configuration operations are available |
| `Connection lost; reconnection attempt n in x s …` | Waiting for automatic reconnection |

Table 9  Main messages in the data display

| Message | Type | Meaning |
|---|---|---|
| `Connected to <target>; Checksum (…) on/off, Length bytes (…) on/off, timeout … ms.` | INFO | Connection established, with the parameters in use |
| `Device: Zevision IMC300 …` | INFO | Device information read during the handshake |
| `The device's frame format differs from the protocol options; now using the device's settings.` | INFO | The switches were adjusted to the device during the handshake |
| `Handshake failed: …` | ERR | The handshake failed; the connection is closed |
| `Retry n/2 of <command>: <reason>` | INFO | Resend after a transmission error |
| `Connection lost: <reason>` | ERR | Unexpected loss of the connection |
| `Connection lost: The device did not answer 3 heartbeats in a row.` | ERR | Heartbeats not answered |
| `Reconnection attempt n failed: <reason>` | INFO | One automatic reconnection attempt failed |
| `Reconnected.` | INFO | Automatic reconnection succeeded |
| `Disconnected.`, `Automatic reconnection stopped.` | INFO | The user disconnected or stopped reconnection |
| `Now using the device's new frame format: …` | INFO | Handshake after a change of the frame settings succeeded |
| `Current device state (C09): <state>` | INFO | Current state after a device reply F |
| `Communication data is saved to: <path>` | INFO | File location with "Save communication data" on |

### 5.8 Quick reference guide

Table 10  Keyboard shortcuts

| Location | Key | Function |
|---|---|---|
| Command input box | Enter | Send |
| Command input box | Shift+Enter | New line |
| Data display | Ctrl+A | Select all |
| Data display | Ctrl+C | Copy the selected lines |
| Edit window | Ctrl+S | Save |
| Edit window | Ctrl+F | Move to the filter box |
| Edit window | Esc | Close the window; cancel writing while writing |
| Process editor | Ctrl+S | Save locally (no upload) |
| Process editor | Esc | Close the window; cancel the upload while uploading |

## 6 Notes

Table 11  Terms and abbreviations

| Term or abbreviation | Meaning |
|---|---|
| IMC300 | INFICON thin film controller |
| Zevision protocol | Remote communication protocol of the IMC300; a frame starts with `{` and ends with `}` |
| Frame format | Whether a frame carries a checksum (1 byte) and length bytes (2 bytes) |
| Handshake | Procedure after connecting that confirms the device answers, determines the frame format and reads the model |
| Heartbeat | Hidden `H01` sent while the connection is idle to check the link |
| Resend | Sending the same command again after a transmission error |
| Configuration file | JSON file with the instrument configuration and an MD5 checksum |
| Device data | Data source for editing: the current device configuration |
| Saved configuration | Data source for editing: a configuration file |
| Ready | Idle state of the device (C09) |
| TX, RX, ERR, INFO | Sent, received, error, information; ERR in the header is the number of failed commands |
| RS232 | Serial communication interface standard |
| TCP | Transmission Control Protocol |
| MD5 | Message digest algorithm, used to detect changes to configuration files |
| HEX | Hexadecimal |
| ASCII | American Standard Code for Information Interchange |

## Annex A (informative) File locations

Table A.1  Data files

| File | Content |
|---|---|
| `settings.json` | Connection settings, send options, user interface language and the last sent command |
| `configs\*.json` | Instrument configuration files (`recipe\` before 1.1.0, `recipes\` in 1.1.0 to 1.2.0, see 5.5) |
| `recipes\material\*.json`, `recipes\film\*.json`, `recipes\layer\*.json`, `recipes\process\*.json` | Materials, films, layers and processes of the process editor, one file per object (see 5.3.12.4) |
| `logs\error.txt` | Error log, appended; moved to `error.1.txt` above 5 MB |
| `logs\<serial number>_<date>_<time>.txt` | With "Save communication data" on: the content shown during the connection; written when it exceeds 50 KB, when the connection ends and when the program exits; continued in `_1`, `_2`… files above 30 MB |

These files are located in the program directory, or in `Documents\Zevision\` when the program directory is not writable.

The material library `materials.json` is shipped with the program, is located in the program directory and is only read by the software. It contains the material table of Annex A of the IMC300 operating manual (253 materials; source file `docs\materials.json`) as UTF-8 JSON, for example `{ "materials": [ { "formula": "Al", "material_name": "aluminum", "material_name_zh": "铝", "density": 2.7, "z_ratio": 1.08, "z_ratio_established": true } ] }`. `formula` is the chemical formula (used as the material name when selected), `material_name_zh` the Chinese name, `density` the density (g/cm³) and `z_ratio` the Z ratio; `z_ratio_established` false means the manual gives no established Z ratio (1.000 is used). Other fields are not read.
