using System.Runtime.Serialization;
namespace Bar.Logging
{
    /// <summary>订单逐维判定；只用于日志，数值不显示给玩家。</summary>
    [DataContract] public sealed class OrderDimension
    {
        [DataMember] public string dimension,direction,severity,feedbackId;
        [DataMember] public bool inside;
        [DataMember] public double normalizedDeviation;
        [DataMember(EmitDefaultValue=false)] public double? value;
    }
}
