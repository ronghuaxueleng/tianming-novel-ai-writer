using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Account.AccountBinding
{
    public class AccountBindingSettings : BaseSettings<AccountBindingSettings, BindingsData>
    {
        public AccountBindingSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Account/AccountBinding", "bindings.json");

        protected override BindingsData CreateDefaultData() => _objectFactory.Create<BindingsData>();

        public BindingsData LoadBindings() => Data;
        public void SaveBindings(BindingsData data) { Data = data; SaveData(); }
    }
}
