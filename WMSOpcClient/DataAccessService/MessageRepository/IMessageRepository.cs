using WMSOpcClient.DataAccessService.Models;

namespace WMSOpcClient.DataAccessService.MessageRepository
{
    public delegate void NewMessageHandler(MessageModel message);
    public interface IMessageRepository
    {
        event NewMessageHandler OnNewMessage;
        void Dispose();
        void Start(string connectionString);
    }
}