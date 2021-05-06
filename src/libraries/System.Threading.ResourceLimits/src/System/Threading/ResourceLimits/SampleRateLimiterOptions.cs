// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.ResourceLimits
{
    public sealed class SampleRateLimiterOptions : ResourceLimiterOptions
    {
        // TODO: Actual representation will likely include two components:
        // 1. A Timespan representing the tick rate
        // 2. A long representing how resources replenished per tick
        // For simplicity, it's currently represented as resources per second
        public long ReplenishRate { get; set; }

        // TODO: allow external triggering of replenishment to reduce timer instantiation
    }
}
