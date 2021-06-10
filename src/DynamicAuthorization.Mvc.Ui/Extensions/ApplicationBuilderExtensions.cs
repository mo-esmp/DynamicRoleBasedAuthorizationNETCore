using DynamicAuthorization.Mvc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Ui
{
    /// <summary>
    ///     Contains extensions for configuring routing on an <see cref="IApplicationBuilder"/>.
    /// </summary>
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        ///     Adds a <see cref="UiMiddleware"/> middleware to the specified <see cref="IApplicationBuilder"/>.
        /// </summary>
        /// <param name="applicationBuilder">
        ///     The <see cref="IApplicationBuilder"/> to add the middleware to.
        /// </param>
        /// <param name="options"> The options to configure dynamic authorization UI. </param>
        /// <returns> IApplicationBuilder. </returns>
        /// <exception cref="ArgumentNullException"> throw if applicationBuilder if null </exception>
        public static IApplicationBuilder UseDynamicAuthorizationUi(this IApplicationBuilder applicationBuilder, Action<UiOptions> options = null)
        {
            if (applicationBuilder == null)
                throw new ArgumentNullException(nameof(applicationBuilder));

            var uiOptions = new UiOptions();
            options?.Invoke(uiOptions);

            var scope = applicationBuilder.ApplicationServices.CreateScope();
            var authOptions = scope.ServiceProvider.GetService<UiAuthorizationOptions>();
            uiOptions.AuthType = authOptions.AuthenticationType.ToString();

            scope.Dispose();

            return applicationBuilder.UseMiddleware<UiMiddleware>(uiOptions);
        }
    }
}