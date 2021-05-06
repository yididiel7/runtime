// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace System.Threading.ResourceLimits
{
    public sealed class SampleConcurrencyLimiter : ResourceLimiter
    {
        private readonly SampleConcurrencyLimiterOptions _options;
        private long _resourceCount;
        private readonly ConcurrentQueue<ConcurrencyLimitRequest> _queue = new ConcurrentQueue<ConcurrencyLimitRequest>();

        public SampleConcurrencyLimiter(SampleConcurrencyLimiterOptions options)
        {
            _options = options;
        }

        public override long EstimatedCount => Interlocked.Read(ref _resourceCount);

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
                return new ValueTask<Resource>(new Resource(null, resource => Release(requestedCount)));
            }

            // Undo resource acquisition
            Interlocked.Add(ref _resourceCount, -requestedCount);

            var request = new ConcurrencyLimitRequest(requestedCount, cancellationToken);
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
                return _resourceCount < _options.ResourceLimit;
            }

            if (Interlocked.Add(ref _resourceCount, requestedCount) <= _options.ResourceLimit)
            {
                resource = new Resource(null, resource => Release(requestedCount));
                return true;
            }

            // Undo resource acquisition
            Interlocked.Add(ref _resourceCount, -requestedCount);
            return false;
        }

        private void Release(long releaseCount)
        {
            // Check for negative requestCount
            Interlocked.Add(ref _resourceCount, -releaseCount);

            while (_queue.TryPeek(out var request))
            {
                if (Interlocked.Add(ref _resourceCount, request.Count) <= _options.ResourceLimit)
                {
                    // Request can be fulfilled
                    _queue.TryDequeue(out var requestToFulfill);

                    if (requestToFulfill == request)
                    {
                        // If requestToFulfill == request, the fulfillment is successful.
                        requestToFulfill.Tcs.SetResult(new Resource(null, resource => Release(request.Count)));
                    }
                    else
                    {
                        // If requestToFulfill != request, there was a concurrent Dequeue:
                        // 1. Reset the resource count.
                        // 2. Put requestToFulfill back in the queue (no longer FIFO) if not null
                        Interlocked.Add(ref _resourceCount, -request.Count);
                        if (requestToFulfill != null)
                        {
                            _queue.Enqueue(requestToFulfill);
                        }
                    }
                }
                else
                {
                    // Request cannot be fulfilled
                    Interlocked.Add(ref _resourceCount, -request.Count);
                    break;
                }
            }
        }

        // TODO: replace with AsyncOperation
        private class ConcurrencyLimitRequest
        {
            public ConcurrencyLimitRequest(long count, CancellationToken token)
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
