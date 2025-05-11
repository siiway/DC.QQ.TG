using System;
using System.Threading.Tasks;
using DC.QQ.TG.Models;

namespace DC.QQ.TG.Interfaces
{
    public interface IMessageAdapter
    {
        /// <summary>
        /// The platform this adapter is for
        /// </summary>
        MessageSource Platform { get; }

        /// <summary>
        /// Initialize the adapter
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Send a message to the platform
        /// </summary>
        /// <param name="message">The message to send</param>
        Task SendMessageAsync(Message message);

        /// <summary>
        /// Event that is triggered when a message is received
        /// </summary>
        event EventHandler<Message> MessageReceived;

        /// <summary>
        /// Start listening for messages
        /// </summary>
        Task StartListeningAsync();

        /// <summary>
        /// Stop listening for messages
        /// </summary>
        Task StopListeningAsync();
    }
}
