using System;
using WMSOpcClient.DataAccessService.Models;

namespace WMSOpcClient.OPCService
{
    public interface IOPCClient
    {
        event NewOPCMessageHandler OnMessageReveived;
        event NewSSSCMessageHandler OnSSSCReceived;
        bool OpcServerConnected { get; }

        void Connect();
        void Disconnect();
        public void SendMessageToQueue(MessageModel message);
        public void Start();
    }
}