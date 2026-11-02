using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读瓶库存的表现层；复用 VolumeProfile、LiquidFill 与水平 LiquidSurface，不注册核心容器。</summary>
    [DefaultExecutionOrder(90)]public sealed class BottleLiquidView : MonoBehaviour
    {
        public BottleGeometryData Geometry {get;private set;}
        public VolumeProfile Profile {get;private set;}
        public MeshFilter Cavity {get;private set;}
        private CavityShape shape;
        public CavityShape Shape=>shape??=CavityShape.FromCavity(Cavity,transform);
        public float Level {get;private set;}
        public Vector3 SurfaceNormal=>Vector3.up;
        private Bottle bottle;private MeshRenderer volume;private LiquidSurface surface;private Material material,surfaceMaterial;private Mesh mesh;
        private double previousMl=double.NaN;private Matrix4x4 matrix;private LiquidMotion opticalMotion=new LiquidMotion();private CraftingResearch.CoreV2.VesselState opticalState;private VisualSettings visual;private BubbleStreams bubbles;private float carbonation;
        public void Initialize(Bottle source,double[] optical,float carbonation=0)
        {
            bottle=source;this.carbonation=carbonation;Geometry=BottleGeometryData.Load(bottle.Definition.Id);if(Geometry==null)return;
            Profile=Geometry.CreateProfile();mesh=Geometry.CreateMesh(PourSettings.Load().bottleSides);
            var node=new GameObject("Bottle inner cavity (generated)");node.layer=31;node.transform.SetParent(transform,false);Cavity=node.AddComponent<MeshFilter>();Cavity.sharedMesh=mesh;
            volume=node.AddComponent<MeshRenderer>();material=new Material(Shader.Find("Bar/LiquidFill")){renderQueue=2498};
            var c=optical==null?new Color(.85f,.94f,1):new Color(Mathf.Exp(-(float)optical[0]*3),Mathf.Exp(-(float)optical[1]*3),Mathf.Exp(-(float)optical[2]*3));
            material.SetColor("_BaseColor",c);volume.sharedMaterial=material;surface=node.AddComponent<LiquidSurface>();
            surfaceMaterial=new Material(Shader.Find("Bar/BottleLiquidSurface"));surfaceMaterial.SetFloat("_Strength",PourSettings.Load().bottleSurfaceHighlight);
            opticalState=new CraftingResearch.CoreV2.VesselState{optical=optical};visual=JsonUtility.FromJson<CueSettings>(Resources.Load<CueSettingsAsset>("BarCueSettings").Json.text).visual;
            if(carbonation>0){bubbles=gameObject.AddComponent<BubbleStreams>();bubbles.Initialize(transform,Shape);}
            gameObject.AddComponent<BottleGlassOptics>().Initialize(this);
        }
        private void LateUpdate()
        {
            if(Geometry==null)return;
            bool visible=bottle.RemainingMl>1e-8;volume.enabled=visible;
            if(previousMl!=bottle.RemainingMl||matrix!=transform.localToWorldMatrix){Level=BottleGeometryMath.PlaneForMl(Geometry,transform,bottle.RemainingMl);previousMl=bottle.RemainingMl;matrix=transform.localToWorldMatrix;}
            opticalMotion.Apply(material,transform,opticalState,visual,Time.unscaledDeltaTime,0);
            material.SetVector("_PlaneNormal",Vector3.up);material.SetFloat("_FillHeight",Level);surface.Draw(Cavity,Level,surfaceMaterial,visible);
            if(bubbles)bubbles.Present(carbonation,(float)bottle.ServeTemperatureC,Level,Vector3.up,0,visible,Time.unscaledDeltaTime);
            if(surface.Renderer)surface.Renderer.gameObject.layer=0;
        }
        private void OnDestroy(){if(material)Destroy(material);if(surfaceMaterial)Destroy(surfaceMaterial);if(mesh)Destroy(mesh);if(Profile)Destroy(Profile);}
    }
}
