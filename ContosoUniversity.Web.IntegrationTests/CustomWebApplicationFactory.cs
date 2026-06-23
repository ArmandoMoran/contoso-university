using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ContosoUniversity.Common.Interfaces;
using ContosoUniversity.Data;
using ContosoUniversity.Data.DbContexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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

            builder.ConfigureTestServices(services =>
            {
                // Point every context at the SQL container instead of the in-memory provider
                // that AddCustomizedContext registers for the Testing environment.
                ReplaceWithSqlContainer<ApplicationContext>(services);
                ReplaceWithSqlContainer<SecureApplicationContext>(services);
                ReplaceWithSqlContainer<WebContext>(services);
                ReplaceWithSqlContainer<ApiContext>(services);

                services.AddScoped<IDbInitializer, TestDbInitializer>();

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
                db.Database.EnsureCreated();
                Utilities.InitializeDbForTest(db);
            });
        }

        private void ReplaceWithSqlContainer<TContext>(IServiceCollection services) where TContext : DbContext
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>))
                .ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TContext>(options =>
                options.UseSqlServer(_sqlContainer.GetConnectionString()));
        }
    }
}
