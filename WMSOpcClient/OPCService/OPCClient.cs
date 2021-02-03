using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using WMSOpcClient.Common;
using WMSOpcClient.DataAccessService.Models;
using System.Linq;

namespace WMSOpcClient.OPCService
{
    public delegate void NewOPCMessageHandler(MessageModel message);
    public delegate void NewSSSCMessageHandler(string sssc);

    public class OPCClient : IOPCClient
    {
        /// <summary>
        /// Event to be fired when message received from SQL
        /// </summary>
        public event NewOPCMessageHandler OnMessageReveived;
        /// <summary>
        /// Event to be fired when PLC reads the SSSC
        /// </summary>
        public event NewSSSCMessageHandler OnSSSCReceived;

        private readonly ILogger<OPCClient> _logger;
        private readonly IConfiguration _configuration;

        public MessageQueue<MessageModel> MessageQueue { get; set; }
        public MessageModel MessageModel { get; set; }

        private Session _mySession;
        private Subscription _mySubscription;
        private UAClientHelperAPI _myClientHelperAPI;
        private EndpointDescription _mySelectedEndpoint;
        private List<String> _myRegisteredNodeIdStrings;

        private bool ToWMS_dataReceived = false;
        private bool ToWMS_dataReady = false;
        private bool FromWMS_watchdog = false;

        public OPCClient(ILogger<OPCClient> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _myClientHelperAPI = new UAClientHelperAPI();
            _myRegisteredNodeIdStrings = new List<string>();
            MessageQueue = new MessageQueue<MessageModel>();
            MessageQueue.ItemAdded += ProcessMessage;
        }

        public bool OpcServerConnected { get; private set; }

        /// <summary>
        /// Connect to OPC server
        /// </summary>
        public void Connect()
        {
            try
            {
                // Create OPC Client
                // 1. Connection & session creation
                _logger.LogInformation("Connecting to OPC Server...");
                while (OpcServerConnected != true)
                {
                    //Thread.Sleep(1000);
                    ConnectToServer();
                }
                OpcServerConnected = true;

                _logger.LogInformation("Connected to OPC Server: {opc}", _configuration.GetSection("OPCServerUrl").Value);

                // 2. Subscription of items
                AddSubscription();
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to OPC server: {message}", ex.Message);
            }
        }

