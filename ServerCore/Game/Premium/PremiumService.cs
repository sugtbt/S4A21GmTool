using System.Threading.Tasks;

namespace DfoGmTool.ServerCore.Game.Premium
{
    // GM瘦身拷贝: 只保留 GM 调用图可达的成员(保留成员逐字一致, 命名空间重写除外)。
    // 删除: ActivateAndNotify 等依赖 EnhancedClientSession/Network.Builders/ISelectCharacterDataSource 的在线通知成员。
    public static class PremiumService
    {
        public static bool IsContractItem(int itemTemplateId)
        {
            return PremiumCatalog.Load().TryGetValue(itemTemplateId, out var pt, out var dd)
                && pt > 0 && dd > 0;
        }
    }
}
