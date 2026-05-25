using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Account.LoginHistory
{
    public class LoginHistorySettings : BaseSettings<LoginHistorySettings, LoginHistoryData>
    {
        public LoginHistorySettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Account/LoginHistory", "login_history.json");

        protected override LoginHistoryData CreateDefaultData() => _objectFactory.Create<LoginHistoryData>();

        public LoginHistoryData LoadRecords() => Data;
        public void SaveRecords(LoginHistoryData data) { Data = data; SaveData(); }
    }
}
