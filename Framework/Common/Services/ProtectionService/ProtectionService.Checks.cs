using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public static partial class ProtectionService
    {
        #region C1

        private static bool CheckDebugger() => false;

        #endregion

        #region C4

        private static bool CheckIntegrity() => false;

        private static Task<bool> CheckIntegrityAsync() => Task.FromResult(false);

        #endregion
    }
}
