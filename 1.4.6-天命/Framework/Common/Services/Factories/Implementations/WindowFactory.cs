using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace TM.Framework.Common.Services.Factories
{
    public class WindowFactory : IWindowFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public WindowFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Window CreateWindow(Type windowType)
        {
            if (!typeof(Window).IsAssignableFrom(windowType))
                throw new ArgumentException($"类型 {windowType.Name} 不是 Window 类型", nameof(windowType));

            var window = (Window)ActivatorUtilities.CreateInstance(_serviceProvider, windowType);
            TrySetViewModel(window, windowType);
            return window;
        }

        public Window CreateWindow(Type windowType, params object[] args)
        {
            if (!typeof(Window).IsAssignableFrom(windowType))
                throw new ArgumentException($"类型 {windowType.Name} 不是 Window 类型", nameof(windowType));

            var window = (Window)ActivatorUtilities.CreateInstance(_serviceProvider, windowType, args);
            TrySetViewModel(window, windowType);
            return window;
        }

        public T CreateWindow<T>() where T : Window
        {
            var window = ActivatorUtilities.CreateInstance<T>(_serviceProvider);
            TrySetViewModel(window, typeof(T));
            return window;
        }

        public T CreateWindow<T>(params object[] args) where T : Window
        {
            var window = ActivatorUtilities.CreateInstance<T>(_serviceProvider, args);
            TrySetViewModel(window, typeof(T));
            return window;
        }

        private void TrySetViewModel(Window window, Type windowType)
        {
        }
    }
}
