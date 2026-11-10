using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Amazon.CognitoIdentityProvider;
using CapstoneServices.Galaxy;
using CapstoneServices.NCBI;
using PhyloDivas.Data.New;
using Microsoft.EntityFrameworkCore;
using CapstoneServices;
using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Data;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Builder;
using SearchAPI;

namespace CapstoneDesign
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
          
            builder.Services.AddControllers()
            .ConfigureApplicationPartManager(apm =>
            {
                // Remove ALL existing parts (including CapstoneDesign)
                apm.ApplicationParts.Clear();

                // Add ONLY the current assembly where your SearchController lives
                apm.ApplicationParts.Add(
                    new AssemblyPart(typeof(SearchAPI.Controllers.SearchController).Assembly)
                );
            });

            //read connection string (users-secret/env/appsettings)
            var conn = builder.Configuration.GetConnectionString("PhylodivasDb");

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowReactApp",
                    policy => policy
                        .WithOrigins("http://localhost:3000")
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            });

            // registers EF Core DbContext for runtime
            builder.Services.AddDbContext<PhylodivasContext>(options =>
                options.UseNpgsql(conn));

            // AWS Cognito Configuration
            builder.Services.Configure<AWSOptions>(builder.Configuration.GetSection("AWS"));
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

            builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
            builder.Services.AddAWSService<IAmazonCognitoIdentityProvider>();

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.Authority = builder.Configuration["Authentication:Cognito:Authority"];
                options.ClientId = builder.Configuration["Authentication:Cognito:ClientId"];
                options.ClientSecret = builder.Configuration["Authentication:Cognito:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.CallbackPath = "/auth/callback";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });

            // Add services
            builder.Services.AddControllersWithViews();
            builder.Services.AddScoped<DataCheckService>();
            builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            builder.Services.AddSingleton<GalaxyBustedPipeline>();
            builder.Services.AddSingleton<GalaxyOrthoFinderPipeline>();

            builder.Services.Configure<GalaxyOptions>(builder.Configuration.GetSection("Galaxy"));
            builder.Services.Configure<NcbiOptions>(builder.Configuration.GetSection("Ncbi"));
            builder.Services.AddHttpClient<GalaxyClient>(client =>
                client.Timeout = TimeSpan.FromMinutes(15));
            builder.Services.AddHttpClient<NcbiClient>();

            var app = builder.Build();

            ;

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseCors("AllowReactApp");
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Capstone}/{action=HomePage}");

            app.Run();
        }
    }
}
