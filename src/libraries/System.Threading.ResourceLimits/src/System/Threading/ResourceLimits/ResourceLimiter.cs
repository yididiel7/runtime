// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace System.Threading.ResourceLimits
{
    public abstract class ResourceLimiter
    {
        // An estimated count of resources.
        public abstract long EstimatedCount { get; }

        // Fast synchronous attempt to acquire resources.
        // Set requestedCount to 0 to get whether resource limit has been reached.
        public abstract bool TryAcquire(long requestedCount, out Resource resource);

        // Wait until the requested resources are available.
        // Set requestedCount to 0 to wait until resource is replenished.
        // An exception is thrown if resources cannot be obtained.
        public abstract ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default);
    }
}
