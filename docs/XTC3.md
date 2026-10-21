# XTC/3 Standard Protocol 通讯指令集

> 来源：INFICON XTC/3 Operating Manual, Chapter 5 Remote Communications  
> 适用范围：RS-232 选择 `Standard` 协议时生效；TCP/IP 始终使用 Standard 协议，TCP 端口 2101。

---

## 1. 指令包 / 反馈包结构

### 1.1 Standard Command Packet（Host -> XTC/3）

格式：

    <Length><Message><Checksum>

| 字段 | 长度 | 说明 |
|---|---:|---|
| `<Length>` | 2 bytes | Low byte 在前，High byte 在后。不包含 Length 本身和 Checksum。数值范围 0–57,800，表示 command 字符数。多数命令小于 256，因此 Low byte 为字符数，High byte 为 0。 |
| `<Message>` | 可变 | 由一个或多个 `<Command>` 组成。 |
| `<Checksum>` | 1 byte | 对所有字节求和后取模 256，**不包含 Length 字节**。 |

`<Message>` 结构：

    <Command><Command>...<Command>

单个 `<Command>`：

    <Command Group>(<Command Sub-group>)(<Command ID>)(<Parameter>...<Parameter>)

| 字段 | 长度 | 说明 |
|---|---:|---|
| Command Group | 1 ASCII byte | `E` Echo, `H` Hello, `Q` Query, `R` Remote action, `S` Status, `U` Update |
| Command Sub-group | 1 ASCII byte，可选 | 仅 Q/U 使用：`F` Film, `G` General, `I` Input, `N` Film Name, `O` Output, `P` Process, `T` Output Type。H/E/S/R 无 sub-group。 |
| Command ID | 1 binary byte | 指定命令组内的具体命令。 |
| Parameter | 可变 | `<Byte>` / `<Integer>` / `<Float>` / `<String>` |

参数编码：

| 类型 | 长度 | 说明 |
|---|---:|---|
| `<Byte>` | 1 byte | 单字节二进制值。 |
| `<Integer>` | 4 bytes | 低字节在前，有符号整数。 |
| `<Float>` | 4 bytes | ANSI 标准单精度浮点数，低字节在前。 |
| `<String>` | 可变 | 以 Null 结尾的 ASCII 字符串。 |

---

### 1.2 Standard Response Packet（XTC/3 -> Host）

格式：

    <Length><CCB><Timer><Response Message><Checksum>

| 字段 | 长度 | 说明 |
|---|---:|---|
| `<Length>` | 2 bytes | Low byte 在前，High byte 在后。不包含 Length 和 Checksum。数值范围 0–57,800，表示 Response Packet 中 **CCB + Timer + Response Message** 的字符数。 |
| `<CCB>` | 1 byte | Condition Code Byte。MSB 置位（0x80）表示 command packet error；MSB 清零表示命令包格式有效并已解析。 |
| `<Timer>` | 1 byte | 0–255，每 250 ms 递增一次。 |
| `<Response Message>` | 可变 | `<Command Response>...<Command Response><Packet Error Code>`。Command Response 数量等于发送的命令数量。若 CCB MSB 置位，Response Message 为单个 packet response error。 |
| `<Checksum>` | 1 byte | 对 CCB、Timer、Response Message 所有字节求和后取模 256，**不包含 Length**。 |

`<Command Response>` 结构：

    <ACK><Response><Response Error Code>

- `<ACK>`：ASCII 6，表示 Positive Acknowledgement。  
- 当返回 Response Error Code 时，不发送 ACK。
- `<Response>` 可为 `<Integer>`、`<Float>`、`<String>` 或其他组合，均为低字节在前 / Null 结尾 ASCII。

#### Response Error Code（1 byte ASCII）

| Code | 含义 |
|---|---|
| `A` | Illegal Command，非法命令 |
| `B` | Illegal parameter value，非法参数值 |
| `C` | Illegal ID，非法 ID |
| `D` | Illegal format，非法格式 |
| `E` | Data not available，数据不可用 |
| `F` | Cannot do now，当前不能执行（部分命令要求 Ready/Stop） |
| `L` | Length Error，长度必须 >0 且 ≤57,800 |
| `O` | Not enough room for response size，响应空间不足 |
| `P` | Prior command failed，若多命令包中前一条失败，后续命令不执行并返回此错误 |

