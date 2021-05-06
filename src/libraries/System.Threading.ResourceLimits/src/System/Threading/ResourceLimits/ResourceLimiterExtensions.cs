// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace System.Threading.ResourceLimits
{
    public static class ResourceLimiterExtensions
    {
        public static bool TryAcquire(this ResourceLimiter limiter, out Resource resource)
        {
            return limiter.TryAcquire(1, out resource);
        }

        public static ValueTask<Resource> AcquireAsync(this ResourceLimiter limiter, CancellationToken cancellationToken = default)
        {
            return limiter.AcquireAsync(1, cancellationToken);
        }
    }
}
