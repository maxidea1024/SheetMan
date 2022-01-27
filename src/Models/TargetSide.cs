using System;

namespace SheetMan.Models
{
    /// <summary>
    /// Target side flags
    /// </summary>
    [Flags]
    public enum TargetSide
    {
        /// <summary>None</summary>
        None = 0,

        /// <summary>Client side only</summary>
        ClientOnly = 0x1,

        /// <summary>Server side only</summary>
        ServerOnly = 0x2,

        /// <summary>Client and Server sides</summary>
        Both = 0x3,
    }
}
