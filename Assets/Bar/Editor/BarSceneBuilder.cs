using System;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Unity.Input;
using CraftingResearch.CoreV2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bar.Unity.Editor
{
    /// <summary>以真实米制尺寸生成可重复构建的灰盒场景及基础材质。</summary>
    public static class BarSceneBuilder
    {
        private const string ScenePath="Assets/Bar/Scenes/Bar.unity";
        /// <summary>构建并保存灰盒场景。</summary>
        /// <precondition>核心数据、items.json、controls.json 均可加载；只允许在指定验收副本或用户主动执行的 Editor 内调用。</precondition>
        /// <effects>新建场景、生成米制基本体及物品绑定、设置输入和双手锚点，保存 Bar.unity 并写入构建场景列表。</effects>
        [MenuItem("Bar/Build Graybox Scene")]
        public static void Build()
        {
            if (File.Exists("Assets/Bar/Prefabs/BarKit-installed.txt"))
                throw new InvalidOperationException("BarKit 已接入，为保护用户美术调整，禁止灰盒生成器覆盖正式资源。");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Directory.CreateDirectory("Assets/Bar/Art/Graybox");
            var model=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json");var items=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json");var controls=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/controls.json");
            var catalog=ItemCatalog.Load(items.text,JsonUtility.FromJson<ModelData>(model.text));
            Material Material(string name,Color color,float metallic=0)
            {
                string path="Assets/Bar/Art/Graybox/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}material.color=color;material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",.4f);return material;
            }
            var wood=Material("WarmWood",new Color(.23f,.12f,.075f));var stone=Material("Charcoal",new Color(.055f,.075f,.08f));var steel=Material("Steel",new Color(.58f,.65f,.68f),.75f);var glass=Material("GlassProxy",new Color(.27f,.49f,.55f),.15f);var liquid=Material("LiquidProxy",new Color(.58f,.83f,.68f));
            glass.SetFloat("_Surface",1);glass.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);glass.SetFloat("_ZWrite",0);glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.renderQueue=3000;glass.color=new Color(.27f,.49f,.55f,.22f);
            GameObject Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,Transform parent=null)
            {var go=GameObject.CreatePrimitive(type);go.name=name;if(parent)go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
            Shape("Floor",PrimitiveType.Cube,new Vector3(0,-.06f,.4f),new Vector3(6,.1f,5),stone);
            Shape("Back wall",PrimitiveType.Cube,new Vector3(0,1.6f,2.5f),new Vector3(6,3.2f,.1f),stone);
            Shape("Back shelf",PrimitiveType.Cube,new Vector3(0,1.35f,2.2f),new Vector3(3,.045f,.28f),wood);
            var counterDef=catalog["fixture.counter"];var counterGo=new GameObject("吧台 / Counter");counterGo.transform.position=new Vector3(0,0,.65f);var counter=counterGo.AddComponent<ItemView>();counter.DefinitionId=counterDef.Id;counter.InstanceId="counter";counter.Size=new Vector3(2,1.1f,.65f);
            Shape("Visual",PrimitiveType.Cube,new Vector3(0,.55f,0),counter.Size,wood,counterGo.transform);var cc=counterGo.AddComponent<BoxCollider>();cc.center=new Vector3(0,.55f,0);cc.size=counter.Size;
            // Root dimensions follow items.json; child shapes remain plain greybox proxies.
            var placeable=catalog.Items.Where(d=>d.Kind!=ItemKind.Container||d.Type=="Glass").Where(d=>d.Kind!=ItemKind.Fixture&&d.Type!="PourSpout").OrderBy(d=>d.Id).ToList();
            int index=0;
            foreach(var d in placeable)
            {
                int col=index%8,row=index/8;index++;var go=new GameObject(d.DisplayName);go.transform.position=new Vector3(-.84f+col*.235f,1.102f,.83f-row*.20f);
                var view=go.AddComponent<ItemView>();view.DefinitionId=d.Id;view.InstanceId="item:"+d.Id;view.Size=new Vector3((float)d.Size.WidthCm,(float)d.Size.HeightCm,(float)d.Size.DepthCm)*.01f;
                var size=view.Size;var material=d.Type=="LemonHalf"?Material("Lemon",new Color(.84f,.72f,.17f)):d.Kind==ItemKind.Bottle?Material("Bottle-"+d.Id,d.Id=="bottle.gin"?new Color(.07f,.38f,.62f):d.Id=="bottle.syrup"?new Color(.53f,.30f,.09f):new Color(.18f,.34f,.20f)):d.Type=="Glass"?glass:steel;
                if(d.Type=="Jigger")
                {Shape("Visual",PrimitiveType.Cylinder,new Vector3(0,size.y/2,0),new Vector3(size.x,size.y/2,size.z),glass,go.transform);for(int j=1;j<=3;j++)Shape("Marking"+j,PrimitiveType.Cylinder,new Vector3(0,size.y*(.55f+j*.11f),0),new Vector3(size.x*1.02f,.0007f,size.z*1.02f),stone,go.transform);}
                else if(d.Type=="CobblerShaker")
                {Shape("Visual",PrimitiveType.Cylinder,new Vector3(0,size.y*.35f,0),new Vector3(size.x,size.y*.35f,size.z),glass,go.transform);Shape("StrainerVisual",PrimitiveType.Cylinder,new Vector3(0,size.y*.77f,0),new Vector3(size.x*.95f,size.y*.075f,size.z*.95f),material,go.transform);Shape("CapVisual",PrimitiveType.Cylinder,new Vector3(0,size.y*.92f,0),new Vector3(size.x*.45f,size.y*.08f,size.z*.45f),material,go.transform);}
                else if(d.Type=="Glass"&&d.Id=="glass.coupe")
                {Shape("Visual",PrimitiveType.Cylinder,new Vector3(0,size.y*.8f,0),new Vector3(size.x,size.y*.2f,size.z),material,go.transform);Shape("Stem",PrimitiveType.Cylinder,new Vector3(0,size.y*.32f,0),new Vector3(.012f,size.y*.3f,.012f),material,go.transform);Shape("Foot",PrimitiveType.Cylinder,new Vector3(0,.005f,0),new Vector3(size.x*.65f,.005f,size.z*.65f),material,go.transform);}
                else Shape("Visual",d.Kind==ItemKind.Bottle?PrimitiveType.Cube:PrimitiveType.Cylinder,new Vector3(0,size.y/2,0),new Vector3(size.x,d.Kind==ItemKind.Bottle?size.y:size.y/2,size.z),material,go.transform);
                if(d.Kind==ItemKind.Bottle)Shape("Neck",PrimitiveType.Cylinder,new Vector3(0,size.y*.9f,0),new Vector3(size.x*.38f,size.y*.1f,size.z*.38f),material,go.transform);
                var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,size.y/2,0);collider.size=size;
                if(d.Kind==ItemKind.Composite||d.Type=="Glass"){var l=Shape("LiquidVolume",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.01f,.001f,.01f),liquid,go.transform);l.SetActive(false);}
            }
            // Fixed facilities use compact proxies on a side station, preserving table space.
            int fi=0;foreach(var d in catalog.Items.Where(d=>d.Kind==ItemKind.Fixture&&d.Id!="fixture.counter"))
            {var go=new GameObject(d.DisplayName);go.transform.position=new Vector3(1.4f,1.102f,.1f+fi++*.45f);var v=go.AddComponent<ItemView>();v.DefinitionId=d.Id;v.InstanceId="item:"+d.Id;v.Size=new Vector3((float)d.Size.WidthCm,(float)d.Size.HeightCm,(float)d.Size.DepthCm)*.01f;Shape("Visual",PrimitiveType.Cube,new Vector3(0,v.Size.y/2,0),v.Size,steel,go.transform);var col=go.AddComponent<BoxCollider>();col.size=v.Size;col.center=new Vector3(0,v.Size.y/2,0);}
            Shape("Side worktop",PrimitiveType.Cube,new Vector3(1.55f,1.05f,.7f),new Vector3(.65f,.1f,2.2f),wood);
            var playerGo=new GameObject("Player");playerGo.transform.position=new Vector3(0,0,-.62f);var player=playerGo.AddComponent<PlayerController>();
            var cameraGo=new GameObject("First person camera");cameraGo.tag="MainCamera";cameraGo.transform.SetParent(playerGo.transform,false);cameraGo.transform.localPosition=new Vector3(0,1.65f,0);var camera=cameraGo.AddComponent<Camera>();camera.fieldOfView=62;camera.nearClipPlane=.025f;camera.farClipPlane=30;camera.backgroundColor=new Color(.025f,.04f,.045f);camera.clearFlags=CameraClearFlags.SolidColor;cameraGo.AddComponent<AudioListener>();player.ViewCamera=camera;
            var torso=new GameObject("Torso").transform;torso.SetParent(playerGo.transform,false);player.Torso=torso;
            Transform Anchor(string name,Vector3 position){var t=new GameObject(name).transform;t.SetParent(cameraGo.transform,false);t.localPosition=position;return t;}
            player.LeftAnchor=Anchor("Left hand",new Vector3(-.19f,-.27f,.55f));player.RightAnchor=Anchor("Right hand",new Vector3(.20f,-.27f,.55f));
            var session=playerGo.AddComponent<BarSession>();session.ModelJson=model;session.ItemsJson=items;session.ControlsJson=controls;session.Player=player;var input=playerGo.AddComponent<InputRouter>();input.Session=session;
            var lightGo=new GameObject("Key light");var key=lightGo.AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.5f;key.color=new Color(1,.88f,.72f);lightGo.transform.rotation=Quaternion.Euler(45,-35,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.38f,.43f,.46f);
            EditorSceneManager.SaveScene(scene,ScenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};AssetDatabase.SaveAssets();
        }
    }
}
