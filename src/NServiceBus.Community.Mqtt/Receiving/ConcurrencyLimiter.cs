namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Limits how many messages are processed at the same time, and lets the limit change while messages are waiting. A
    /// <see cref="SemaphoreSlim"/> cannot do the second part, because it has no way to take capacity back or to change its maximum.
    /// </summary>
    sealed class ConcurrencyLimiter(int limit)
    {
        public int Limit
        {
            get
            {
                lock (gate)
                {
                    return currentLimit;
                }
            }
        }

        /// <summary>Completes when a slot is free, and takes it. Every completed wait must be paired with <see cref="Release"/>.</summary>
        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (running < currentLimit && waiters.Count == 0)
                {
                    running++;
                    return Task.CompletedTask;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Enqueue(waiter);

                if (cancellationToken.CanBeCanceled)
                {
                    // A cancelled waiter must not be handed a slot, so cancelling completes its source and Drain skips completed sources.
                    var registration = cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetCanceled(), waiter);
                    _ = waiter.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(), registration, TaskScheduler.Default);
                }

                return waiter.Task;
            }
        }

        public void Release()
        {
            lock (gate)
            {
                running--;
                Drain();
            }
        }

        /// <summary>Takes effect for the next message. Handlers that are already running are not interrupted when the limit is lowered.</summary>
        public void SetLimit(int newLimit)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(newLimit, 1);

            lock (gate)
            {
                currentLimit = newLimit;
                Drain();
            }
        }

        void Drain()
        {
            while (running < currentLimit && waiters.TryDequeue(out var waiter))
            {
                if (waiter.TrySetResult())
                {
                    running++;
                }
            }
        }

        readonly object gate = new();
        readonly Queue<TaskCompletionSource> waiters = new();
        int currentLimit = limit >= 1 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), limit, "The concurrency limit must be at least 1.");
        int running;
    }
}