#### Packet Error Code（1 byte ASCII）

| Code | 含义 |
|---|---|
| `C` | invalid checksum，校验和无效 |
| `F` | illegal format，格式非法（字节过多或缺少值） |
| `I` | invalid message，无效消息 |
| `L` | Length Error，长度必须 >0 且 ≤57,800 |
| `M` | too many commands，命令过多；最多 100 条 |

#### 超时与限制

- 命令包字符之间超过 **3 秒**，XTC/3 超时，不返回响应，清空缓冲区，并将后续字符视为新包开始。
- 一个包最多 **100 条命令**。
- 最大长度 **57,800 bytes**。

---

## 2. 基础命令

### 2.1 ECHO

| 项目 | 内容 |
|---|---|
| 类型 | Echo |
| 指令包 | `E <String>` |
| 响应 | 返回相同 `<String>` |
| 说明 | String 为 Null 结尾 ASCII。 |

### 2.2 HELLO

| 项目 | 内容 |
|---|---|
| 类型 | Hello |
| 指令包 | `H <Command ID>` |
| 响应 | `<String>` 或 `<Float>` |

| Command ID | 含义 | 响应 |
|---:|---|---|
| 1 | ASCII name and version | `<String> = "XTC/3x Version y.yy"`，x = M 或 S，y.yy = firmware version |
| 2 | Structure number / Compatibility number / Range number / Unit Type | `<Integer><Integer><Integer><Integer>`；0 = XTC3/M，1 = XTC3/S；77 = XTC3/M，83 = XTC3/S |
| 3 | Firmware version number | `<Float>` |

---

## 3. Query 指令（查询）

| 指令 | 指令包格式 | 类型 | 说明 / 值范围 |
|---|---|---|---|
| QB | `QB <Command ID>` | Query Block | ID=1 所有参数；ID=2 所有参数但不含 Process Layer lists 和 Process Names；ID=3 Process 1–50 信息（XTC/3M）；ID=4 Process 51–99 信息（XTC/3M） |
| QN | `QN <Film Number>` | Query Film Name | Film Number：1–32（仅 XTC/3M）。响应最大 15 字符 + Null。 |
| QF | `QF <Command ID> <Film Number>` | Query Film Parameters | Command ID 见 Film Parameters 表；Film Number：1–9（XTC/3S）或 1–32（XTC/3M）。`QF255 <Film Number>` 返回该 Film 所有参数。 |
| QG | `QG <Command ID>` | Query General Parameter | Command ID 见 General Parameters 表。`QG255` 返回所有 General 参数。 |
| QI | `QI <Input Number>` | Query Input Definition | 仅 XTC/3M。Input Number：1–9，255 = 所有 9 个输入。 |
| QO | `QO <Output Number>` | Query Output Definition | 仅 XTC/3M。Output Number：1–20，255 = 所有输出。 |
| QT | `QT <Output Number>` | Query Output Type | 仅 XTC/3M。Output Number：1–20，255 = 所有。返回 0 = Normally Open，1 = Normally Closed。 |
| QP | `QP <Command ID> <Process Number> [<Parameter>]` | Query Process Parameters | 仅 XTC/3M。Process Number：1–99。ID=1 Layer List；ID=2 Name；ID=3 Specific Layer（Parameter 为 2 bytes 目标层，返回 1–32 film number）。 |

---

## 4. Update 指令（更新）

