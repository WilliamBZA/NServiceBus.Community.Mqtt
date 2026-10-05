using System;
using System.Threading;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    internal interface IClock
    {
        DateTime UtcNow { get; }
    }

    internal interface IDelay
    {
        /// <summary>Waits, unless <paramref name="interrupt" /> is set first. Returns true when it was interrupted.</summary>
        bool Wait(TimeSpan duration, ManualResetEvent interrupt);
    }

    internal interface IIdGenerator
    {
        string NewId();
    }

    /// <summary>The sources of time, waiting and identity, so that tests can fix them.</summary>
    internal sealed class EndpointServices
    {
        public EndpointServices(IClock clock, IDelay delay, IIdGenerator ids)
        {
            Clock = clock;
            Delay = delay;
            Ids = ids;
        }

        public IClock Clock { get; private set; }

        public IDelay Delay { get; private set; }

        public IIdGenerator Ids { get; private set; }

        public static EndpointServices CreateDefault()
        {
            return new EndpointServices(new SystemClock(), new SystemDelay(), new GuidIdGenerator());
        }

        sealed class SystemClock : IClock
        {
            public DateTime UtcNow
            {
                get { return DateTime.UtcNow; }
            }
        }

        sealed class SystemDelay : IDelay
        {
            public bool Wait(TimeSpan duration, ManualResetEvent interrupt)
            {
                var milliseconds = (int)(duration.Ticks / TimeSpan.TicksPerMillisecond);
                return interrupt.WaitOne(milliseconds, false);
            }
        }

        sealed class GuidIdGenerator : IIdGenerator
        {
            public string NewId()
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
