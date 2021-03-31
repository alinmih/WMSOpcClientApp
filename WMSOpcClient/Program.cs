using Microsoft.Extensions.Configuration;
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
            //Log.Logger = new LoggerConfiguration()
            //    .MinimumLevel.Debug()
            //    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            //    .Enrich.FromLogContext()
            //    .WriteTo.Console()
            //    .WriteTo.File(@$"{Directory.GetCurrentDirectory()}\Logs\LogFile.txt")
            //    .CreateLogger();
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(baseDir)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", true)
                    .Build();
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();
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
                    services.AddSingleton<IBoxDataRepository, BoxDataRepository>();
                    services.AddSingleton<IMessageRepository, MessageRepository>();

                    // OPC service
                    services.AddSingleton<IOPCClient, OPCClient>();
                }).UseWindowsService().UseSerilog();

    }
}
