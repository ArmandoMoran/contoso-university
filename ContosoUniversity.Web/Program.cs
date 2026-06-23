using System;
using Azure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContosoUniversity
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Generic host. The Startup class is retained so the WebApplicationFactory
        // integration tests keep working.
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(ConfigConfiguration)
                .ConfigureLogging(ConfigureLogger)
                .ConfigureWebHostDefaults(web => web.UseStartup<Startup>());

        public static void ConfigConfiguration(HostBuilderContext context, IConfigurationBuilder config)
        {
            if (context.HostingEnvironment.IsDevelopment())
            {
                config.AddJsonFile("sampleData.json", optional: true, reloadOnChange: false);
                config.AddUserSecrets<Startup>(optional: true);
            }

            config.AddEnvironmentVariables();

            // Layer Azure Key Vault as a configuration source when a vault URI is present.
            // DefaultAzureCredential uses the App Service Managed Identity in Azure and falls
            // back to az login / Visual Studio locally — no secret is needed to read secrets.
            var builtConfig = config.Build();
            var keyVaultUri = builtConfig["KeyVaultUri"];
            if (!string.IsNullOrWhiteSpace(keyVaultUri))
            {
                config.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
            }
        }

        static void ConfigureLogger(HostBuilderContext ctx, ILoggingBuilder logging)
        {
            logging.AddConfiguration(ctx.Configuration.GetSection("Logging"));
            logging.AddConsole();
            logging.AddDebug();
        }
    }
}
