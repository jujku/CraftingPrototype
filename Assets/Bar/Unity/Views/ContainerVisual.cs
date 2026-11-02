using UnityEngine;
namespace Bar.Unity
{
    /// <summary>将某个容器部件的内腔、烘焙曲线、液体网格和刻度锚点绑定在一起。</summary>
    public sealed class ContainerVisual : MonoBehaviour
    {
        /// <summary>对应 items.json 的容器定义；复合量酒器两端分别填写。</summary>
        public string DefinitionId;
        /// <summary>封闭且可读的内腔参考网格，不作为外观绘制。</summary>
        public MeshFilter InnerCavity;
        /// <summary>离线烘焙得到的体积高度曲线。</summary>
        public VolumeProfile Profile;
        /// <summary>额定容量是可用液量而非满口几何容量；仅用户指定留出冰块/杯口余量的新玻璃杯启用，核心容量不变。</summary>
        public bool NominalCapacityIsWorkingFill;
        /// <summary>填充液体的网格节点。</summary>
        public MeshRenderer LiquidVolume;
        private CavityShape shape;private LocalIceWorld ice;
        /// <summary>由 InnerCavity 切片得到的内腔形状（本节点局部空间），首次读取时计算并缓存。</summary>
        public CavityShape Shape => shape ??= CavityShape.FromCavity(InnerCavity, transform);
        /// <summary>实际液量对应的显示高度；只增加当前确实浸没的显示冰排水体积。</summary>
        public float HeightForLiquid(double liquidMl)
        {if(!ice)ice=GetComponent<LocalIceWorld>();return ice?ice.LiquidHeight(liquidMl):Profile.HeightForMl(liquidMl);}
    }
}