| 指令 | 指令包格式 | 类型 | 说明 / 值范围 |
|---|---|---|---|
| UB | `UB <Command ID> <List of parameters>` | Update Block | ID=1/2/3/4，格式与 QB 对应。最大 57,800 bytes；超过需拆分。 |
| UN | `UN <Film Number> <Name>` | Update Film Name | 仅 XTC/3M。Film Number：1–32。Name 最大 15 字符，字符限制 hex 20–7E，加 Null 结尾。 |
| UF | `UF <Command ID> <Film Number> <Parameter Value>` | Update Film Parameters | Command ID 见 Film Parameters 表；Film Number：1–9（XTC/3S）或 1–32（XTC/3M）。`UF255 <Film Number> <List>` 更新该 Film 所有参数。部分参数运行中不可改。 |
| UG | `UG <Command ID> <Parameter Value>` | Update General Parameters | Command ID 见 General Parameters 表。`UG255 <List>` 更新所有 General 参数。部分参数有状态限制。 |
| UI | `UI <Input Number> <Input Definition>` | Update Input Definition | 仅 XTC/3M。Input Number：1–9，255 = 所有输入。Input Definition 见 Input Definition 表。 |
| UO | `UO <Output Number> <Output Definition>` | Update Output Definition | 仅 XTC/3M。Output Number：1–20，255 = 所有输出。Output Definition 见 Output Definition 表。 |
| UT | `UT <Output Number> <Output Type>` | Update Output Type | 仅 XTC/3M。Output Number：1–20，255 = 所有。Type：0 = Normally Open，1 = Normally Closed。 |
| UP | `UP <Command ID> <Process Number> <Parameter List> [<Parameter Value>]` | Update Process Parameters | 仅 XTC/3M。Process Number：1–99。ID=1 Layer List；ID=2 Name（最大 15 字符 hex20–7E + Null）；ID=3 Specific Layer（Parameter List 为目标层 2 bytes，Parameter Value 为 1–32）。 |

---

## 5. Status 指令（状态）

| 指令 | 响应格式 | 说明 |
|---|---|---|
| S0 `<Timer Tick>` | Byte / Byte / Byte / Byte / Float / Float / Float / Float / Int | 过程信息，仅对指定 timer tick 有效。返回：Crystal Status（1 byte，bit0 1=Failed，bit1 1=Switching，bit2 1=Invalid Reading）；State（1 byte）；Active Sensor（1 byte）；Active Crystal（1 byte）；Rate（Float，1 秒平均）；Thickness（Float）；Power（Float）；Rate Deviation（Float）；Frequency（Int DDS，乘 0.0034924596 转 Hz）。Stop 或非 Deposit 时 Rate/Rate Deviation 返回 0。 |
| S1 | Float | 当前 Rate，A/s，1 秒滚动平均。 |
| S2 | Float | 当前 Power，%。 |
| S3 | Float | 当前 Thickness，kA。 |
| S4 | Byte | 当前 State：0=Ready，1=Crucible Switch，2=Rise 1，3=Soak 1，4=Rise 2，5=Soak 2，6=Shutter Delay，7=Deposit，8=Rate Ramp，9=Manual，10=Time Power，11=Idle Ramp，12=Idle。 |
| S5 | Int | 当前 State Time，秒。 |
| S6 | Int | Active Layer。 |
| S7 | Byte | Active Film。 |
| S8 | Byte | Active Sensor，1 或 2。 |
| S9 | Byte | Crystal Life，%。 |
| S10 | Byte | Power Source，1 或 2。 |
| S11 | 4 Bytes | Output Status，每 bit 对应一个输出；Output 1 = LSB，Output 20 = 第 20 bit。 |
| S12 | 2 Bytes | Input Status，每 bit 对应一个输入：0x0100=Input1，0x0200=Input2，0x0400=Input3，0x0800=Input4，0x1000=Input5，0x2000=Input6，0x4000=Input7，0x8000=Input8，0x0001=Input9。 |
| S13 | Int | Raw Frequency，DDS units；乘 0.0034924596 转 Hz。 |
| S14 | Byte | Xtal Fail：0=good/not in fail，1=failed/in fail。 |
| S15 | Byte | Max Power：0=false，1=true。 |
| S16 | Byte | Crystal Switching：0=not switching，1=switching。 |
| S17 | Byte | End of Process：0=False，1=True。 |
| S18 | Byte | Stop：0=False，1=True。 |
| S19 | 混合 | Data Log：Layer#（Int），Film#（Int），Rate（Float A/s 1s avg），Thickness（Float kA），Deposit time（Int s），Average power（Float %），S Value（Int），Q Value（Byte），Begin Frequency（Float Hz），End Frequency（Float Hz），Crystal Life（Byte %），Layer end type（0 Normal，1 Time power，2 Stop），Cause of stop（Byte：0 No stop，1 Keyboard，5 Crystal Fail，6 Max Power，7 Hand Controller，8 Communications，9 Digital Input，10 Power Loss，11 Rate Dev Error，12 Crystal Switcher fail）。 |
| S20 | Byte | Active Process。 |
| S21 | Byte | Power up error flag：bit0=Parameter loss，bit1=Process variables loss，bit2=Power down/up cycle。 |
| S30 | 2 Bytes | Crystal Status，每 bit 对应 multi-head sensor 一个晶体，1=Good，0=Failed，LSB=Crystal 1；超出晶体数的 bit 为 0。 |
| S31 | Float | Rolling Average，6.25 秒测量速率平均，每 0.25 秒更新。 |
| S32 | Byte | Active Crystal，当前 sensor 使用中的晶体。 |
| S33 | 4 Bytes | Status Messages 位图：0x80000000 Max Power，0x40000000 Crystal Fail，0x20000000 Crystal Switch Fail，0x10000000 Crystal Switching，0x08000000 Carousel Open，0x04000000 Control Delay，0x02000000 Transfer Delay，0x01000000 Delay Failure，0x00800000 End of Process，0x00400000 RateWatcher Delay，0x00200000 RateWatcher Hold，0x00100000 RateWatcher Sample，0x00080000 Crucible Switch Fail，0x00400000 Soak Hold，0x00200000 Non Deposit Hold，0x00100000 Etch Mode，0x00008000 Local Lock，0x00004000 Remote Lock，0x00002000 Test，0x00001000 Delayed Start，0x00008000 Testing XIU，0x00004000 Layer Insert；0x00002000–0x00000001 未定义。 |
| S34 | Float | Raw Rate，A/s。 |
| S35 | 8 Bytes | Ethernet parameters：前 4 bytes = IP Address（xx.xx.xx.xx），后 4 bytes = Net Mask（xx.xx.xx.xx），最左 xx 为 MSB。 |

