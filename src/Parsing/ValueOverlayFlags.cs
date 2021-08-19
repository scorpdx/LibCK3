using System;

namespace LibCK3
{
    [Flags]
    public enum ValueOverlayFlags
    {
        None = 0,
        /// <summary>
        /// Parse an int token as a CK3Date
        /// </summary>
        AsDate = 1 << 0,
        /// <summary>
        /// Parse a long_float token as a Q49.15,5
        /// </summary>
        AsQ = 1 << 1,
        /// <summary>
        /// Don't remove this flag for the current depth
        /// </summary>
        Repeats = 1 << 2,
        /// <summary>
        /// Only remove this flag when the container token it occurred on is popped
        /// </summary>
        KeepForChildren = 1 << 3,
        /// <summary>
        /// Flatten duplicate keys into an array
        /// </summary>
        Flatten = 1 << 4,
        /// <summary>
        /// Check for hidden objects
        /// </summary>
        HiddenObjectContainer = 1 << 5
    }
}