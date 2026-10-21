# Zevision Software User Manual

| Item | Content |
|---|---|
| Document identifier | ZEV-SUM-01-EN |
| Document version | 1.1 |
| Applicable software | Zevision 1.1.0 (user interface language: English) |
| Prepared by | INFICON |
| Date | 2026-10-19 |

## 1 Scope

### 1.1 Identification

This document applies to the following software:

- Software name: Zevision (window title `IMC300 Communication Test - Zevision 1.1.0`);
- Software version: 1.1.0;
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
| Left, "Instrument configuration" | "Save", "Restore" and "Edit" buttons, the drop-down for the data source to edit, operation progress and "Cancel" button |
| Bottom left | "Connect" and "Disconnect" buttons, status line |
| Right, "Data display" | Records of the data sent and received, TX, RX and ERR counters, "Clear" button, language button "中" (or "EN" in the Chinese interface) |
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

- While waiting, the status line shows `Connection lost; reconnection attempt n in x s …`, the status dot shows the primary color, and "Connection" and "Protocol options" remain locked.
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
- When sending, the command part of each command is converted to upper case; data after `:` is unchanged, for example `um0101:Aluminium` is sent as `UM0101:Aluminium`. The input box shows the converted text and keeps it, so that it can be edited and sent again.
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
- With "Save communication data" on, the content shown during the connection (in the display format selected at the time) is kept in memory and written to a communication data file in the `logs` folder when it exceeds 50 KB, when the connection ends (including a lost connection) and when the program exits. The file name consists of the device's system serial number (G06) and the date and time of the connection (to the minute), for example `XXXXXXXX_20261021_1337.txt`; with "Handshake on connect" off, or when the device does not return a serial number, the serial number part is `unknown`. A connection within the same minute appends to the same file. When a file exceeds 30 MB, the following content is written to a file named after it with `_1`, `_2`… appended (for example `XXXXXXXX_20261021_1337_1.txt`). Content not yet written is lost if the program terminates abnormally. The switch takes effect on connecting.

#### 5.3.9 Saving the configuration

1. Connect to the device.
2. Click "Save". The software reads the current device configuration (about 10 s over RS232).
3. Confirm the file name and location in the save dialog. The default location is the `recipes` folder in the program directory; the default file name is `IMC300_yyyyMMdd_HHmmss.json`.

A successful save shows no message box; the result is recorded in the data display. The reading phase is limited to 60 s and can be stopped with "Cancel".

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
- A successful restore shows no message box; the result is recorded in the data display. Errors, and a restore that finished with settings that could not be written, show a message box.

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
- Successful saves, New, Delete and list changes show no message box; the result is shown at the bottom of the window. Errors show a message box.

### 5.5 Data backup

- Configuration files are stored in the `recipes` folder of the program directory (`Documents\Zevision\recipes` when the program directory is not writable). A backup consists of a copy of this folder.
- Configuration files are UTF-8 JSON files with an MD5 checksum; files changed by hand are refused.
- Versions earlier than 1.1.0 use the folder `recipe`. The first time a configuration file is saved or opened, the software renames `recipe` to `recipes`; when `recipes` already exists, both folders are kept. If renaming fails (for example because a file in it is open in another program), the old files remain in `recipe` and can be selected in the open dialog.
- `settings.json` holds the connection settings, the send options and the user interface language. When it is damaged, the software uses default values, reports this in the data display and overwrites the file on exit.
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
| `settings.json` | Connection settings, send options and user interface language |
| `recipes\*.json` | Instrument configuration files (`recipe\` before 1.1.0, see 5.5) |
| `logs\error.txt` | Error log, appended; moved to `error.1.txt` above 5 MB |
| `logs\<serial number>_<date>_<time>.txt` | With "Save communication data" on: the content shown during the connection; written when it exceeds 50 KB, when the connection ends and when the program exits; continued in `_1`, `_2`… files above 30 MB |

These files are located in the program directory, or in `Documents\Zevision\` when the program directory is not writable.