> S0 的 `<Timer Tick>` 为 0x00–0xFF；XTC/3 按每个 tick 存储数据。若发送的不是当前 tick，返回旧数据，最旧可能约 64 秒（256 ticks / 4 per second）。要获取当前数据，需先从先前响应中提取 timer tick，每 250 ms 递增后填入 S0。

---

## 6. Remote 指令（远程动作）

| 指令 | 指令包格式 | 功能 | 说明 / 参数 |
|---|---|---|---|
| R0 | `R0` | Start | 启动 active process 的下一层。 |
| R1 | `R1` | Stop | 停止当前层，功率归零，关闭挡板等。 |
| R2 | `R2` | Reset | 若在 Stop，复位到 Ready，过程回到开始。 |
| R3 | `R3` | Remote lock on | 禁止前面板更新参数。 |
| R4 | `R4` | Remote lock off | 解除远程锁定。 |
| R5 | `R5` | Crystal fail inhibit on | 禁止 crystal fail relay 动作。 |
| R6 | `R6` | Crystal fail inhibit off | 恢复正常 crystal fail relay。 |
| R7 | `R7` | Soak Hold 2 on | 进入 Soak 2 后保持，直到 R8。 |
| R8 | `R8` | Soak Hold 2 off | 允许离开 Soak 2。 |
| R9 | `R9` | Manual on | 进入 Manual power state。 |
| R10 | `R10` | Manual off | 退出 Manual，进入 Deposit。 |
| R11 | `R11 <Value>` | Set power | Value = Float 0–100，仅在 Manual state 有效。 |
| R12 | `R12` | Zero thickness | 当前层厚度清零。 |
| R13 | `R13` | Final thickness trigger | 模拟达到 Final Thickness。 |
| R14 | `R14` | Crystal switch | 多晶体 sensor 切换到下一位置。 |
| R15 | `R15` | Enter comm. I/O mode | 仅 XTC/3S；数字输出由远程控制；XTC/3M 返回 illegal ID。 |
| R16 | `R16` | Exit comm. I/O mode | 仅 XTC/3S；恢复数字输出正常控制；XTC/3M 返回 illegal ID。 |
| R17 | `R17 <Value>` | Set/close digital output | Value = Byte，Output # 1–20。XTC/3S 需 comm I/O mode；XTC/3M 输出需设为 Computer Control。 |
| R18 | `R18 <Value>` | Clear/open digital output | Value = Byte，Output # 1–20。条件同 R17。 |
| R19 | `R19` | Backlight on | LCD 背光全亮。 |
| R20 | `R20` | Backlight off | LCD 背光全灭。 |
| R21 | `R21` | Trigger beeper | 发出蜂鸣。 |
| R22 | `R22` | Clear power up error flag | 清除上电错误。 |
| R26 | `R26` | Clear All Crystals | 当前 sensor 所有晶体状态设为 good。 |
| R27 | `R27` | Rotate head | 对 CrystalSix、Crystal12、Rotary、Dual Head 执行完整旋转并验证所有晶体；Single/CrystalTwo/Dual Head 可能返回 A；无输出定义返回 F。 |
| R28 | `R28` | Clear S and Q counts | 清除累积 Stability 和 Quality 计数。 |

