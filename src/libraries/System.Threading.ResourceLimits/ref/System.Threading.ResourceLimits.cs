// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// ------------------------------------------------------------------------------
// Changes to this file must follow the https://aka.ms/api-review process.
// ------------------------------------------------------------------------------

namespace System.Threading.ResourceLimits
{
    public partial struct Resource : System.IDisposable
    {
        private object _dummy;
        private int _dummyPrimitive;
        public static System.Threading.ResourceLimits.Resource NoopResource;
        public Resource(object? state, System.Action<object?>? onDispose) { throw null; }
        public readonly object? State { get { throw null; } }
        public void Dispose() { }
    }
    public enum ResourceDepletedMode
    {
        Fail = 0,
        WaitQueue = 1,
        WaitStack = 2,
    }
    public partial class ResourceExhaustedException : System.Exception
    {
        public ResourceExhaustedException() { }
    }
    public abstract partial class ResourceLimiter
    {
        protected ResourceLimiter() { }
        public abstract long EstimatedCount { get; }
        public abstract System.Threading.Tasks.ValueTask<System.Threading.ResourceLimits.Resource> AcquireAsync(long requestedCount, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken));
        public abstract bool TryAcquire(long requestedCount, out System.Threading.ResourceLimits.Resource resource);
    }
    public static partial class ResourceLimiterExtensions
    {
        public static System.Threading.Tasks.ValueTask<System.Threading.ResourceLimits.Resource> AcquireAsync(this System.Threading.ResourceLimits.ResourceLimiter limiter, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) { throw null; }
        public static bool TryAcquire(this System.Threading.ResourceLimits.ResourceLimiter limiter, out System.Threading.ResourceLimits.Resource resource) { throw null; }
    }
    public abstract partial class ResourceLimiterOptions
    {
        protected ResourceLimiterOptions() { }
        public System.Threading.ResourceLimits.ResourceDepletedMode DepletedMode { get { throw null; } set { } }
        public long ResourceLimit { get { throw null; } set { } }
    }
    public sealed partial class SampleConcurrencyLimiter : System.Threading.ResourceLimits.ResourceLimiter
    {
        public SampleConcurrencyLimiter(System.Threading.ResourceLimits.SampleConcurrencyLimiterOptions options) { }
        public override long EstimatedCount { get { throw null; } }
        public override System.Threading.Tasks.ValueTask<System.Threading.ResourceLimits.Resource> AcquireAsync(long requestedCount, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) { throw null; }
        public override bool TryAcquire(long requestedCount, out System.Threading.ResourceLimits.Resource resource) { throw null; }
    }
    public sealed partial class SampleConcurrencyLimiterOptions : System.Threading.ResourceLimits.ResourceLimiterOptions
    {
        public SampleConcurrencyLimiterOptions() { }
    }
    public sealed partial class SampleRateLimiter : System.Threading.ResourceLimits.ResourceLimiter
    {
        public SampleRateLimiter(System.Threading.ResourceLimits.SampleRateLimiterOptions options) { }
        public override long EstimatedCount { get { throw null; } }
        public override System.Threading.Tasks.ValueTask<System.Threading.ResourceLimits.Resource> AcquireAsync(long requestedCount, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) { throw null; }
        public override bool TryAcquire(long requestedCount, out System.Threading.ResourceLimits.Resource resource) { throw null; }
    }
    public sealed partial class SampleRateLimiterOptions : System.Threading.ResourceLimits.ResourceLimiterOptions
    {
        public SampleRateLimiterOptions() { }
        public long ReplenishRate { get { throw null; } set { } }
    }
}
