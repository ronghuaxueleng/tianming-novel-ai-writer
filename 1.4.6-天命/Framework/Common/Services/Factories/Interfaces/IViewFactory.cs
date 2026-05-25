using System;
using System.Windows.Controls;

namespace TM.Framework.Common.Services.Factories
{
    public interface IViewFactory
    {
        UserControl CreateView(Type viewType);

        UserControl CreateView(Type viewType, params object[] args);

        T CreateView<T>() where T : UserControl;

        T CreateView<T>(params object[] args) where T : UserControl;
    }
}
