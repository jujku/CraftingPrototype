using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>可调表现初值，全部人为设定、未标定；不参与 Core。</summary>
    [Serializable]public sealed class PresentationSettings
    {
        public string note;public float environmentExposure=.7f,environmentIntensity=1,reflectionIntensity=1;
        public PostPreset post=new PostPreset();public QualityPreset desktop=new QualityPreset(),web=new QualityPreset();
        public LightPreset[] lights;public GlassPreset glass=new GlassPreset();public IcePreset ice=new IcePreset();public SoftenPreset soften=new SoftenPreset();
        public static PresentationSettings Load()=>JsonUtility.FromJson<PresentationSettings>(Resources.Load<TextAsset>("BarPresentationSettings").text);
    }
    [Serializable]public sealed class PostPreset{public string tonemapping="ACES";public float bloomThreshold=1.1f,bloomIntensity=.3f,exposure=-.2f,contrast=8,saturation=-4;}
    [Serializable]public sealed class QualityPreset{public string name;public float renderScale=1,shadowDistance=12;public int msaa=4,reflectionResolution=256,shadowResolution=2048;public bool ssao;}
    [Serializable]public sealed class LightPreset{public string label;public Vector3 position,target;public Color color;public float intensity,range=5,angle=105;public bool shadows;}
    [Serializable]public sealed class GlassPreset{public float refraction=.012f,thicknessMm=2.7f,tintStrength=.75f,fresnelPower=5,smoothness=.96f,specularPower=160;}
    /// <summary>
    /// 2026-10-28 试玩反馈后加的运行时柔光：灯光乘 lightScale，聚光灯内锥取外锥的 innerSpotRatio（≤0 不改，越小边缘越软），
    /// 反射探针和天空反射乘 reflectionScale，指定渲染器的材质副本光滑度不超过 cap（item 为空表示不限物品）。
    /// </summary>
    [Serializable]public sealed class SoftenPreset{public string note;public float lightScale=1,innerSpotRatio,reflectionScale=1;public bool jiggerUseShakerSteel=true;public Color jiggerSteelColor=new Color(.55f,.59f,.62f,1);public float jiggerMetallic=.75f,jiggerSmoothness=.8f,jiggerFinishScale=.96f,jiggerInnerFinishScale=.95f,jiggerHighlightKnee=.18f,jiggerHighlightLimit=.45f;public SmoothnessCap[] smoothnessCaps;}
    [Serializable]public sealed class SmoothnessCap{public string renderer,item;public float cap=1;}
    [Serializable]public sealed class IcePreset{public float alpha=.3f,coreAlpha=.12f,fog=.8f,fresnelPower=4,smoothness=.94f,normalNoise=.018f,specularPower=128;}
}
