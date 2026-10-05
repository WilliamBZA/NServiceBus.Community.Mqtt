using System;
using Microsoft.Extensions.Logging;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Writes to the configured logger. The text is passed on as it is: the logger extensions format their message, and a message that holds braces,
    /// as an exception message or a payload can, must not be taken for a format.
    /// </summary>
    internal sealed class EndpointLog
    {
        static readonly EventId NoEvent = new EventId(0, null);

        readonly ILogger logger;

        public EndpointLog(ILogger logger)
        {
            this.logger = logger;
        }

        public void Debug(string message)
        {
            Write(LogLevel.Debug, message, null);
        }

        public void Information(string message)
        {
            Write(LogLevel.Information, message, null);
        }

        public void Warning(string message, Exception exception)
        {
            Write(LogLevel.Warning, message, exception);
        }

        public void Error(string message, Exception exception)
        {
            Write(LogLevel.Error, message, exception);
        }

        void Write(LogLevel level, string message, Exception exception)
        {
            try
            {
                if (logger.IsEnabled(level))
                {
                    logger.Log(level, NoEvent, message, exception, null);
                }
            }
            catch (Exception)
            {
                // a logger that fails must not take the endpoint's threads down
            }
        }
    }
}
