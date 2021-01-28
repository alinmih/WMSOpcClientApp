using WMSOpcClient.DataAccessService.Models;

namespace WMSOpcClient.OPCService
{
    public interface IOPCClient
    {
        bool OpcServerConnected { get; }

        void Connect();
        void Disconnect();
        public bool SendMessageToOPC(MessageModel message);
        public void Start();
    }
}