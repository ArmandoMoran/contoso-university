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
using ContosoUniversity.Common;
using ContosoUniversity.Common.Data;
using ContosoUniversity.Common.Interfaces;
using ContosoUniversity.Data.DbContexts;
using Microsoft.OpenApi.Models;
using AutoMapper;

namespace ContosoUniversity.Api
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IWebHostEnvironment CurrentEnvironment { get; }

        public Startup(IWebHostEnvironment env, IConfiguration config)
        {
            CurrentEnvironment = env;
            Configuration = config;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCustomizedContext(Configuration, CurrentEnvironment)
                .AddAutoMapper(cfg =>
                {
                    cfg.AddProfile<ApiProfile>();
                })
                .AddCustomizedMvc(CurrentEnvironment)
                .AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Contoso University Api", Version = "v1" });
                });

            services.AddCustomizedApiAuthentication(Configuration);
            services.AddScoped<UnitOfWork<ApiContext>, UnitOfWork<ApiContext>>();
            services.AddScoped<IDbInitializer, ApiInitializer>();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            ConfigureDataProtection(services);

            if (!string.IsNullOrWhiteSpace(Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            {
                services.AddApplicationInsightsTelemetry();
            }

            services.AddHealthChecks()
                .AddDbContextCheck<ApiContext>("db");
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

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory, IDbInitializer dbInitializer)
        {
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                dbInitializer.Initialize();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Contoso API V1");
            });

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
                endpoints.MapHealthChecks("/health/ready");
                endpoints.MapControllers();
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });
        }

        public void ConfigureTesting(IApplicationBuilder app, IDbInitializer dbInitializer)
        {
            dbInitializer.Initialize();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
