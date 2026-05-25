using System;
using System.Reflection;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
[Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
public partial class ModelManagementViewModel : DataManagementViewModelBase<UserConfigurationData, AIProviderCategory, ModelService>, IAIGeneratingState, IDisposable
{
}
