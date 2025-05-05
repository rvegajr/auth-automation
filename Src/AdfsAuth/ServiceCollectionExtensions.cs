using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdfsAuth
{
    /// <summary>
    /// Extension methods for configuring ADFS authentication services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds ADFS authentication services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddAdfsAuth(this IServiceCollection services)
        {
            services.AddOptions<AdfsAuthOptions>();
            services.AddTransient<IAdfsAuthService, AdfsAuthService>();
            return services;
        }

        /// <summary>
        /// Adds ADFS authentication services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="configureOptions">A delegate to configure the <see cref="AdfsAuthOptions"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddAdfsAuth(this IServiceCollection services, Action<AdfsAuthOptions> configureOptions)
        {
            services.AddOptions<AdfsAuthOptions>().Configure(configureOptions);
            services.AddTransient<IAdfsAuthService, AdfsAuthService>();
            return services;
        }

        /// <summary>
        /// Adds ADFS authentication services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="configuration">The configuration section for ADFS authentication options.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddAdfsAuth(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AdfsAuthOptions>(configuration);
            services.AddTransient<IAdfsAuthService, AdfsAuthService>();
            return services;
        }
    }
}
