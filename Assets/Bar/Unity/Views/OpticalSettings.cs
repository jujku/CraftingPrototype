using System;
using CraftingResearch.CoreV2;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>把用户可调的 sRGB 透射色与浑浊度转成核心被动系数；不调整物理参数。</summary>
    [Serializable] public sealed class OpticalSettings
    {
        [Serializable] public sealed class Entry {public string id;public double[] transmissionSrgb;public double turbidity;}
        public Entry[] ingredients;public double referencePathCm=5;
        public static void Apply(ModelData model,string json)
        {
            var data=JsonUtility.FromJson<OpticalSettings>(json);
            foreach(var input in data.ingredients)foreach(var item in model.ingredients)if(item.id==input.id)
            {var a=new double[7];a[3]=input.turbidity;for(int i=0;i<3;i++){double c=input.transmissionSrgb[i];double linear=c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);a[i]=-Math.Log(Math.Max(.001,linear))/data.referencePathCm;a[4+i]=input.turbidity*linear;}item.optical=a;}
        }
    }
}
