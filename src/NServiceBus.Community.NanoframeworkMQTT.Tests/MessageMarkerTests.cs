using System;
using nanoFramework.TestFramework;
using NServiceBus;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class MessageMarkerTests
    {
        [TestMethod]
        public void Event_and_command_markers_derive_from_the_message_marker()
        {
            Assert.AreEqual(typeof(IMessage), typeof(IEvent).GetInterfaces()[0]);
            Assert.AreEqual(typeof(IMessage), typeof(ICommand).GetInterfaces()[0]);
        }
    }
}
