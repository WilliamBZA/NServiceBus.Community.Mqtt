namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>The NServiceBus header names the device reads and writes.</summary>
    internal static class HeaderNames
    {
        public const string MessageId = "NServiceBus.MessageId";
        public const string MessageIntent = "NServiceBus.MessageIntent";
        public const string EnclosedMessageTypes = "NServiceBus.EnclosedMessageTypes";
        public const string ContentType = "NServiceBus.ContentType";
        public const string ConversationId = "NServiceBus.ConversationId";
        public const string CorrelationId = "NServiceBus.CorrelationId";
        public const string RelatedTo = "NServiceBus.RelatedTo";
        public const string ReplyToAddress = "NServiceBus.ReplyToAddress";
        public const string OriginatingEndpoint = "NServiceBus.OriginatingEndpoint";
        public const string TimeSent = "NServiceBus.TimeSent";
        public const string TimeToBeReceived = "NServiceBus.TimeToBeReceived";
        public const string OriginatingSagaId = "NServiceBus.OriginatingSagaId";
        public const string OriginatingSagaType = "NServiceBus.OriginatingSagaType";
        public const string SagaId = "NServiceBus.SagaId";
        public const string SagaType = "NServiceBus.SagaType";

        public const string FailedQ = "NServiceBus.FailedQ";
        public const string TimeOfFailure = "NServiceBus.TimeOfFailure";
        public const string ExceptionType = "NServiceBus.ExceptionInfo.ExceptionType";
        public const string ExceptionMessage = "NServiceBus.ExceptionInfo.Message";
        public const string ExceptionStackTrace = "NServiceBus.ExceptionInfo.StackTrace";
        public const string InnerExceptionType = "NServiceBus.ExceptionInfo.InnerExceptionType";
        public const string ProcessingEndpoint = "NServiceBus.ProcessingEndpoint";

        public const string JsonContentType = "application/json";

        public const string IntentSend = "Send";
        public const string IntentPublish = "Publish";
        public const string IntentReply = "Reply";

        /// <summary>Headers the device sets itself, which the application cannot set on an outgoing message.</summary>
        public static bool IsReserved(string name)
        {
            switch (name)
            {
                case MessageId:
                case MessageIntent:
                case EnclosedMessageTypes:
                case ContentType:
                case ConversationId:
                case CorrelationId:
                case RelatedTo:
                case ReplyToAddress:
                case OriginatingEndpoint:
                case TimeSent:
                case TimeToBeReceived:
                case SagaId:
                case SagaType:
                    return true;
                default:
                    return false;
            }
        }
    }
}
