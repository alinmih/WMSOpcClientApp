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
            // Create OPC Client
            // 1. Connection
            // 2. Session
            var url = _configuration.GetSection("OPCServerUrl").Value;
            var endpoints = myClientHelperAPI.GetEndpoints(url);
            mySelectedEndpoint = endpoints[0];
            //Check if sessions exists; If yes > delete subscriptions and disconnect
            if (mySession != null && !mySession.Disposed)
            {
                try
                {
                    mySubscription.Delete(true);
                }
                catch
                {
                    ;
                }

                myClientHelperAPI.Disconnect();
                mySession = myClientHelperAPI.Session;

            }
            else
            {
                try
                {
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

                    }
                    else
                    {
                        _logger.LogWarning("Please select an endpoint before connecting to OPC");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                }
            }

            // 3. Subscription
            //this example only supports one item per subscription; remove the following IF loop to add more items
            if (myMonitoredItem != null && mySubscription != null)
            {
                try
                {
                    myMonitoredItem = myClientHelperAPI.RemoveMonitoredItem(mySubscription, myMonitoredItem);
                }
                catch
                {
                    //ignore
                    ;
                }
            }

            try
            {
                //use different item names for correct assignment at the notificatino event
                itemCount++;
                monitoredItemName = "myItem" + itemCount.ToString();
                if (mySubscription == null)
                {
                    mySubscription = myClientHelperAPI.Subscribe(1000);
                }
                myClientHelperAPI.ItemChangedNotification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
            // ************************************************************
            return base.StartAsync(cancellationToken);
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
                _logger.LogError("Error: {error} at {stackTrace}", ex.Message, ex.StackTrace);
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

            _messageRepository.Dispose();
            _logger.LogInformation("The service has been stopped...");
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

            _logger.LogInformation("Processed {boxes} boxes", records.Count);

            _logger.LogInformation("Entering subscription mode...");

            _messageRepository.Start(_configuration.GetConnectionString("Default"));

            var tag = "ns=5;s=Square1";
            myMonitoredItem = myClientHelperAPI.AddMonitoredItem(mySubscription, tag, monitoredItemName, 1);
        }
    }
}
