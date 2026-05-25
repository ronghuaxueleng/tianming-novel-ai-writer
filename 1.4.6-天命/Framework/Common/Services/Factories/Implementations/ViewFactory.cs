using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace TM.Framework.Common.Services.Factories
{
    public class ViewFactory : IViewFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ViewFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public UserControl CreateView(Type viewType)
        {
            if (!typeof(UserControl).IsAssignableFrom(viewType))
                throw new ArgumentException($"类型 {viewType.Name} 不是 UserControl 类型", nameof(viewType));

            var view = (UserControl)ActivatorUtilities.CreateInstance(_serviceProvider, viewType);
            TrySetViewModel(view, viewType);
            return view;
        }

        public UserControl CreateView(Type viewType, params object[] args)
        {
            if (!typeof(UserControl).IsAssignableFrom(viewType))
                throw new ArgumentException($"类型 {viewType.Name} 不是 UserControl 类型", nameof(viewType));

            var view = (UserControl)ActivatorUtilities.CreateInstance(_serviceProvider, viewType, args);
            TrySetViewModel(view, viewType);
            return view;
        }

        public T CreateView<T>() where T : UserControl
        {
            var view = ActivatorUtilities.CreateInstance<T>(_serviceProvider);
            TrySetViewModel(view, typeof(T));
            return view;
        }

        public T CreateView<T>(params object[] args) where T : UserControl
        {
            var view = ActivatorUtilities.CreateInstance<T>(_serviceProvider, args);
            TrySetViewModel(view, typeof(T));
            return view;
        }

        private void TrySetViewModel(UserControl view, Type viewType)
        {
        }
    }
}
