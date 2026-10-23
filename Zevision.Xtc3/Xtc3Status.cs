using System;

namespace Zevision.Xtc3
{
    /// <summary>S4 和 S0 的状态 (docs/XTC3.md 第 5 章)。</summary>
    public enum Xtc3State : byte
    {
        /// <summary>0 Ready。</summary>
        Ready = 0,

        /// <summary>1 Crucible Switch。</summary>
        CrucibleSwitch = 1,

        /// <summary>2 Rise 1。</summary>
        Rise1 = 2,

        /// <summary>3 Soak 1。</summary>
        Soak1 = 3,

        /// <summary>4 Rise 2。</summary>
        Rise2 = 4,

        /// <summary>5 Soak 2。</summary>
        Soak2 = 5,

        /// <summary>6 Shutter Delay。</summary>
        ShutterDelay = 6,

        /// <summary>7 Deposit。</summary>
        Deposit = 7,

        /// <summary>8 Rate Ramp。</summary>
        RateRamp = 8,

        /// <summary>9 Manual。</summary>
        Manual = 9,

        /// <summary>10 Time Power。</summary>
        TimePower = 10,

        /// <summary>11 Idle Ramp。</summary>
        IdleRamp = 11,

        /// <summary>12 Idle。</summary>
        Idle = 12,
    }

    /// <summary>S0 的 Crystal Status 位。</summary>
    [Flags]
    public enum Xtc3CrystalStatus : byte
    {
        /// <summary>正常。</summary>
        None = 0,

        /// <summary>bit0:晶振失效。</summary>
        Failed = 0x01,

        /// <summary>bit1:正在切换晶振。</summary>
        Switching = 0x02,

        /// <summary>bit2:读数无效。</summary>
        InvalidReading = 0x04,
    }

    /// <summary>S21 的上电错误标志位。</summary>
    [Flags]
    public enum Xtc3PowerUpErrors : byte
    {
        /// <summary>无。</summary>
        None = 0,

        /// <summary>bit0:参数丢失。</summary>
        ParameterLoss = 0x01,

        /// <summary>bit1:过程变量丢失。</summary>
        ProcessVariablesLoss = 0x02,

        /// <summary>bit2:断电后重新上电。</summary>
        PowerCycle = 0x04,
    }

    /// <summary>
    /// S33 的状态消息位。
    /// </summary>
    /// <remarks>
    /// docs/XTC3.md 中的位值有重复 (ZEV-FAR-01 的 T-10)。这里按“自 0x80000000 起每项右移一位”排列:
    /// 前 13 项与转录相同;Soak Hold、Non Deposit Hold、Etch Mode 为 0x00040000~0x00010000 (转录多写了一位 0),
    /// Local Lock~Delayed Start 与转录相同,Testing XIU、Layer Insert 为 0x00000800、0x00000400。待对照原版手册。
    /// </remarks>
    [Flags]
    public enum Xtc3StatusMessages : uint
    {
        /// <summary>无。</summary>
        None = 0,

        /// <summary>Max Power。</summary>
        MaxPower = 0x80000000,

        /// <summary>Crystal Fail。</summary>
        CrystalFail = 0x40000000,

        /// <summary>Crystal Switch Fail。</summary>
        CrystalSwitchFail = 0x20000000,

        /// <summary>Crystal Switching。</summary>
        CrystalSwitching = 0x10000000,

        /// <summary>Carousel Open。</summary>
        CarouselOpen = 0x08000000,

        /// <summary>Control Delay。</summary>
        ControlDelay = 0x04000000,

        /// <summary>Transfer Delay。</summary>
        TransferDelay = 0x02000000,

        /// <summary>Delay Failure。</summary>
        DelayFailure = 0x01000000,

        /// <summary>End of Process。</summary>
        EndOfProcess = 0x00800000,

        /// <summary>RateWatcher Delay。</summary>
        RateWatcherDelay = 0x00400000,

        /// <summary>RateWatcher Hold。</summary>
        RateWatcherHold = 0x00200000,

        /// <summary>RateWatcher Sample。</summary>
        RateWatcherSample = 0x00100000,

        /// <summary>Crucible Switch Fail。</summary>
        CrucibleSwitchFail = 0x00080000,

        /// <summary>Soak Hold。</summary>
        SoakHold = 0x00040000,

        /// <summary>Non Deposit Hold。</summary>
        NonDepositHold = 0x00020000,

        /// <summary>Etch Mode。</summary>
        EtchMode = 0x00010000,

        /// <summary>Local Lock。</summary>
        LocalLock = 0x00008000,

        /// <summary>Remote Lock。</summary>
        RemoteLock = 0x00004000,

        /// <summary>Test。</summary>
        Test = 0x00002000,

        /// <summary>Delayed Start。</summary>
        DelayedStart = 0x00001000,

        /// <summary>Testing XIU。</summary>
        TestingXiu = 0x00000800,

        /// <summary>Layer Insert。</summary>
        LayerInsert = 0x00000400,
    }
}
