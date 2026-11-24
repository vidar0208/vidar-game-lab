// Added for Unity: the RVO2-CS library uses .NET 8 ArgumentOutOfRangeException.ThrowIf* helpers, which Unity's
// .NET Standard 2.1 profile does not have. These do the same checks. (Modification of the Apache-2.0 library.)
using System;

namespace RVO
{
    internal static class Guard
    {
        public static void ThrowIfNegative(int v, string name) { if (v < 0) throw new ArgumentOutOfRangeException(name); }
        public static void ThrowIfNegative(float v, string name) { if (v < 0) throw new ArgumentOutOfRangeException(name); }
        public static void ThrowIfNegativeOrZero(int v, string name) { if (v <= 0) throw new ArgumentOutOfRangeException(name); }
        public static void ThrowIfNegativeOrZero(float v, string name) { if (v <= 0) throw new ArgumentOutOfRangeException(name); }
        public static void ThrowIfGreaterThanOrEqual(int v, int other, string name) { if (v >= other) throw new ArgumentOutOfRangeException(name); }
        public static void ThrowIfGreaterThanOrEqual(float v, float other, string name) { if (v >= other) throw new ArgumentOutOfRangeException(name); }
    }
}
