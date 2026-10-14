using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UptimeMonitor.Models;
using UptimeMonitor.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Bad settings fail loudly at the first check, with every problem
        // listed, rather than as a confusing error deep in a request.
        services.AddOptions<MonitorOptions>()
            .Bind(context.Configuration.GetSection("Monitor"))
            .Validate(o => !o.Problems().Any(), "Monitor settings are invalid; check Monitor__Endpoints__N__Name/Url.");

        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<EndpointChecker>(client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("azure-uptime-monitor/1.0"));
        services.AddSingleton(_ => TableClientFactory.Create(context.Configuration));
        services.AddSingleton<IResultStore, TableResultStore>();
    })
    .Build();

host.Run();
