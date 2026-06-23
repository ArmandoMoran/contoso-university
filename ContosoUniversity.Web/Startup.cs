using System;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ContosoUniversity.Web;
using ContosoUniversity.Common;
using ContosoUniversity.Web.Helpers;
using ContosoUniversity.Common.Data;
using ContosoUniversity.Common.Interfaces;
using ContosoUniversity.Data.DbContexts;
using AutoMapper;

namespace ContosoUniversity
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env, IConfiguration config)
        {
            CurrentEnvironment = env;
            Configuration = config;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment CurrentEnvironment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCustomizedContext(Configuration, CurrentEnvironment);
            services.AddCustomizedIdentity(Configuration, CurrentEnvironment);
            services.AddCustomizedAuthentication(Configuration);
            services.AddCustomizedMessage(Configuration);
            services.AddCustomizedMvc(CurrentEnvironment);
            services.AddAutoMapper(cfg =>
            {
                cfg.AddProfile<WebProfile>();
            });

            services.AddScoped<IDbInitializer, WebInitializer>();
            services.AddScoped<IModelBindingHelperAdaptor, DefaultModelBindingHelaperAdaptor>();
            services.AddScoped<IUrlHelperAdaptor, UrlHelperAdaptor>();
            services.AddSingleton<IConfiguration>(Configuration);

            // Trust forwarded headers from Front Door / App Service so scheme + client IP are correct.
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            // Persist Data Protection keys to Azure Blob Storage and protect them with Key Vault
            // so Identity auth cookies / antiforgery tokens survive restarts and work across instances.
            ConfigureDataProtection(services);

            if (!string.IsNullOrWhiteSpace(Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            {
                services.AddApplicationInsightsTelemetry();
            }

            services.AddHealthChecks()
                .AddDbContextCheck<ApplicationContext>("db");
        }

        private void ConfigureDataProtection(IServiceCollection services)
        {
            var blobUri = Configuration["DataProtection:BlobUri"];
            var keyId = Configuration["DataProtection:KeyIdentifier"];
            if (!string.IsNullOrWhiteSpace(blobUri))
            {
                var dp = services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(new Uri(blobUri), new DefaultAzureCredential());
                if (!string.IsNullOrWhiteSpace(keyId))
                {
                    dp.ProtectKeysWithAzureKeyVault(new Uri(keyId), new DefaultAzureCredential());
                }
            }
        }

        public void Configure(IApplicationBuilder app,
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory,
            IDbInitializer dbInitializer)
        {
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                dbInitializer.Initialize();
            }
            else if (!env.IsEnvironment("Testing"))
            {
                app.UseExceptionHandler("/Home/Error");
                // HTTPS is always enforced outside Development (was previously a config toggle).
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
                endpoints.MapHealthChecks("/health/ready");
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();
            });
        }
    }
}
