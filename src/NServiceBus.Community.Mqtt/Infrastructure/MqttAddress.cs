namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Maps NServiceBus queue addresses to MQTT topics. The same mapping is used for receiving, sending and the error and audit addresses.
    /// </summary>
    static class MqttAddress
    {
        public static string ToTopic(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("An address cannot be empty when using the MQTT transport.", nameof(address));
            }

            if (address.Contains('+') || address.Contains('#'))
            {
                throw Invalid(address, "it contains the MQTT wildcard character '+' or '#'");
            }

            if (address[0] == '$')
            {
                throw Invalid(address, "it starts with '$', which MQTT reserves for broker topics");
            }

            return address.Replace('_', '/');
        }

        public static string ToTopic(QueueAddress address)
        {
            var transportAddress = address.BaseAddress;

            if (address.Discriminator != null)
            {
                transportAddress += "-" + address.Discriminator;
            }

            if (address.Qualifier != null)
            {
                transportAddress += "." + address.Qualifier;
            }

            return ToTopic(transportAddress);
        }

        static ArgumentException Invalid(string address, string reason) =>
            new($"The address '{address}' cannot be used with the MQTT transport because {reason}. Addresses map to MQTT topics, so they cannot contain '+' or '#' and cannot start with '$'.", nameof(address));
    }
}
