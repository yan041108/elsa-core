using Elsa.Common.DistributedLocks.Noop;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.MassTransit.Options;
using Elsa.Extensions;
using Elsa.ServerAndStudio.Web.Extensions;
using Elsa.MassTransit.Extensions;
using Elsa.ServerAndStudio.Web.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Proto.Persistence.Sqlite;
using Proto.Persistence.SqlServer;
using SQLite;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog;
using Serilog.Core;


const bool useMassTransit = true;
const bool useProtoActor = false;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var services = builder.Services;
var configuration = builder.Configuration;
var sqlServerConnectionString = configuration.GetConnectionString("SqlServer")!;
var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq")!;
var azureServiceBusConnectionString = configuration.GetConnectionString("AzureServiceBus")!;
var identitySection = configuration.GetSection("Identity");
var identityTokenSection = identitySection.GetSection("Tokens");
var massTransitSection = configuration.GetSection("MassTransit");
var massTransitDispatcherSection = configuration.GetSection("MassTransit.Dispatcher");
var heartbeatSection = configuration.GetSection("Heartbeat");
const MassTransitBroker useMassTransitBroker = MassTransitBroker.Memory;

services.Configure<MassTransitOptions>(massTransitSection);
services.Configure<MassTransitWorkflowDispatcherOptions>(massTransitDispatcherSection);

// Add Elsa services.
services
    .AddElsa(elsa =>
    {
        elsa
            .UseSasTokens()
            .UseIdentity(identity =>
            {
                identity.IdentityOptions = options => identitySection.Bind(options);
                identity.TokenOptions = options => identityTokenSection.Bind(options);
                identity.UseConfigurationBasedUserProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedApplicationProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedRoleProvider(options => identitySection.Bind(options));

                identity.UseAdminUserProvider();//dyg add
            })
            
            .UseFluentStorageProvider()//dyg add  Add the Fluent Storage workflow definition provider.  Fluent 存储提供程序将在文件夹中查找工作流定义

            .UseDefaultAuthentication(auth => auth.UseAdminApiKey())//dyg update   old .UseDefaultAuthentication()
            .UseApplicationCluster(x => x.HeartbeatOptions = settings => heartbeatSection.Bind(settings))
            .UseWorkflowManagement(management =>
            {
                if (useMassTransit)
                {
                    management.UseMassTransitDispatcher();
                }

                management.UseEntityFrameworkCore(ef => ef.UseSqlServer(sqlServerConnectionString));
            })
            .UseWorkflowRuntime(runtime =>
            {
                runtime.UseEntityFrameworkCore(ef => ef.UseSqlServer(sqlServerConnectionString));
                
                if (useMassTransit)
                {
                    runtime.UseMassTransitDispatcher();
                }
                
                if (useProtoActor)
                {
                    runtime.UseProtoActor(proto => proto.PersistenceProvider = _ =>
                    {
                        //return new SqlServerProvider(new SqliteConnectionStringBuilder(sqliteConnectionString));
                        return new SqlServerProvider(sqlServerConnectionString);
                    });
                }

                runtime.DistributedLockProvider = _ => new NoopDistributedSynchronizationProvider();
                runtime.WorkflowInboxCleanupOptions = options => configuration.GetSection("Runtime:WorkflowInboxCleanup").Bind(options);
                runtime.WorkflowDispatcherOptions = options => configuration.GetSection("Runtime:WorkflowDispatcher").Bind(options);
            })
            .UseScheduling()
            .UseJavaScript(options => options.AllowClrAccess = true)
            .UseLiquid()
            .UseCSharp()
            // .UsePython()
            .UseHttp(http => http.ConfigureHttpOptions = options => configuration.GetSection("Http").Bind(options))
            .UseEmail(email => email.ConfigureOptions = options => configuration.GetSection("Smtp").Bind(options))
            .UseWebhooks(webhooks => webhooks.WebhookOptions = options => builder.Configuration.GetSection("Webhooks").Bind(options))
            .UseWorkflowsApi()
            .UseRealTimeWorkflows()
            .AddActivitiesFrom<Program>()
            .AddWorkflowsFrom<Program>();

        if (useMassTransit)
        {
            elsa.UseMassTransit(massTransit =>
                {
                    switch (useMassTransitBroker)
                    {
                        case MassTransitBroker.AzureServiceBus:
                            massTransit.UseAzureServiceBus(azureServiceBusConnectionString);
                            break;
                        case MassTransitBroker.RabbitMq:
                            massTransit.UseRabbitMq(rabbitMqConnectionString);
                            break;
                    }
                }
            );
        }
    });

services.AddHealthChecks();

services.AddCors(cors => cors.Configure(configuration.GetSection("CorsPolicy")));

// Razor Pages.
services.AddRazorPages(options => options.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute()));

//services.AddLogging(builder =>
//{
//    builder.AddConfiguration(configuration.GetSection("Logging"));
//    //builder.AddFile(o => o.RootPath = Directory.GetCurrentDirectory());
//    builder.AddConsole();
//});

Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // 排除Microsoft的日志
            .Enrich.FromLogContext() // 注册日志上下文
            .Enrich.WithProperty("IP", "110.110.")
            .WriteTo.Logger(configure => configure // 输出到文件
                .MinimumLevel.Debug()
                .WriteTo.Console() // 输出到控制台
                .WriteTo.File(
                    $"logs\\Debug.txt", // 单个日志文件
                    rollingInterval: RollingInterval.Day,
                  restrictedToMinimumLevel: LogEventLevel.Debug,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u20}] {Message:lj}{NewLine}"
                )
                .WriteTo.File(
                    $"logs\\Information.txt", // 单个日志文件
                    rollingInterval: RollingInterval.Day,
                  restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u20}] {Message:lj}{NewLine}"
                )
                .WriteTo.File(
                    $"logs\\Error.txt", // 单个日志文件
                    rollingInterval: RollingInterval.Day,
                  restrictedToMinimumLevel: LogEventLevel.Error,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u20}] {Message:lj}{NewLine}{Exception}"
                )
                 .WriteTo.File(
                    $"logs\\Warning.txt", // 单个日志文件
                    rollingInterval: RollingInterval.Day,
                  restrictedToMinimumLevel: LogEventLevel.Warning,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u20}] {Message:lj}{NewLine}"
                )
            )
            .CreateLogger();


builder.Host.UseSerilog();

Log.Information("Information up");

//Log.Debug("Debug up");
//Log.Warning("Warning up");
//Log.Error("Error up");

// Configure middleware pipeline.
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

//app.UseHttpsRedirection();  //dyg 删
app.UseBlazorFrameworkFiles();
app.MapHealthChecks("/health");
app.UseRouting();
app.UseCors();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseWorkflowsApi();
app.UseWorkflows();
app.UseWorkflowsSignalRHubs();
app.MapFallbackToPage("/_Host");
app.Run();