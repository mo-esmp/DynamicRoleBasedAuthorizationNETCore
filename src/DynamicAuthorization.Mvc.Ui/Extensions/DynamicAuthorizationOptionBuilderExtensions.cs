using DynamicAuthorization.Mvc.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Ui.Extensions
{
    /// <summary>
    ///     Extension methods for <see cref=" Core.IDynamicAuthorizationOptionBuilder"/>.
    /// </summary>
    public static class DynamicAuthorizationOptionBuilderExtensions
    {
        /// <summary>
        ///     Adds default user management UI to manage roles and users.
        /// </summary>
        /// <param name="builder"> The builder being used to configure the dynamic authorization UI. </param>
        /// <param name="options"> An action to allow configure authorization. </param>
        /// <returns> IDynamicAuthorizationOptionBuilder. </returns>
        /// <exception cref="ArgumentNullException"> Throw if optionsBuilder is null </exception>
        /// <exception cref="ArgumentNullException"> Throw if options is null </exception>
        public static IDynamicAuthorizationOptionBuilder AddUi(this IDynamicAuthorizationOptionBuilder builder, Action<UiAuthorizationOptions> options)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var authorizationOptions = new UiAuthorizationOptions();
            options(authorizationOptions);

            ((DynamicAuthorizationOptionBuilder)builder).Services.AddSingleton(authorizationOptions);

            return builder;
        }
    }
}