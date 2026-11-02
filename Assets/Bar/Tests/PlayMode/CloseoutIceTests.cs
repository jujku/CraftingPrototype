using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>临时棚拍与四个吧台机位，同帧切换历史光学材质，屏幕掩码测量亮度及全反射。</summary>
    public sealed class CloseoutIceTests
    {
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../CloseoutReports"));
        [Serializable]public sealed class Row{public string view,version;public int pixels;public float iceLuminance,backgroundLuminance,ratio,tirFraction;}
        [Serializable]public sealed class Rows{public Row[] rows;}
        private readonly List<Row> rows=new List<Row>();
        private Texture2D Capture(Camera camera,string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Output,path+".png")));
            var rt=RenderTexture.GetTemporary(640,480,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(640,480,TextureFormat.RGB24,false);
            t.ReadPixels(new Rect(0,0,640,480),0,0);t.Apply();RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(Output,path+".png"),t.EncodeToPNG());return t;
        }
        private void Compare(Camera camera,string view,MeshRenderer[] surfaces)
        {
            var all=surfaces.SelectMany(r=>r.transform.parent.GetComponentsInChildren<MeshRenderer>(true)).Distinct().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Ice").ToArray();
            var original=all.Select(r=>r.sharedMaterial).ToArray();var enabled=all.Select(r=>r.enabled).ToArray();
            var data=camera.GetUniversalAdditionalCameraData();bool post=data.renderPostProcessing;
            foreach(string version in new[]{"pre-P4","P4","current"})
            {
                IceRefractionFeature.CaptureHistoricalOrder=version=="P4";
                var temp=new List<Material>();
                for(int i=0;i<all.Length;i++)
                {
                    var m=new Material(original[i]);temp.Add(m);
                    if(version=="pre-P4") {m.shader=Shader.Find("Hidden/Bar/CloseoutLegacyIce");m.SetFloat("_FresnelPower",3);m.SetFloat("_EdgeWidth",.25f);all[i].enabled=true;}
                    else if(version=="P4"){m.shader=Shader.Find("Hidden/Bar/CloseoutP4Ice");m.SetFloat("_FresnelPower",3);}
                    else m.SetFloat("_FresnelPower",5);
                    all[i].sharedMaterial=m;
                }
                Shader.SetGlobalFloat("_BarIceDebug",0);var rendered=Capture(camera,view+"/"+version);
                data.renderPostProcessing=false;Shader.SetGlobalFloat("_BarIceDebug",5);var mask=Capture(camera,view+"/"+version+"-mask");
                Shader.SetGlobalFloat("_BarIceDebug",2);var tir=Capture(camera,view+"/"+version+"-tir");Shader.SetGlobalFloat("_BarIceDebug",0);data.renderPostProcessing=post;
                if(version=="current"){data.renderPostProcessing=false;Shader.SetGlobalFloat("_BarIceDebug",6);Object.Destroy(Capture(camera,view+"/current-exit-path"));Shader.SetGlobalFloat("_BarIceDebug",7);Object.Destroy(Capture(camera,view+"/current-opaque-input"));Shader.SetGlobalFloat("_BarIceDebug",0);data.renderPostProcessing=post;}
                foreach(var r in all)r.enabled=false;var background=Capture(camera,view+"/"+version+"-background");
                float light=0,back=0;int count=0,total=0;
                var mp=mask.GetPixels();var rp=rendered.GetPixels();var bp=background.GetPixels();var tp=tir.GetPixels();
                for(int i=0;i<mp.Length;i++)if(mp[i].r>.8f&&mp[i].b>.8f&&mp[i].g<.15f)
                {count++;light+=Luminance(rp[i]);back+=Luminance(bp[i]);if(tp[i].r>.8f&&tp[i].g<.15f)total++;}
                Assert.That(count,Is.GreaterThan(30),view+" requires visible ice mask");
                rows.Add(new Row{view=view,version=version,pixels=count,iceLuminance=light/count,backgroundLuminance=back/count,ratio=light/Mathf.Max(back,.00001f),tirFraction=version=="pre-P4"?-1:total/(float)count});
                if(view=="studio"&&version=="current")Assert.That(light/Mathf.Max(back,.00001f),Is.GreaterThan(.5f),"白色棚拍背景不能被无效的黑色 opaque 输入代替");
                for(int i=0;i<all.Length;i++){all[i].sharedMaterial=original[i];all[i].enabled=enabled[i];}foreach(var m in temp)Object.Destroy(m);
                Object.Destroy(rendered);Object.Destroy(mask);Object.Destroy(tir);Object.Destroy(background);
            }
            if(view=="ice-table")
            {
                // Diagnose the unmodified P4 implementation before judging the new optical path.
                var temp=surfaces.Select(r=>new Material(r.sharedMaterial){shader=Shader.Find("Hidden/Bar/CloseoutP4Ice")}).ToArray();
                IceRefractionFeature.CaptureHistoricalOrder=true;
                var saved=surfaces.Select(r=>r.sharedMaterial).ToArray();for(int i=0;i<surfaces.Length;i++)surfaces[i].sharedMaterial=temp[i];
                foreach(var pair in new[]{(1,"refraction-only"),(2,"tir-regions"),(3,"absorption-only"),(4,"core-only")})
                {Shader.SetGlobalFloat("_BarIceDebug",pair.Item1);Object.Destroy(Capture(camera,"diagnostic/"+pair.Item2));}
                Shader.SetGlobalFloat("_BarIceDebug",0);for(int i=0;i<surfaces.Length;i++){surfaces[i].sharedMaterial=saved[i];Object.Destroy(temp[i]);}
            }
            IceRefractionFeature.CaptureHistoricalOrder=false;
        }
        private static float Luminance(Color c){c=c.linear;return .2126f*c.r+.7152f*c.g+.0722f*c.b;}
        private static MeshRenderer[] Ice(GameObject go)=>go.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Ice").ToArray();
        [UnityTest]public IEnumerator FiveViewsWithThreeVersionsAndDiagnostics()
        {
            try
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
                var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
                Object.FindFirstObjectByType<PresentationRig>().Desktop();
                var camera=session.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=640f/480;
                var board=session.Views.First(v=>v.DefinitionId=="fixture.board");var group=new GameObject("Closeout table ice");
                for(int i=0;i<3;i++){var c=IceAppearance.Create(i,group.transform);c.transform.localScale=Vector3.one*.037f;c.transform.localPosition=new Vector3((i-1)*.045f,.0185f,0);c.transform.localRotation=Quaternion.Euler(0,i*23,0);}
                group.transform.position=board.transform.position+new Vector3(0,board.Size.y+.001f,-.06f);
                camera.transform.position=group.transform.position+new Vector3(-.05f,.12f,-.2f);camera.transform.LookAt(group.transform.position+Vector3.up*.012f);
                yield return null;Compare(camera,"ice-table",Ice(group));group.SetActive(false);
                foreach(string id in new[]{"glass.highball","shaker.cobbler"})
                {
                    var view=session.Views.First(v=>v.DefinitionId==id);Container c=view.Item is Glass g?g:((CobblerShaker)view.Item).Body;
                    if(view.Item is CobblerShaker sh)
                    {
                        // Remove both physical lid views for the required open-body view, without changing authored assets.
                        foreach(var part in new Attachment[]{sh.CapSlot.Content,sh.StrainerSlot.Content})if(part!=null){sh.Detach(part);var partView=session.Views.FirstOrDefault(v=>v.Item==part);if(partView)partView.gameObject.SetActive(false);}
                    }
                    session.Adapter.World.Apply(new Operation{type="PourSource",to=c.CoreVesselId,ingredient="spirit",ml=70});session.Adapter.World.Apply(new Operation{type="AddIce",to=c.CoreVesselId,grams=80});session.Adapter.Advance(0);
                    camera.transform.position=view.transform.position+new Vector3(-.04f,.21f,-.18f);camera.transform.LookAt(view.transform.position+Vector3.up*view.Size.y*.42f);
                    if(view.Item is CobblerShaker){camera.transform.position=view.transform.position+new Vector3(-.025f,.34f,-.07f);camera.transform.LookAt(view.transform.position+Vector3.up*.08f);}
                    yield return new WaitForSeconds(.6f);foreach(var rig in view.GetComponentsInChildren<LocalIceWorld>())rig.enabled=false;
                    Compare(camera,id,Ice(view.gameObject));
                }
                var tongs=session.Views.First(v=>v.Item is IceTongs);typeof(Tool).GetProperty("LoadedCubes").SetValue(tongs.Item,1);
                camera.transform.position=tongs.transform.position+new Vector3(-.02f,.13f,-.18f);camera.transform.LookAt(tongs.Anchor("HoldPoint").position);
                yield return new WaitForSeconds(.5f);Compare(camera,"ice-tongs",Ice(tongs.gameObject));
                // Isolated studio set. No saved scene, no modifications to bar lighting or materials.
                var studio=new Vector3(12,2,12);var table=GameObject.CreatePrimitive(PrimitiveType.Cube);table.transform.position=studio-new Vector3(0,.025f,0);table.transform.localScale=new Vector3(.65f,.05f,.5f);
                var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.92f,.94f,.94f);table.GetComponent<Renderer>().sharedMaterial=mat;
                var back=GameObject.CreatePrimitive(PrimitiveType.Cube);back.transform.position=studio+new Vector3(0,.35f,.5f);back.transform.localScale=new Vector3(2,1,.03f);var gray=new Material(mat);gray.color=new Color(.46f,.5f,.5f);back.GetComponent<Renderer>().sharedMaterial=gray;
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))light.enabled=false;
                var soft=new GameObject("Studio soft light").AddComponent<Light>();soft.type=LightType.Directional;soft.color=Color.white;soft.intensity=1.4f;soft.transform.rotation=Quaternion.Euler(45,-30,0);soft.shadows=LightShadows.Soft;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.65f,.67f);
                group.SetActive(true);group.transform.position=studio+Vector3.up*.001f;
                camera.fieldOfView=35;camera.transform.position=studio+new Vector3(-.14f,.21f,-.28f);camera.transform.LookAt(studio+Vector3.up*.013f);
                yield return null;Compare(camera,"studio",Ice(group));
                Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"ice-metrics.json"),JsonUtility.ToJson(new Rows{rows=rows.ToArray()},true));
            }
            finally{Shader.SetGlobalFloat("_BarIceDebug",0);IceRefractionFeature.CaptureHistoricalOrder=false;}
        }
    }
}
