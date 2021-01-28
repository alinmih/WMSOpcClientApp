using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WMSOpcClient.DataAccessService;
using WMSOpcClient.DataAccessService.DataRepository;
using WMSOpcClient.DataAccessService.MessageRepository;
using WMSOpcClient.OPCService;

namespace WMSOpcClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(@$"{Directory.GetCurrentDirectory()}\Logs\LogFile.txt")
                .CreateLogger();
            try
            {
                Log.Information("Starting up the service");

                CreateHostBuilder(args).Build().Run();

                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "There was a problem starting the service");

                return;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();

                    services.AddSingleton(new ConnectionStringData
                    {
                        SqlConnectionName = "Default"
                    });

                    // SQL services
                    services.AddTransient<IBoxDataRepository, BoxDataRepository>();
                    services.AddTransient<IMessageRepository, MessageRepository>();

                    // OPC service
                    services.AddTransient<IOPCClient, OPCClient>();
                }).UseWindowsService().UseSerilog();

    }
}
