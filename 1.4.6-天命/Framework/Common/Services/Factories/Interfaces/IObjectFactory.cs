using System;

namespace TM.Framework.Common.Services.Factories
{
    public interface IObjectFactory
    {
        T Create<T>() where T : class;

        T Create<T>(params object[] args) where T : class;

        object Create(Type type);

        object Create(Type type, params object[] args);
    }
}
