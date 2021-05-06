// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.ResourceLimits;
using System.Threading.Tasks;

namespace System.Threading.Channels
{
    public sealed class LimitedChannel<T> : Channel<T>
    {
        private readonly LimitedChannelOptions _options;

        /// <summary>Task that indicates the channel has completed.</summary>
        private readonly TaskCompletionSource _completion;
        /// <summary>The items in the channel.</summary>
        private readonly ConcurrentQueue<T> _items = new ConcurrentQueue<T>();
        /// <summary>Readers blocked reading from the channel.</summary>
        private readonly Deque<AsyncOperation<T>> _blockedReaders = new Deque<AsyncOperation<T>>();
        /// <summary>Whether to force continuations to be executed asynchronously from producer writes.</summary>
        private readonly bool _runContinuationsAsynchronously;

        /// <summary>Readers waiting for a notification that data is available.</summary>
        private AsyncOperation<bool>? _waitingReadersTail;
        /// <summary>Set to non-null once Complete has been called.</summary>
        private Exception? _doneWriting;

        internal LimitedChannel(LimitedChannelOptions options)
        {
            _options = options;
            _runContinuationsAsynchronously = !options.AllowSynchronousContinuations;
            _completion = new TaskCompletionSource(!options.AllowSynchronousContinuations ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

            Reader = new LimitedChannelReader(this);
            Writer = new LimitedChannelWriter(this);
        }

        // This is identical to UnboundedChannelReader.
        private sealed class LimitedChannelReader : ChannelReader<T>
        {
            internal readonly LimitedChannel<T> _parent;
            private readonly AsyncOperation<T> _readerSingleton;
            private readonly AsyncOperation<bool> _waiterSingleton;

            internal LimitedChannelReader(LimitedChannel<T> parent)
            {
                _parent = parent;
                _readerSingleton = new AsyncOperation<T>(parent._runContinuationsAsynchronously, pooled: true);
                _waiterSingleton = new AsyncOperation<bool>(parent._runContinuationsAsynchronously, pooled: true);
            }

            public override Task Completion => _parent._completion.Task;

            public override bool CanCount => true;

            public override int Count => _parent._items.Count;

            public override ValueTask<T> ReadAsync(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<T>(Task.FromCanceled<T>(cancellationToken));
                }

                // Dequeue an item if we can.
                LimitedChannel<T> parent = _parent;
                if (parent._items.TryDequeue(out T? item))
                {
                    CompleteIfDone(parent);
                    return new ValueTask<T>(item);
                }

                lock (parent.SyncObj)
                {
                    //parent.AssertInvariants();

                    // Try to dequeue again, now that we hold the lock.
                    if (parent._items.TryDequeue(out item))
                    {
                        CompleteIfDone(parent);
                        return new ValueTask<T>(item);
                    }

                    // There are no items, so if we're done writing, fail.
                    if (parent._doneWriting != null)
                    {
                        return ChannelUtilities.GetInvalidCompletionValueTask<T>(parent._doneWriting);
                    }

                    // If we're able to use the singleton reader, do so.
                    if (!cancellationToken.CanBeCanceled)
                    {
                        AsyncOperation<T> singleton = _readerSingleton;
                        if (singleton.TryOwnAndReset())
                        {
                            parent._blockedReaders.EnqueueTail(singleton);
                            return singleton.ValueTaskOfT;
                        }
                    }

                    // Otherwise, create and queue a reader.
                    var reader = new AsyncOperation<T>(parent._runContinuationsAsynchronously, cancellationToken);
                    parent._blockedReaders.EnqueueTail(reader);
                    return reader.ValueTaskOfT;
                }
            }

            public override bool TryRead([MaybeNullWhen(false)] out T item)
            {
                LimitedChannel<T> parent = _parent;

                // Dequeue an item if we can
                if (parent._items.TryDequeue(out item))
                {
                    CompleteIfDone(parent);
                    return true;
                }

                item = default;
                return false;
            }

            private void CompleteIfDone(LimitedChannel<T> parent)
            {
                if (parent._doneWriting != null && parent._items.IsEmpty)
                {
                    // If we've now emptied the items queue and we're not getting any more, complete.
                    ChannelUtilities.Complete(parent._completion, parent._doneWriting);
                }
            }

            public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                }

                if (!_parent._items.IsEmpty)
                {
                    return new ValueTask<bool>(true);
                }

                LimitedChannel<T> parent = _parent;