---

## 7. Film Parameters：UF/QF Command ID 与值范围

| Command ID (Hex) | 参数名 | 单位 / 允许值 | 数据类型 |
|---:|---|---|---|
| 0 (0x00) | Rise Time 1 | MM:SS，0–99:59（0–5999 s） | Int |
| 1 (0x01) | Soak Power 1 | 0.0–100.0 % | Float |
| 2 (0x02) | Soak Time 1 | 0–99:59（0–5999 s） | Int |
| 3 (0x03) | Rise Time 2 | 0–99:59（0–5999 s） | Int |
| 4 (0x04) | Soak Power 2 | 0.0–100.0 % | Float |
| 5 (0x05) | Soak Time 2 | 0–99:59（0–5999 s） | Int |
| 6 (0x06) | Idle Ramp | 0–99:59（0–5999 s） | Int |
| 7 (0x07) | Idle Power | 0.0–100.0 % | Float |
| 8 (0x08) | Deposition Rate | 0.0–999.9 A/s | Float |
| 9 (0x09) | Final Thickness | 0.000–999.9 kA | Float |
| 10 (0x0a) | Thickness Set Point | 0.000–999.9 kA | Float |
| 11 (0x0b) | New Rate | 0.0–999.9 A/s | Float |
| 12 (0x0c) | Rate Ramp Time | 0–99:59（0–5999 s） | Int |
| 13 (0x0d) | Rate Watcher Accuracy | 1–99 % | Int |
| 14 (0x0e) | RW Hold Time | 0–99:59（0–5999 s） | Int |
| 15 (0x0f) | Sensor | 1–2；运行中不可改 | Int |
| 16 (0x10) | Tool Factor 1 | 10.0–500.0 % | Float |
| 17 (0x11) | Tool Factor 2 | 10.0–500.0 %；仅 CrystalTwo 时有效 | Float |
| 18 (0x12) | Xtal Stability Single | 0 或 25–9999；1–24 不允许 | Int |
| 19 (0x13) | Xtal Stability Total | 0 或 25–9999；1–24 不允许 | Int |
| 20 (0x14) | Xtal Quality Percent | 0–99 % | Int |
| 21 (0x15) | Xtal Quality Counts | 0–99 | Int |
| 22 (0x16) | Source | 1–2 | Int |
| 23 (0x17) | Crucible | 0–8 | Int |
| 24 (0x18) | Control Gain | 0.01–100.0 A/s/% | Float |
| 25 (0x19) | Control Time Constant | 0.1–100.0 s | Float |
| 26 (0x1a) | Control Dead Time | 0.1–100.0 s | Float |
| 27 (0x1b) | Max Power | 0.0–100.0 % | Float |
| 28 (0x1c) | Density | 0.50–99.99 g/cm³ | Float |
| 29 (0x1d) | Z-Ratio | 0.100–9.999 | Float |
| 30 (0x1e) | Time Power | 1=Yes，0=No | Int |
| 31 (0x1f) | Delay option | 0=None，1=Shutter，2=Control，3=Both | Int |
| 32 (0x20) | Transfer Sensor | 1=Yes，0=No；运行中不可改 | Int |
| 33 (0x21) | Transfer Tooling | 10.0–500.0 % | Float |
| 34 (0x22) | Control Delay Time | 0–99:59（0–5999 s） | Int |
| 35 (0x23) | Ion Assisted Deposit | 1=Yes，0=No | Int |
| 36 (0x24) | Graph Label | 0=Rate Dev，1=Power | Int |
| 37 (0x25) | Graph Scale | 0=5，1=10，2=20，3=40 | Int |
| 255 (0xff) | All Parameters | 批量读取/更新该 Film 所有参数 | — |

