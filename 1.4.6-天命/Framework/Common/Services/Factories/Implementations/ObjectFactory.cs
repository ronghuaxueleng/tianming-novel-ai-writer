using System;
using Microsoft.Extensions.DependencyInjection;

namespace TM.Framework.Common.Services.Factories
{
    public class ObjectFactory : IObjectFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ObjectFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public T Create<T>() where T : class
        {
            return ActivatorUtilities.CreateInstance<T>(_serviceProvider);
        }

        public T Create<T>(params object[] args) where T : class
        {
            return ActivatorUtilities.CreateInstance<T>(_serviceProvider, args);
        }

        public object Create(Type type)
        {
            return ActivatorUtilities.CreateInstance(_serviceProvider, type);
        }

        public object Create(Type type, params object[] args)
        {
            return ActivatorUtilities.CreateInstance(_serviceProvider, type, args);
        }
    }
}
