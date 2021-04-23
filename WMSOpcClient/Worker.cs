using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly IOPCClient _opcClient;
        private readonly ConnectionStringData _connectionString;
        private int millisecondsTimeout = 0;
        private Stopwatch sw;


        public Worker(ILogger<Worker> logger, IConfiguration configuration, IBoxDataRepository scannedData, IMessageRepository messageRepository, IOPCClient OPCClient, ConnectionStringData connectionString)
        {
            _logger = logger;
            _configuration = configuration;
            _scannedData = scannedData;
            _messageRepository = messageRepository;
            _opcClient = OPCClient;
            _connectionString = connectionString;
            sw = new Stopwatch();
            millisecondsTimeout = int.Parse(_configuration.GetSection("RefreshTime").Value);
        }

        // init clients on startup
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // init client on service startup

            try
            {
                // ********************SQL SECTION*****************************

                // subscribe to SQL message service broker 
                //_messageRepository.OnNewMessage += HandleSQLMessageReceived;
                // start monitor SQL table

                // ********************OPC SECTION*****************************
                //var sqlConnected = _scannedData.IsSQLServerConnected(_configuration.GetConnectionString(_connectionString.SqlConnectionName));
                //_messageRepository.Start(_configuration.GetConnectionString(_connectionString.SqlConnectionName));
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to SQL server: {0}-{1}", ex.Message, ex.StackTrace);
            }
            try
            {
                _opcClient.Connect();

                _opcClient.OnSSSCReceived += HandleSSSCMessageRead;
                _opcClient.OnMessageReveived += HandleOPCMessageReceived;

            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to OPC server: {0}-{1}", ex.Message, ex.StackTrace);
            }
            // ************************************************************
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Process message received from OPC server subscription
        /// Update the record from SQL with value 1
        /// </summary>
        /// <param name="message"></param>
        private void HandleOPCMessageReceived(MessageModel message)
        {
            var box = new BoxModel
            {
                Id = message.Id,
                SSCC = message.SSSC,
                OriginalBox = message.OriginalBox,
                Destination = message.Destination
            };
            _logger.LogInformation("Box {id}-{sssc}-{orig}-{dest} has been sent to OPC", box.Id, box.SSCC, box.OriginalBox, box.Destination);
            _scannedData.UpdateSentToServer(box);
            sw.Stop();
            _logger.LogDebug("Elapsed time:{e}", sw.Elapsed);
            sw.Reset();
        }

        /// <summary>
        /// Event handler which process read SSSC from PLC
        /// </summary>
        /// <param name="sssc"></param>
        private void HandleSSSCMessageRead(string sssc)
        {
            _scannedData.UpdateSSCCRead(sssc);

            _logger.LogInformation("SSCC scanned: {sssc}", sssc);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            // dispose services
            try
            {
                _logger.LogInformation("Try disposing services");
                _messageRepository.Dispose();

                _opcClient.Disconnect();
                _logger.LogInformation("The service has been stopped...");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot disposing services: {0}-{1}", ex.Message, ex.StackTrace);
            }
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                _logger.LogInformation("Getting unprocessed boxes...");

                // start monitor opc 
                _opcClient.Start();

                List<BoxModel> records = await UpdateExistingSQLItems();

                _logger.LogInformation("Processed {boxes} boxes from past", records.Count);

                _logger.LogInformation("Entering subscription mode...");

                // new implementation to search for new records
                while (!stoppingToken.IsCancellationRequested)
                {
                    List<BoxModel> newRecords = new List<BoxModel>();
                    newRecords = await UpdateExistingSQLItems();
                    _logger.LogInformation("Processed {boxes} boxes", newRecords.Count);

                    Thread.Sleep(millisecondsTimeout);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Service failure: {message}", ex.Message);
            }
        }

        private async Task<List<BoxModel>> UpdateExistingSQLItems()
        {
            var records = await _scannedData.GetBoxes();

            if (records.Count > 0 && _opcClient.OpcServerConnected == true)
            {
                foreach (var record in records)
                {
                    var model = new MessageModel
                    {
                        Id = record.Id,
                        SSSC = record.SSCC,
                        OriginalBox = record.OriginalBox,
                        Destination = record.Destination,
                        PickingLocation = record.PickingLocation
                    };
                    
                    sw.Start();
                    _opcClient.SendMessageToQueue(model);
                    await _scannedData.UpdateSentToServer(new BoxModel
                    {
                        Id = model.Id,
                        SSCC = model.SSSC,
                        OriginalBox = model.OriginalBox,
                        Destination = model.Destination,
                        PickingLocation = model.PickingLocation
                    });
                }
            }
            return records;

        }
    }
}