                lock (parent.SyncObj)
                {
                    //parent.AssertInvariants();

                    // Try again to read now that we're synchronized with writers.
                    if (!parent._items.IsEmpty)
                    {
                        return new ValueTask<bool>(true);
                    }

                    // There are no items, so if we're done writing, there's never going to be data available.
                    if (parent._doneWriting != null)
                    {
                        return parent._doneWriting != ChannelUtilities.s_doneWritingSentinel ?
                            new ValueTask<bool>(Task.FromException<bool>(parent._doneWriting)) :
                            default;
                    }

                    // If we're able to use the singleton waiter, do so.
                    if (!cancellationToken.CanBeCanceled)
                    {
                        AsyncOperation<bool> singleton = _waiterSingleton;
                        if (singleton.TryOwnAndReset())
                        {
                            ChannelUtilities.QueueWaiter(ref parent._waitingReadersTail, singleton);
                            return singleton.ValueTaskOfT;
                        }
                    }

                    // Otherwise, create and queue a waiter.
                    var waiter = new AsyncOperation<bool>(parent._runContinuationsAsynchronously, cancellationToken);
                    ChannelUtilities.QueueWaiter(ref parent._waitingReadersTail, waiter);
                    return waiter.ValueTaskOfT;
                }
            }
        }

        private sealed class LimitedChannelWriter : ChannelWriter<T>
        {
            internal readonly LimitedChannel<T> _parent;

            internal LimitedChannelWriter(LimitedChannel<T> parent)
            {
                _parent = parent;
            }

            // This is identical to UnboundedChannelWriter.TryComplete
            public override bool TryComplete(Exception? error)
            {
                LimitedChannel<T> parent = _parent;
                bool completeTask;

                lock (parent.SyncObj)
                {
                    //parent.AssertInvariants();

                    // If we've already marked the channel as completed, bail.
                    if (parent._doneWriting != null)
                    {
                        return false;
                    }

                    // Mark that we're done writing.
                    parent._doneWriting = error ?? ChannelUtilities.s_doneWritingSentinel;
                    completeTask = parent._items.IsEmpty;
                }

                // If there are no items in the queue, complete the channel's task,
                // as no more data can possibly arrive at this point.  We do this outside
                // of the lock in case we'll be running synchronous completions, and we
                // do it before completing blocked/waiting readers, so that when they
                // wake up they'll see the task as being completed.
                if (completeTask)
                {
                    ChannelUtilities.Complete(parent._completion, error);
                }

                // At this point, _blockedReaders and _waitingReaders will not be mutated:
                // they're only mutated by readers while holding the lock, and only if _doneWriting is null.
                // freely manipulate _blockedReaders and _waitingReaders without any concurrency concerns.
                ChannelUtilities.FailOperations<AsyncOperation<T>, T>(parent._blockedReaders, ChannelUtilities.CreateInvalidCompletionException(error));
                ChannelUtilities.WakeUpWaiters(ref parent._waitingReadersTail, result: false, error: error);

                // Successfully transitioned to completed.
                return true;
            }

            public override bool TryWrite(T item)
            {
                if (_parent._options.Limiter.TryAcquire(out Resource resource))
                {
                    using (resource)
                    {
                        return TryWriteCore(item);
                    }
                }

                return false;
            }

            public bool TryWriteCore(T item)
            {
                LimitedChannel<T> parent = _parent;
                while (true)
                {
                    AsyncOperation<T>? blockedReader = null;
                    AsyncOperation<bool>? waitingReadersTail = null;
                    lock (parent.SyncObj)
                    {
                        // If writing has already been marked as done, fail the write.
                        //parent.AssertInvariants();
                        if (parent._doneWriting != null)
                        {
                            return false;
                        }

                        // If there aren't any blocked readers, just add the data to the queue,
                        // and let any waiting readers know that they should try to read it.
                        // We can only complete such waiters here under the lock if they run
                        // continuations asynchronously (otherwise the synchronous continuations
                        // could be invoked under the lock).  If we don't complete them here, we
                        // need to do so outside of the lock.
                        if (parent._blockedReaders.IsEmpty)
                        {
                            parent._items.Enqueue(item);
                            waitingReadersTail = parent._waitingReadersTail;
                            if (waitingReadersTail == null)
                            {
                                return true;
                            }
                            parent._waitingReadersTail = null;
                        }
                        else
                        {
                            // There were blocked readers.  Grab one, and then complete it outside of the lock.
                            blockedReader = parent._blockedReaders.DequeueHead();
                        }
                    }

                    if (blockedReader != null)
                    {
                        // Complete the reader.  It's possible the reader was canceled, in which
                        // case we loop around to try everything again.
                        if (blockedReader.TrySetResult(item))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // Wake up all of the waiters.  Since we've released the lock, it's possible
                        // we could cause some spurious wake-ups here, if we tell a waiter there's
                        // something available but all data has already been removed.  It's a benign
                        // race condition, though, as consumers already need to account for such things.
                        ChannelUtilities.WakeUpWaiters(ref waitingReadersTail, result: true);
                        return true;
                    }
                }
            }

            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken)
            {
                Exception? doneWriting = _parent._doneWriting;
                return
                    cancellationToken.IsCancellationRequested ? new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken)) :
                    doneWriting == null ? WaitForLimiterAsync(cancellationToken) : // unbounded writing can always be done if we haven't completed
                    doneWriting != ChannelUtilities.s_doneWritingSentinel ? new ValueTask<bool>(Task.FromException<bool>(doneWriting)) :
                    default;
            }

            private async ValueTask<bool> WaitForLimiterAsync(CancellationToken cancellationToken)
            {
                try
                {
                    // No need to release resource since the count was 0
                    // TODO: Check completion status
                    await _parent._options.Limiter.AcquireAsync(0, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (ResourceExhaustedException)
                {
                    return false;
                }
            }

            public async override ValueTask WriteAsync(T item, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (await _parent._options.Limiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!TryWriteCore(item))
                    {
                        throw ChannelUtilities.CreateInvalidCompletionException(_parent._doneWriting);
                    }
                }
            }
        }

        /// <summary>Gets the object used to synchronize access to all state on this instance.</summary>
        private object SyncObj => _items;
    }
}
