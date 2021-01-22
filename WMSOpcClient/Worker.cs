using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using WMSOpcClient.DataAccessService;
using WMSOpcClient.DataAccessService.DataRepository;
using WMSOpcClient.DataAccessService.MessageRepository;
using WMSOpcClient.DataAccessService.Models;
using WMSOpcClient.OPCService;

namespace WMSOpcClient
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBoxDataRepository _scannedData;
        private readonly IMessageRepository _messageRepository;

        /// <summary>
        /// Fields
        /// </summary>
        #region Fields
        private Session mySession;
        private Subscription mySubscription;
        private UAClientHelperAPI myClientHelperAPI;
        private EndpointDescription mySelectedEndpoint;
        private MonitoredItem myMonitoredItem;
        private List<String> myRegisteredNodeIdStrings;
        private ReferenceDescriptionCollection myReferenceDescriptionCollection;
        private List<string[]> myStructList;
        private Int16 itemCount;

        private string monitoredItemName;

        public bool OpcServerConnected { get; private set; } = false;
        #endregion

        public Worker(ILogger<Worker> logger, IConfiguration configuration, IBoxDataRepository scannedData, IMessageRepository messageRepository)
        {
            _logger = logger;
            _configuration = configuration;
            _scannedData = scannedData;
            _messageRepository = messageRepository;
            myClientHelperAPI = new UAClientHelperAPI();
            myRegisteredNodeIdStrings = new List<string>();
        }

        // init clients on startup
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // init client on service startup

            // ********************SQL SECTION*****************************

            // subscribe to SQL message service broker 
            _messageRepository.OnNewMessage += NewMessageReceived;

            // ************************************************************


            // ********************OPC SECTION*****************************
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
                // 2. Subscription of items
                AddSubscription();
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to OPC server: {message}", ex.Message);
            }
            // ************************************************************
            return base.StartAsync(cancellationToken);
        }

        private void AddSubscription()
        {
            try
            {
                //use different item names for correct assignment at the notificatino event
                itemCount++;
                monitoredItemName = "myItem" + itemCount.ToString();
                if (mySubscription == null && mySession != null)
                {
                    mySubscription = myClientHelperAPI.Subscribe(100);
                    myClientHelperAPI.ItemChangedNotification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        private void AddItemsToSubscription(Subscription currentSubscription)
        {
            var tag1 = "ns=5;s=Sinusoid1";
            var tag2 = "ns=5;s=Triangle1";
            myMonitoredItem = myClientHelperAPI.AddMonitoredItem(mySubscription, tag1, monitoredItemName, 1);
            myMonitoredItem = myClientHelperAPI.AddMonitoredItem(mySubscription, tag2, monitoredItemName, 1);
        }

        private void ConnectToServer()
        {
            try
            {
                var url = _configuration.GetSection("OPCServerUrl").Value;
                var endpoints = myClientHelperAPI.GetEndpoints(url);
                mySelectedEndpoint = endpoints[0];

                //Register mandatory events (cert and keep alive)
                myClientHelperAPI.KeepAliveNotification += new KeepAliveEventHandler(Notification_KeepAlive);
                myClientHelperAPI.CertificateValidationNotification += new CertificateValidationEventHandler(Notification_ServerCertificate);

                //Check for a selected endpoint
                if (mySelectedEndpoint != null)
                {
                    //Call connect
                    myClientHelperAPI.Connect(mySelectedEndpoint, false, "", "").Wait();
                    //Extract the session object for further direct session interactions

                    mySession = myClientHelperAPI.Session;
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

        private void Notification_MonitoredItem(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            if (notification == null)
            {
                return;
            }

            Console.WriteLine($"Monitored Item {notification.Value.WrappedValue}");

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
                if (!Object.ReferenceEquals(sender, mySession))
                {
                    return;
                }
                // check for disconnected session.
                if (!ServiceResult.IsGood(e.Status))
                {
                    // try reconnecting using the existing session state
                    mySession.Reconnect();
                }
            }
            catch (Exception ex)
            {
                //Thread.Sleep(1000);
                //var connected = ConnectToServer();
                //if (connected)
                //{
                //    _logger.LogWarning("Reconnected after server connection fails...");
                //    AddItemSubscription();
                //}
                //else
                //{
                _logger.LogError("Error: {error} {stackTrace}", ex.Message, ex.StackTrace);
                OpcServerConnected = false;
                mySubscription.Delete(true);
                mySubscription = null;
                myClientHelperAPI.Disconnect();
                mySession = null;
                myClientHelperAPI.KeepAliveNotification -= new KeepAliveEventHandler(Notification_KeepAlive);
                myClientHelperAPI.CertificateValidationNotification -= new CertificateValidationEventHandler(Notification_ServerCertificate);
                myClientHelperAPI.ItemChangedNotification -= new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                while (OpcServerConnected !=true)
                {
                    //Thread.Sleep(1000);
                    ConnectToServer();
                }
                AddSubscription();
                AddItemsToSubscription(mySubscription);

            }
        }

        private void NewMessageReceived(MessageModel message)
        {
            Console.WriteLine($"- New Message Received: {message.Id}\t {message.SSSC}\t{message.OriginalBox}\t{message.Destination}]");

            var trySend = SendToOPC(message);
            if (trySend == true)
            {
                // Update the record with send flag = true
                var box = new BoxModel
                {
                    Id = message.Id,
                    SSSC = message.SSSC,
                    OriginalBox = message.OriginalBox,
                    Destination = message.Destination
                };

                _scannedData.UpdateSingleBox(box);
            }
        }

        private bool SendToOPC(MessageModel message)
        {
            Console.WriteLine($"Message {message.Id} sent to OPC Server");
            // Send to server the message
            return true;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            // dispose services

            try
            {
                _logger.LogInformation("Try disposing services");
                _messageRepository.Dispose();

                myClientHelperAPI.Disconnect();
                _logger.LogInformation("The service has been stopped...");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot disposing services: {mess}", ex.Message);
            }
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                _logger.LogInformation("Getting unprocessed boxes...");
                var records = await _scannedData.GetBoxes();

                foreach (var record in records)
                {
                    var model = new MessageModel
                    {
                        Id = record.Id,
                        SSSC = record.SSSC,
                        OriginalBox = record.OriginalBox,
                        Destination = record.Destination
                    };
                    var trySend = SendToOPC(model);

                    if (trySend == true)
                    {
                        await _scannedData.UpdateSingleBox(record);
                    }
                }

                _logger.LogInformation("Processed {boxes} boxes from past", records.Count);

                _logger.LogInformation("Entering subscription mode...");

                // start monitor SQL table
                _messageRepository.Start(_configuration.GetConnectionString("Default"));


                if (mySession != null)
                {
                    if (mySession.SubscriptionCount > 0)
                    {
                        AddItemsToSubscription(mySubscription);
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning("Service failure: {message}", ex.Message);
            }
        }
    }
}
