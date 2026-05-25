using System;
using System.Windows;

namespace TM.Framework.Common.Services.Factories
{
    public interface IWindowFactory
    {
        Window CreateWindow(Type windowType);

        Window CreateWindow(Type windowType, params object[] args);

        T CreateWindow<T>() where T : Window;

        T CreateWindow<T>(params object[] args) where T : Window;
    }
}
