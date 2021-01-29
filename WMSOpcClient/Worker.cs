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
        private readonly IOPCClient _opcClient;
        
        public Worker(ILogger<Worker> logger, IConfiguration configuration, IBoxDataRepository scannedData, IMessageRepository messageRepository, IOPCClient OPCClient)
        {
            _logger = logger;
            _configuration = configuration;
            _scannedData = scannedData;
            _messageRepository = messageRepository;
            _opcClient = OPCClient;  
        }

        // init clients on startup
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // init client on service startup

            // ********************SQL SECTION*****************************

            // subscribe to SQL message service broker 
            _messageRepository.OnNewMessage += HandleSQLMessageReceived;


            // ************************************************************


            // ********************OPC SECTION*****************************
            _opcClient.OnSSSCReceived += HandleSSSCMessageRead;
            _opcClient.OnMessageReveived += HandleOPCMessageReceived;
            try
            {
                _opcClient.Connect();
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to OPC server: {message}", ex.Message);
            }
            // ************************************************************
            return base.StartAsync(cancellationToken);
        }

        private void HandleOPCMessageReceived(MessageModel message)
        {
            var box = new BoxModel
            {
                Id = message.Id,
                SSSC = message.SSSC,
                OriginalBox = message.OriginalBox,
                Destination = message.Destination
            };
            _logger.LogInformation("Box {id}-{sssc}-{orig}-{dest} has been sent to OPC", box.Id, box.SSSC, box.OriginalBox, box.Destination);
            _scannedData.UpdateSingleBox(box);
        }

        private void HandleSSSCMessageRead(string sssc)
        {
            _logger.LogInformation("SSSC scanned: {sssc}", sssc);
        }

        private void HandleSQLMessageReceived(MessageModel message)
        {
            //Console.WriteLine($"- New Message Received: {message.Id}\t {message.SSSC}\t{message.OriginalBox}\t{message.Destination}]");
            _logger.LogInformation("New Message Received:{id}/{sssc}/{orig}/{dest}", message.Id, message.SSSC, message.OriginalBox, message.Destination);

            _opcClient.SendMessageToOPC(message);
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
                    _opcClient.SendMessageToOPC(model);

                }

                _logger.LogInformation("Processed {boxes} boxes from past", records.Count);

                _logger.LogInformation("Entering subscription mode...");

                // start monitor SQL table
                _messageRepository.Start(_configuration.GetConnectionString("Default"));

                // start monitor opc 
                _opcClient.Start();
                
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Service failure: {message}", ex.Message);
            }
        }
    }
}
