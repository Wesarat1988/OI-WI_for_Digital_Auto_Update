using System;
using Contracts.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorPdfApp.WorkOrders;

public static class WorkOrderServiceCollectionExtensions
{
    public static IServiceCollection AddWorkOrders(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("WorkOrders").Get<WorkOrderReaderOptions>() ?? new WorkOrderReaderOptions();
        services.AddSingleton(options);

        if (options.Source == WorkOrderSource.SqlServer)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("WorkOrders:ConnectionString must be configured when Source is SqlServer.");
            }

            services.AddDbContext<WorkOrderDbContext>(builder =>
            {
                builder.UseSqlServer(options.ConnectionString);
            });

            services.AddScoped<IWorkOrderReader, SqlWorkOrderReader>();
        }
        else
        {
            services.AddHttpClient(RestWorkOrderReader.ClientName, client =>
            {
                client.BaseAddress = new Uri(options.ApiBaseUrl ?? "http://localhost/");

                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                }
            });

            services.AddScoped<IWorkOrderReader, RestWorkOrderReader>();
        }

        return services;
    }
}
