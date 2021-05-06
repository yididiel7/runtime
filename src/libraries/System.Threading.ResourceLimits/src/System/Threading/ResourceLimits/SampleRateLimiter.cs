// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace System.Threading.ResourceLimits
{
    // Sample rate limiter
    // Time based, auto replenishing
    public sealed class SampleRateLimiter : ResourceLimiter
    {
        private readonly SampleRateLimiterOptions _options;
        private long _resourceCount;
        private Timer _renewTimer;
        private readonly ConcurrentQueue<RateLimitRequest> _queue = new ConcurrentQueue<RateLimitRequest>();

        public override long EstimatedCount => Interlocked.Read(ref _resourceCount);

        public SampleRateLimiter(SampleRateLimiterOptions options)
        {
            _options = options;
            // Start timer
            _renewTimer = new Timer(Replenish, this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public override ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default)
        {
            if (requestedCount < 0 || requestedCount > _options.ResourceLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedCount));
            }

            if (requestedCount == 0 && EstimatedCount <= _options.ResourceLimit)
            {
                return new ValueTask<Resource>(Resource.NoopResource);
            }

            if (Interlocked.Add(ref _resourceCount, requestedCount) <= _options.ResourceLimit)
            {
                return new ValueTask<Resource>(Resource.NoopResource);
            }

            // Undo resource acquisition
            Interlocked.Add(ref _resourceCount, -requestedCount);

            var request = new RateLimitRequest(requestedCount, cancellationToken);
            // TODO: Currently only implements ResourceDepletedMode.WaitQueue
            _queue.Enqueue(request);

            return new ValueTask<Resource>(request.Tcs.Task);
        }

        public override bool TryAcquire(long requestedCount, out Resource resource)
        {
            resource = Resource.NoopResource;
            if (requestedCount < 0 || requestedCount > _options.ResourceLimit)
            {
                return false;
            }

            if (requestedCount == 0)
            {
                return Interlocked.Read(ref _resourceCount) < _options.ResourceLimit;
            }

            if (Interlocked.Add(ref _resourceCount, requestedCount) <= _options.ResourceLimit)
            {
                return true;
            }

            Interlocked.Add(ref _resourceCount, -requestedCount);
            return false;
        }

        private static void Replenish(object? state)
        {
            // Return if Replenish already running to avoid concurrency.
            var limiter = state as SampleRateLimiter;

            if (limiter == null)
            {
                return;
            }

            var resourceCount = Interlocked.Read(ref limiter._resourceCount);
            if (resourceCount > 0)
            {
                var resourceToReplenish = Math.Min(limiter._options.ReplenishRate, resourceCount);
                Interlocked.Add(ref limiter._resourceCount, -resourceToReplenish);
            }

            // Process queued requests
            var queue = limiter._queue;
            while (queue.TryPeek(out var request))
            {
                if (Interlocked.Add(ref limiter._resourceCount, request.Count) <= limiter._options.ResourceLimit)
                {
                    // Request can be fulfilled
                    queue.TryDequeue(out var requestToFulfill);

                    if (requestToFulfill == request)
                    {
                        // If requestToFulfill == request, the fulfillment is successful.
                        requestToFulfill.Tcs.SetResult(Resource.NoopResource);
                    }
                    else
                    {
                        // If requestToFulfill != request, there was a concurrent Dequeue:
                        // 1. Reset the resource count.
                        // 2. Put requestToFulfill back in the queue (no longer FIFO) if not null
                        Interlocked.Add(ref limiter._resourceCount, -request.Count);
                        if (requestToFulfill != null)
                        {
                            queue.Enqueue(requestToFulfill);
                        }
                    }
                }
                else
                {
                    // Request cannot be fulfilled
                    Interlocked.Add(ref limiter._resourceCount, -request.Count);
                    break;
                }
            }
        }

        // TODO: replace with AsyncOperation
        private class RateLimitRequest
        {
            public RateLimitRequest(long count, CancellationToken token)
            {
                Count = count;
                Tcs = new TaskCompletionSource<Resource>();
                token.Register(() => Tcs.TrySetCanceled());
            }

            public long Count { get; }

            public TaskCompletionSource<Resource> Tcs { get; }
        }
    }
}
