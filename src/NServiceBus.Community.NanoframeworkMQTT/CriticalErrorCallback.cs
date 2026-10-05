using System;

namespace NServiceBus
{
    /// <summary>
    /// Called when the endpoint meets an error it cannot recover from by itself: a message that cannot be forwarded to the error queue, another
    /// client taking over the device's session, or a failure of the endpoint's own processing loop. The endpoint keeps running. The callback runs on
    /// one of the endpoint's threads, so it should return quickly.
    /// </summary>
    public delegate void CriticalErrorCallback(string description, Exception exception);
}