---

## 8. General Parameters：UG/QG Command ID 与值范围

| Command ID (Hex) | 参数名 | 单位 / 允许值 | 数据类型 |
|---:|---|---|---|
| 0 (0x00) | Process to run（XTC/3M）/ Film to run（XTC/3S） | 1–99（XTC/3M）/ 1–9（XTC/3S）；仅 Ready 或 Idle in last layer 可改 | Int |
| 1 (0x01) | Start Layer Without Backup Xtal | 1=Yes，0=No | Int |
| 2 (0x02) | Stop on Alarms | 1=Yes，0=No | Int |
| 3 (0x03) | Stop On Max Power | 1=Yes，0=No | Int |
| 4 (0x04) | Dep/Eth Mode | 0=Deposit，1=Etch | Int |
| 5 (0x05) | Test | 1=Yes，0=No | Int |
| 6 (0x06) | Sensor 1 Type | 0=Single，1=CrystalTwo，2=CrystalSix，3=Crystal12，4=Rotary，5=Dual Head | Int |
| 7 (0x07) | Sensor 2 Type | 0=Single，1=CrystalTwo，2=CrystalSix，3=Crystal12，4=Rotary；Query 时 5=Dual Head | Int |
| 8 (0x08) | Source Control Voltage | 0=0~+10，1=0~-10，2=0~+5，3=0~-5，4=0~+2.5，5=0~-2.5 | Int |
| 9 (0x09) | Recorder Mode | 0=Rate，1=Thickness，2=Power，3=Rate Deviation | Int |
| 10 (0x0a) | Recorder Range | 0=100，1=1000 | Int |
| 11 (0x0b) | Recorder Filter | 1=Smooth，0=Unfiltered | Int |
| 12 (0x0c) | Audio Feedback | 1=Yes，0=No | Int |
| 13 (0x0d) | Dim LCD | 0–99 分钟 | Int |
| 14 (0x0e) | RS232 Baud Rate | 0=9600，1=19200，2=38400，3=57600，4=115200 | Int |
| 15 (0x0f) | RS232 Communications Protocol | 0=Standard RS232，1=Datalog，2=XTC/2 w/checksum，3=XTC/2 no checksum | Int |

---

## 9. Input Definition：QI/UI 值范围（XTC/3M）

| 值 | 输入功能 |
|---:|---|
| 0 | Blank，未使用 |
| 1 | Start |
| 2 | Stop |
| 3 | End Deposit |
| 4 | Rate Watcher Hold Initiate |
| 5 | Rate Watcher Hold Inhibit |
| 6 | Crystal Fail Inhibit |
| 7 | Zero Thickness |
| 8 | Soak 2 Hold |
| 9 | Crucible 1 Valid |
| 10 | Crucible 2 Valid |
| 11 | Reset |
| 12 | Select Process 1–4，2 bits（主 bit） |
| 13 | Select Process 1–4，bit 2 |
| 14 | Select Process 1–16，4 bits（主 bit） |
| 15 | Select Process 1–16，bit 2 |
| 16 | Select Process 1–16，bit 3 |
| 17 | Select Process 1–16，bit 4 |
| 18 | Select Process 1–64，6 bits（主 bit） |
| 19 | Select Process 1–64，bit 2 |
| 20 | Select Process 1–64，bit 3 |
| 21 | Select Process 1–64，bit 4 |
| 22 | Select Process 1–64，bit 5 |
| 23 | Select Process 1–64，bit 6 |
| 24 | Select Process 1–99，7 bits（主 bit） |
| 25 | Select Process 1–99，bit 2 |
| 26 | Select Process 1–99，bit 3 |
| 27 | Select Process 1–99，bit 4 |
| 28 | Select Process 1–99，bit 5 |
| 29 | Select Process 1–99，bit 6 |
| 30 | Select Process 1–99，bit 7 |
| 31 | Switch Crystal |
| 32 | Non-Deposit Hold |
| 33 | Zero Film Time |
| 34 | Start Inhibit |
| 35 | Soak 1 Hold |

