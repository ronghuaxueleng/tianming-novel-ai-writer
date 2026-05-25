using System;
using Microsoft.Extensions.DependencyInjection;

namespace TM.Framework.Common.Services
{
    public static class ServiceLocator
    {
        private static IServiceProvider? _serviceProvider;

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public static T Get<T>() where T : class
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("ServiceLocator 未初始化，请先调用 Initialize()");

            return _serviceProvider.GetRequiredService<T>();
        }

        public static T? TryGet<T>() where T : class
        {
            return _serviceProvider?.GetService<T>();
        }

        public static object? GetOrDefault(Type serviceType)
        {
            return _serviceProvider?.GetService(serviceType);
        }

        public static bool IsInitialized => _serviceProvider != null;
    }
}
