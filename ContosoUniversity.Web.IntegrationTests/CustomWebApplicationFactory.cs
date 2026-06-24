using System.Collections.Generic;
using System.Threading.Tasks;
using ContosoUniversity.Common.Interfaces;
using ContosoUniversity.Data.DbContexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace ContosoUniversity.Web.IntegrationTests
{
    public class TestDbInitializer : IDbInitializer
    {
        public void Initialize()
        {
            // Schema + seed are handled by the factory against the SQL container.
        }
    }

    // Integration tests run against a real SQL Server in a throwaway Docker container
    // (Testcontainers) — the same provider as production — instead of EF InMemory.
    // Requires a running Docker daemon.
    public class CustomWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup>, IAsyncLifetime
        where TStartup : class
    {
        private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();

        async Task IAsyncLifetime.InitializeAsync()
        {
            await _sqlContainer.StartAsync();
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await _sqlContainer.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Supplying a connection string makes AddCustomizedContext use SQL Server (the
            // container) instead of the in-memory provider — a single provider, no conflict.
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ConnectionStrings:DefaultConnection"] = _sqlContainer.GetConnectionString()
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IDbInitializer, TestDbInitializer>();

                // Create the schema and seed the container once, before the tests run.
                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
                db.Database.EnsureCreated();
                Utilities.InitializeDbForTest(db);
            });
        }
    }
}
