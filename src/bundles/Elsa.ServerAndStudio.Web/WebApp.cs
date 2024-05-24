﻿using System.Text.Json.Serialization;
using Antlr4.Runtime.Misc;
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
using System.Runtime.InteropServices;

namespace Elsa.ServerAndStudio.Web;

public class WebApp : BackgroundService
{
    public void StartApp()
    {
        const bool useMassTransit = true;
        const bool useProtoActor = false;
        var builder = WebApplication.CreateBuilder([]);

        //builder.Services.ConfigureHttpJsonOptions(options =>
        //{
        //    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        //});


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

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartApp();
    }
}

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(Todo[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
