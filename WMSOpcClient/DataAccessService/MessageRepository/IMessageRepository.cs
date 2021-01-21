namespace WMSOpcClient.DataAccessService.MessageRepository
{
    public interface IMessageRepository
    {
        event NewMessageHandler OnNewMessage;
        void Dispose();
        void Start(string connectionString);
    }
}