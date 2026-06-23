using ContosoUniversity.Data;
using ContosoUniversity.Data.DbContexts;
using ContosoUniversity.Data.Entities;
using ContosoUniversity.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using ContosoUniversity.Common.Data;
using AutoMapper;
using ContosoUniversity.Common.DTO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ContosoUniversity.Common
{
    // cleaner startup:
    // http://odetocode.com/blogs/scott/archive/2016/08/30/keeping-a-clean-startup-cs-in-asp-net-core.aspx
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCustomizedContext(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
        {
            if (env.IsEnvironment("Testing"))
            {
                services.AddDbContext<ApplicationContext>(optionsBuilder => optionsBuilder.UseInMemoryDatabase("TestDb"));
                services.AddDbContext<SecureApplicationContext>(optionsBuilder => optionsBuilder.UseInMemoryDatabase("TestDb"));
                services.AddDbContext<WebContext>(optionsBuilder => optionsBuilder.UseInMemoryDatabase("TestDb"));
                services.AddDbContext<ApiContext>(optionsBuilder => optionsBuilder.UseInMemoryDatabase("TestDb"));
            }
            else
            {
                // Single provider: SQL Server (Azure SQL in the cloud). Transient-fault
                // resilience is required for Azure SQL; the connection string is supplied
                // by configuration (user-secrets locally, Managed Identity / Key Vault in Azure)
                // and never hardcoded.
                var connectionString = configuration.GetConnectionString("DefaultConnection");

                services.AddDbContext<ApplicationContext>(
                    options => options.UseSqlServer(connectionString,
                        sql => { sql.EnableRetryOnFailure(); sql.MigrationsHistoryTable("Migration", "Contoso"); }));

                services.AddDbContext<SecureApplicationContext>(
                    options => options.UseSqlServer(connectionString,
                        sql => { sql.EnableRetryOnFailure(); sql.MigrationsHistoryTable("IdentityMigration", "Contoso"); }));

                services.AddDbContext<WebContext>(
                    options => options.UseSqlServer(connectionString,
                        sql => { sql.EnableRetryOnFailure(); sql.MigrationsHistoryTable("IdentityMigration", "Contoso"); }));

                services.AddDbContext<ApiContext>(
                    options => options.UseSqlServer(connectionString,
                        sql => { sql.EnableRetryOnFailure(); sql.MigrationsHistoryTable("IdentityMigration", "Contoso"); }));
            }

            services.AddScoped<UnitOfWork<ApplicationContext>, UnitOfWork<ApplicationContext>>();
            services.AddScoped(typeof(IRepository<>), typeof(Repository<,>));

            services.Configure<SampleData>(configuration.GetSection("SampleData"));

            return services;
        }


        public static IServiceCollection AddCustomizedMvc(this IServiceCollection services, IWebHostEnvironment env)
        {
            services.AddMvc();

            return services;
        }

        // identity 2.0
        // oauth - facebook, google
        public static IServiceCollection AddCustomizedIdentity(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
        {
            // Add default identity options for ApplicationUser and IdentityRole
            var identity = services.AddIdentity<ApplicationUser, IdentityRole>();

            // Add EF Store that implements the Identity Store
            identity.AddEntityFrameworkStores<SecureApplicationContext>();

            // Default providers used to generate tokens to reset passwords, change email or passwords, or enable two-factor authentication
            identity.AddDefaultTokenProviders();

            // Change default identity options
            services.Configure<IdentityOptions>(options =>
            {
                options.SignIn.RequireConfirmedEmail = true;
                // issue generating sms friendly verification code
                // https://github.com/aspnet/Identity/issues/1388
                options.Tokens.ChangePhoneNumberTokenProvider = "Phone";
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);
                options.Lockout.MaxFailedAccessAttempts = 3;
            });

            // Admin account will be added to identity db when created.
            var administrator = configuration.GetSection("Administrator");
            if (env.IsDevelopment() && administrator.Exists())
                services.Configure<AdminIdentityOptions>(administrator);

            return services;
        }

        public static IServiceCollection AddCustomizedAuthentication(this IServiceCollection services, IConfiguration configuration)
        {

            // Enable external login using Google
            // https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins
            string googleClientId = configuration["Authentication:Google:ClientId"];
            string googleClientSecret = configuration["Authentication:Google:ClientSecret"];
            if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
            {
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddGoogle(options =>
                {
                    options.ClientId = googleClientId;
                    options.ClientSecret = googleClientSecret;
                });
            }

            // Enable external login using Facebook
            // https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/facebook-logins
            string facebookAppId = configuration["Authentication:Facebook:AppId"];
            string facebookAppSecret = configuration["Authentication:Facebook:AppSecret"];
            if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
            {
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddFacebook(options =>
                {
                    options.AppId = facebookAppId;
                    options.AppSecret = facebookAppSecret;
                });
            }

            services.AddJwtAuthentication(configuration);
            return services;
        }

        public static IServiceCollection AddCustomizedApiAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddJwtAuthentication(configuration);
            return services;
        }



        // sms and email services
        public static IServiceCollection AddCustomizedMessage(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<IEmailSender, AuthMessageSender>();
            services.AddTransient<ISmsSender, AuthMessageSender>();
            services.Configure<AuthMessageSenderOptions>(configuration);
            services.Configure<SMSOptions>(configuration);

            return services;
        }


        public static IServiceCollection AddCustomizedAutoMapper(this IServiceCollection services)
        {
            services.AddAutoMapper(cfg =>
            {
                cfg.CreateMap<DepartmentDTO, Department>().ReverseMap();
            });

            return services;
        }

        private static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // Enable Jwt Tokens
            // https://wildermuth.com/2017/08/19/Two-AuthorizationSchemes-in-ASP-NET-Core-2
            // The signing key is read from configuration, which in Azure is backed by
            // Key Vault (no symmetric secret in source or appsettings).
            string jwtIssuer = configuration["Authentication:Tokens:Issuer"];
            string jwtAudience = configuration["Authentication:Tokens:Audience"];
            string jwtKey = configuration["Authentication:Tokens:Key"];
            if (!string.IsNullOrWhiteSpace(jwtIssuer) && !string.IsNullOrWhiteSpace(jwtAudience) && !string.IsNullOrWhiteSpace(jwtKey))
            {
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                    };
                });
            }
            return services;
        }
    }
}