        /// <summary>
        /// Setup OPC connection properties
        /// </summary>
        private void ConnectToServer()
        {
            try
            {
                var url = _configuration.GetSection("OPCServerUrl").Value;
                var endpoints = _myClientHelperAPI.GetEndpoints(url);
                _mySelectedEndpoint = endpoints[0];

                //Register mandatory events (cert and keep alive)
                _myClientHelperAPI.KeepAliveNotification += new KeepAliveEventHandler(Notification_KeepAlive);
                _myClientHelperAPI.CertificateValidationNotification += new CertificateValidationEventHandler(Notification_ServerCertificate);

                //Check for a selected endpoint
                if (_mySelectedEndpoint != null)
                {
                    //Call connect
                    _myClientHelperAPI.Connect(_mySelectedEndpoint, false, "", "").Wait();
                    //Extract the session object for further direct session interactions

                    _mySession = _myClientHelperAPI.Session;
                    OpcServerConnected = true;

                }
                else
                {
                    _logger.LogWarning("Please select an endpoint before connecting to OPC");
                    OpcServerConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                OpcServerConnected = false;
            }

        }

        /// <summary>
        /// Disconnect from OPC server
        /// </summary>
        public void Disconnect()
        {
            MessageQueue.ItemAdded -= ProcessMessage;
            _myClientHelperAPI.Disconnect();
        }

        private void Notification_ServerCertificate(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            try
            {
                //Search for the server's certificate in store; if found -> accept
                X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                X509CertificateCollection certCol = store.Certificates.Find(X509FindType.FindByThumbprint, e.Certificate.Thumbprint, true);
                store.Close();
                if (certCol.Capacity > 0)
                {
                    e.Accept = true;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }
        private void Notification_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(sender, _mySession))
                {
                    return;
                }
                // check for disconnected session.
                if (!ServiceResult.IsGood(e.Status))
                {
                    // try reconnecting using the existing session state
                    _mySession.Reconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error: {error} {stackTrace}", ex.Message, ex.StackTrace);
                OpcServerConnected = false;
                _mySubscription.Delete(true);
                _mySubscription = null;
                _myClientHelperAPI.Disconnect();
                _mySession = null;
                _myClientHelperAPI.KeepAliveNotification -= new KeepAliveEventHandler(Notification_KeepAlive);
                _myClientHelperAPI.CertificateValidationNotification -= new CertificateValidationEventHandler(Notification_ServerCertificate);
                _myClientHelperAPI.ItemChangedNotification -= new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                while (OpcServerConnected != true)
                {
                    //Thread.Sleep(1000);
                    ConnectToServer();
                }
                AddSubscription();
                AddTagsToSubscription(_mySubscription);

            }
        }
        
        /// <summary>
        /// When a item from subscription changes notify the subscribers to process modified message
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="e"></param>
        private void Notification_MonitoredItem(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            if (notification == null)
            {
                return;
            }

            _logger.LogInformation("Monitored Item: {item} has value: {value}", monitoredItem.DisplayName, notification.Value.WrappedValue);

            if (monitoredItem.DisplayName == "ToWMS_dataReady")
            {
                if (notification.Value.WrappedValue == true)
                {
                    var ssscItem = ReadFromOPC();
                    OnSSSCReceived?.Invoke(ssscItem);
                    return;
                }
            }
            if (monitoredItem.DisplayName == "ToWMS_dataReceived")
            {
                if (notification.Value.WrappedValue == true)
                {
                    ToWMS_dataReceived = true;
                    if (MessageQueue.Count > 0)
                    {
                        MessageQueue.Dequeue();
                    }
                    if (MessageModel != null)
                    {
                        OnMessageReveived?.Invoke(MessageModel);
                    }
                    WriteDataReceived(false);
                    return;
                }
            }
        }

        /// <summary>
        /// Create subscription
        /// </summary>
        private void AddSubscription()
        {
            try
            {
                if (_mySubscription == null && _mySession != null)
                {
                    _mySubscription = _myClientHelperAPI.Subscribe(100);
                    _myClientHelperAPI.ItemChangedNotification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        /// <summary>
        /// Add tags to session
        /// </summary>
        private void AddTagsToSession()
        {
            // to be modified to OPCSessionTags after testing
            var tags = _configuration.GetSection("OPCTestSessionTags").GetChildren();
            List<String> nodeIdStrings = new List<String>();
            foreach (var tag in tags)
            {
                nodeIdStrings.Add(tag.Value);
            }
            try
            {
                _myRegisteredNodeIdStrings = _myClientHelperAPI.RegisterNodeIds(nodeIdStrings);
                foreach (var registeredTag in _myRegisteredNodeIdStrings)
                {
                    _logger.LogInformation("Added tags to session: {tags}", registeredTag);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        /// <summary>
        /// Add tags to a subscription
        /// </summary>
        /// <param name="currentSubscription"></param>
        private void AddTagsToSubscription(Subscription currentSubscription)
        {
            // to be modified to OPCSubscriptionTags after testing
            var tags = _configuration.GetSection("OPCTestSubscriptionTags").GetChildren();
            foreach (var tag in tags)
            {
                var monitoredItemName = tag.Key;
                _myClientHelperAPI.AddMonitoredItem(_mySubscription, tag.Value, monitoredItemName, 1);

                _logger.LogInformation("Added tags to subscription {subscr} : {tag}", currentSubscription.DisplayName, tag.Value);
            }
        }

        /// <summary>
        /// Read SSSC value from OPC server
        /// </summary>
        /// <returns></returns>
        private string ReadFromOPC()
        {
            List<String> values = new List<String>();
            try
            {
                values = _myClientHelperAPI.ReadValues(_myRegisteredNodeIdStrings.Where(item => item.Contains("ToWMS_sscc")).ToList());
                return values.ElementAt<String>(0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
                return "";
            }
        }

        /// <summary>
        /// Add tags to session and subscription
        /// </summary>
        public void Start()
        {
            if (_mySession != null)
            {
                if (_mySession.SubscriptionCount > 0)
                {
                    AddTagsToSubscription(_mySubscription);
                }
            }

            AddTagsToSession();
        }

        /// <summary>
        /// Send message to Queue
        /// </summary>
        /// <param name="message"></param>
        public void SendMessageToQueue(MessageModel message)
        {
            _logger.LogInformation("Message {message} enqued and send to process to OPC Server", message.Id);

            MessageQueue.Enqueue(message);
        }

        /// <summary>
        /// Handler fired when the queue receives item
        /// Sends message to OPC server
        /// </summary>
        public void ProcessMessage()
        {
            // TODO - without while processed only one element from Queue
            while (MessageQueue.Count > 0)
            {
                var currentMessage = MessageQueue.Peek();
                MessageModel = currentMessage;

                //SendDataToOPC(message.SSSC, message.OriginalBox, message.Destination, true);
                SendSSSCTagToOPC(currentMessage.SSSC);
                SendOriginalBoxTagToOPC(currentMessage.OriginalBox);
                SendDestinationToOPC(currentMessage.Destination);
                SendDataReadyToOPC(true);

                //******************TO BE REMOVED IN PRODUCTION CODE*****************
                WriteDataReceived(true);
            }
        }

        private void SendDataToOPC(string sssc, bool original, int destination, bool dataReady)
        {
            List<String> values = new List<string>();
            values.Add(sssc);
            values.Add(original.ToString());
            values.Add(destination.ToString());
            values.Add(dataReady.ToString());

            //var myNodeId = myRegisteredNodeIdStrings.Where(items => items.Contains("ToWMS_sscc")).ToList();
            try
            {
                _myClientHelperAPI.WriteValues(values, _myRegisteredNodeIdStrings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }

        /// <summary>
        /// Manual write data received tag
        /// </summary>
        /// <param name="data"></param>
        private void WriteDataReceived(bool data)
        {
            List<String> values = new List<string>();
            List<String> nodeIdStrings = new List<string>();
            values.Add(data.ToString());
            nodeIdStrings.Add("ns=2;s=ToWMS_dataReceived");
            try
            {
                _myClientHelperAPI.WriteValues(values, nodeIdStrings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }

        /// <summary>
        /// Send data ready to Server
        /// </summary>
        /// <param name="sent"></param>
        private void SendDataReadyToOPC(bool sent)
        {
            List<String> values = new List<String>();
            values.Add(sent.ToString());
            try
            {
                _myClientHelperAPI.WriteValues(values, _myRegisteredNodeIdStrings.Where(item => item.Contains("FromWMS_dataReady")).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }

        private void SendDestinationToOPC(int destination)
        {
            List<String> values = new List<String>();
            values.Add(destination.ToString());
            try
            {
                _myClientHelperAPI.WriteValues(values, _myRegisteredNodeIdStrings.Where(item => item.Contains("FromWMS_destination")).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }

        private void SendOriginalBoxTagToOPC(bool originalBox)
        {
            List<String> values = new List<String>();
            values.Add(originalBox.ToString());
            try
            {
                _myClientHelperAPI.WriteValues(values, _myRegisteredNodeIdStrings.Where(item => item.Contains("FromWMS_originalBox")).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }

        private void SendSSSCTagToOPC(string sSSC)
        {
            List<String> values = new List<String>();
            values.Add(sSSC.ToString());
            try
            {
                _myClientHelperAPI.WriteValues(values, _myRegisteredNodeIdStrings.Where(item => item.Contains("FromWMS_sscc")).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message, "Error");
            }
        }
    }
}
