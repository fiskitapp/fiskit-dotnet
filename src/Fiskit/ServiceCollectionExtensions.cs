using System;
using Microsoft.Extensions.DependencyInjection;

namespace Fiskit
{
    /// <summary>Registers <see cref="FiskitClient"/> in ASP.NET Core.</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds a singleton <see cref="FiskitClient"/> using IHttpClientFactory.
        /// <code>
        /// builder.Services.AddFiskit(o => o.ApiKey = builder.Configuration["Fiskit:ApiKey"]);
        /// </code>
        /// </summary>
        public static IServiceCollection AddFiskit(this IServiceCollection services, Action<FiskitOptions>? configure = null)
        {
            var options = new FiskitOptions();
            configure?.Invoke(options);
            services.AddSingleton(options);
            services.AddHttpClient("fiskit", client => client.Timeout = options.Timeout);
            services.AddSingleton(provider => new FiskitClient(options.ApiKey, options,
                provider.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("fiskit")));
            return services;
        }
    }
}
