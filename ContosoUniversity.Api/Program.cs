using System;
using Azure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ContosoUniversity.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Generic host (normalized with the Web app). EF design-time tooling discovers this.
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(ConfigConfiguration)
                .ConfigureWebHostDefaults(web => web.UseStartup<Startup>());

        public static void ConfigConfiguration(HostBuilderContext context, IConfigurationBuilder config)
        {
            if (context.HostingEnvironment.IsDevelopment())
            {
                config.AddJsonFile("sampleData.json", optional: true, reloadOnChange: false);
                config.AddUserSecrets<Startup>(optional: true);
            }

            config.AddEnvironmentVariables();

            // Key Vault as a configuration source (JWT key, SendGrid, Twilio, OAuth secrets) via
            // Managed Identity in Azure; falls back to az login / Visual Studio locally.
            var builtConfig = config.Build();
            var keyVaultUri = builtConfig["KeyVaultUri"];
            if (!string.IsNullOrWhiteSpace(keyVaultUri))
            {
                config.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
            }
        }
    }
}
