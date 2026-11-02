using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>保存由封闭内腔网格烘焙的累计体积和局部液面高度，单位 ml、m。</summary>
    [CreateAssetMenu(menuName="Bar/Volume Profile")]
    public sealed class VolumeProfile : ScriptableObject
    {
        /// <summary>自下而上的高度采样，单位米。</summary>
        public float[] Heights = Array.Empty<float>();
        /// <summary>对应高度以下的累计毫升体积。</summary>
        public double[] VolumesMl = Array.Empty<double>();
        /// <summary>内腔总容量，单位毫升。</summary>
        public double CapacityMl => VolumesMl.Length==0?0:VolumesMl[VolumesMl.Length-1];
        /// <summary>累计曲线的反向插值，供只读的浸没冰排水计算使用。</summary>
        public double VolumeBelowHeight(float height)
        {
            if(height<=Heights[0])return VolumesMl[0];if(height>=Heights[Heights.Length-1])return CapacityMl;
            int hi=Array.BinarySearch(Heights,height);if(hi>=0)return VolumesMl[hi];hi=~hi;int lo=hi-1;
            return VolumesMl[lo]+(VolumesMl[hi]-VolumesMl[lo])*((height-Heights[lo])/(Heights[hi]-Heights[lo]));
        }
        /// <summary>根据毫升值插值得到局部液面高度。</summary>
        /// <precondition>样本长度相同且体积单调；输入为有限非负 ml。</precondition>
        /// <effects>限到曲线端点并插值，只读资源，不修改核心体积。</effects>
        public float HeightForMl(double ml)
        {
            if(double.IsNaN(ml)||double.IsInfinity(ml)||ml<0||Heights.Length<2||Heights.Length!=VolumesMl.Length)throw new ArgumentException("液面曲线或输入无效");
            if(ml<=0)return Heights[0];if(ml>=CapacityMl)return Heights[Heights.Length-1];
            int hi=Array.BinarySearch(VolumesMl,ml);if(hi>=0)return Heights[hi];hi=~hi;int lo=hi-1;
            return Mathf.Lerp(Heights[lo],Heights[hi],(float)((ml-VolumesMl[lo])/(VolumesMl[hi]-VolumesMl[lo])));
        }
    }
}
