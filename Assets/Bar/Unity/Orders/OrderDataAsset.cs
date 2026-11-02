using UnityEngine;
namespace Bar.Unity
{
    /// <summary>资源入口只引用唯一的 Data/Json/orders.json，不复制订单数据。</summary>
    public sealed class OrderDataAsset:ScriptableObject {public TextAsset Json;}
}