> 对 Select Process 的单个更新只能设置主 bit；对应 secondary bits 自动设置。块更新 `UI 255` 时主 bit 必须设置，其他关联 bit 需为正确编号或 0。

---

## 10. Output Definition：QO/UO 值范围（XTC/3M）

| 值 | 输出功能 |
|---:|---|
| 0 | Blank，未使用 |
| 1 | Source Shutter 1 |
| 2 | Source Shutter 2 |
| 3 | Sensor Shutter 1 |
| 4 | Sensor Shutter 2 |
| 5 | Stop |
| 6 | End of Process |
| 7 | Thickness Setpoint |
| 8 | Crystal Fail |
| 9 | Alarms |
| 10 | Source in use（Open=1；closed=2） |
| 11 | Final Thickness |
| 12 | End of Film |
| 13 | In Layer |
| 14 | Ion Assisted Deposition |
| 15 | Crystal Switcher 1 |
| 16 | Crystal Switcher 2 |
| 17 | Ready |
| 18 | Crucible Switching |
| 19 | Rise 1 |
| 20 | Soak 1 |
| 21 | Rise 2 |
| 22 | Soak 2 |
| 23 | Shutter Delay |
| 24 | Deposit |
| 25 | Rate Ramp |
| 26 | Manual |
| 27 | Time Power |
| 28 | Idle Ramp |
| 29 | Idle |
| 30 | Max Power |
| 31 | Rate Deviation Error |
| 32 | Crystal Switcher Fail |
| 33 | Crystal Switching |
| 34 | Crucible Switcher Fail |
| 35 | Shutter Delay Error |
| 36 | Computer Control |
| 37 | Crucible Select Source 1 Binary（需 8 outputs，one/position，主 bit） |
| 38 | Crucible Select Source 1 Binary，bit 2 |
| 39 | Crucible Select Source 1 Binary，bit 3 |
| 40 | Crucible Select Source 1 Binary，bit 4 |
| 41 | Crucible Select Source 1 Binary，bit 5 |
| 42 | Crucible Select Source 1 Binary，bit 6 |
| 43 | Crucible Select Source 1 Binary，bit 7 |
| 44 | Crucible Select Source 1 Binary，bit 8 |
| 45 | Crucible Select Source 1 BCD（需 3 outputs，主 bit） |
| 46 | Crucible Select Source 1 BCD，bit 2 |
| 47 | Crucible Select Source 1 BCD，bit 3 |
| 48 | Crucible Select Source 2 Binary（需 8 outputs，one/position，主 bit） |
| 49 | Crucible Select Source 2 Binary，bit 2 |
| 50 | Crucible Select Source 2 Binary，bit 3 |
| 51 | Crucible Select Source 2 Binary，bit 4 |
| 52 | Crucible Select Source 2 Binary，bit 5 |
| 53 | Crucible Select Source 2 Binary，bit 6 |
| 54 | Crucible Select Source 2 Binary，bit 7 |
| 55 | Crucible Select Source 2 Binary，bit 8 |
| 56 | Crucible Select Source 2 BCD（需 3 outputs，主 bit） |
| 57 | Crucible Select Source 2 BCD，bit 2 |
| 58 | Crucible Select Source 2 BCD，bit 3 |

> 选择 BCD 输出功能时，必须连续 3 个 relay 或 TTL outputs 可用；选择 Bin 输出功能时，必须连续 8 个 outputs 可用。块更新时主 bit 必须设置，关联 secondary bits 自动设置或需为正确编号/0。

---

## 11. 备注

- `QF255`、`QG255`、`UF255`、`UG255` 用于批量读取/更新对应参数。
- `QB`/`UB` 的 Command ID 1–4 对应不同批量数据范围，最大 57,800 bytes；超过需拆分。
- 标准协议为二进制协议；XTC/2 协议为 ASCII 协议，本文件不包含。
- TCP/IP 始终使用 Standard 协议，不受 RS-232 Protocol 选择影响。
- 所有 String 均为 Null 结尾 ASCII；所有 Integer/Float 均低字节在前。