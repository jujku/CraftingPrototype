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
    /// <summary>真实玻璃合成像素，以及快速扶正时确认尾流和显示姿态的错时回归。</summary>
    public sealed class PourGlassReturnTests
    {
        private BarSession session;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GlassReturnReports"));
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var image=PourCaptureTests.Capture(camera,"glass-return-"+name);
            File.Copy(Path.Combine(PourCaptureTests.Output,"glass-return-"+name+".png"),Path.Combine(Output,name+".png"),true);return image;
        }
        private static int ChangedNearStream(Camera camera,Texture2D with,Texture2D without,Vector3 top,Vector3 bottom)
        {
            var a=camera.WorldToViewportPoint(top);var b=camera.WorldToViewportPoint(bottom);
            int x0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,b.x)*with.width)-14,0,with.width-1),x1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,b.x)*with.width)+14,0,with.width-1);
            int y0=Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(a.y,b.y)*with.height),0,with.height-1),y1=Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.y,b.y)*with.height),0,with.height-1);
            int count=0;for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {var p=with.GetPixel(x,y);var q=without.GetPixel(x,y);if(Mathf.Max(Mathf.Abs(p.r-q.r),Mathf.Max(Mathf.Abs(p.g-q.g),Mathf.Abs(p.b-q.b)))>.025f)count++;}
            return count;
        }
        [UnityTest]public IEnumerator StreamsReachBothGlassLiquidSurfacesIncludingFallback()
        {
            yield return Load();var errors=new List<string>();var rows=new List<string>{"glass,feature,insideChangedPixels,aboveChangedPixels"};
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",BindingFlags.NonPublic|BindingFlags.Instance);
            var renderers=(ScriptableRendererData[])field.GetValue(pipeline);var features=renderers.SelectMany(r=>r.rendererFeatures).OfType<IceRefractionFeature>().ToArray();
            Assert.That(features.Length,Is.GreaterThan(0));
            try
            {
                foreach(string id in new[]{"glass.highball","glass.coupe"})
                {
                    var target=session.Views.First(v=>v.DefinitionId==id);var container=(Glass)target.Item;
                    session.Adapter.World.Apply(new Operation{type="PourSource",to=container.CoreVesselId,ingredient="spirit",ml=15});session.Adapter.Advance(0);
                    var camera=session.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=640f/480;
                    PourPose.Mouth(target,container,out var mouth,out float radius,out float plane);
                    camera.transform.position=mouth+new Vector3(.025f,.10f,-.32f);camera.transform.LookAt(Vector3.Lerp(mouth,new Vector3(mouth.x,plane,mouth.z),.45f));
                    var stream=session.gameObject.AddComponent<PourRibbon>();stream.Tint=new Color(.03f,.9f,1);
                    var input=new PourStreamInput{Outlet=new Pose(mouth+Vector3.up*.06f,Quaternion.identity),ExitVelocity=Vector3.down*.15f,ConfirmedMlPerSecond=20,LiquidPlaneY=plane,MouthCenter=mouth,MouthRadius=radius};
                    PourPose.SetImpactBoundary(ref input,PourPose.Binding(target,container));
                    foreach(bool active in new[]{true,false})
                    {
                        foreach(var f in features)f.SetActive(active);foreach(var r in renderers)r.SetDirty();yield return null;
                        stream.Tint=new Color(.03f,.9f,1);stream.Draw(input);var with=Capture(camera,id+"-"+active+"-with");
                        if(active){stream.Tint=new Color(.85f,.94f,1);stream.Draw(input);Object.Destroy(Capture(camera,id+"-natural"));}
                        stream.Hide();var without=Capture(camera,id+"-"+active+"-without");
                        int inside=ChangedNearStream(camera,with,without,Vector3.Lerp(mouth,new Vector3(mouth.x,plane,mouth.z),.18f),Vector3.Lerp(mouth,new Vector3(mouth.x,plane,mouth.z),.80f));
                        int above=ChangedNearStream(camera,with,without,mouth+Vector3.up*.045f,mouth+Vector3.up*.012f);
                        rows.Add(id+","+active+","+inside+","+above);
                        if(inside<=30)errors.Add(id+" feature="+active+" cup section invisible: "+inside);
                        if(above<=15)errors.Add(id+" feature="+active+" outlet invisible: "+above);
                        Object.Destroy(with);Object.Destroy(without);
                    }
                    Object.Destroy(stream);yield return null;
                }
            }
            finally{foreach(var f in features)f.SetActive(true);foreach(var r in renderers)r.SetDirty();}
            File.WriteAllLines(Path.Combine(Output,"glass-pixels.csv"),rows);Assert.That(errors,Is.Empty,string.Join("; ",errors));
        }
        [UnityTest]public IEnumerator RapidUprightRetainsClearanceAndDoesNotTeleport()
        {
            yield return Load();var rows=new List<string>{"target,frame,inputAngle,tailFlow,shownAngle,lipStepMetres,clearanceMetres"};var errors=new List<string>();
            var source=session.Views.First(v=>v.DefinitionId=="bottle.gin");((Bottle)source.Item).SetOpen(true);
            var sourceRotation=source.transform.rotation;var camera=session.Player.ViewCamera;
            foreach(string id in new[]{"jigger.dual","glass.highball"})
            {
                var target=session.Views.First(v=>v.DefinitionId==id);var targetRotation=target.transform.rotation;
                var sourceHome=camera.transform.TransformPoint(new Vector3(.18f,-.35f,.5f));var targetHome=camera.transform.TransformPoint(new Vector3(-.14f,-.26f,.5f));
                source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                var pose=new PourPose();pose.Begin(source,target,camera.transform,true);
                Container receiving=target.Item is Jigger j?j.ActiveCup:(Container)target.Item;
                var input=pose.Step(.25f,110,30,receiving);Vector3 previous=source.PourPoint;float previousAngle=110;
                for(int frame=0;frame<35;frame++)
                {
                    source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                    float tailFlow=frame<6?30:0;input=pose.Step(1f/60,0,tailFlow,receiving);
                    float angle=Vector3.Angle(PourPose.Axis(source),Vector3.up),lipStep=Vector3.Distance(previous,source.PourPoint);
                    float clearance=float.PositiveInfinity;
                    // Conservative body-axis sampling: no frame may place the thick bottle body in the receiver.
                    for(int sample=3;sample<=24;sample++)
                    {
                        var point=source.PourPoint-PourPose.Axis(source)*(source.Size.y*sample/24f);
                        if(point.y>input.MouthCenter.y||point.y<target.transform.position.y)continue;
                        float distance=new Vector2(point.x-input.MouthCenter.x,point.z-input.MouthCenter.z).magnitude-input.MouthRadius-Mathf.Max(source.Size.x,source.Size.z)*.5f;
                        clearance=Mathf.Min(clearance,distance);
                    }
                    if(clearance<-.001f)errors.Add(id+" frame "+frame+" body penetrates: "+clearance);
                    if(lipStep>.025f)errors.Add(id+" frame "+frame+" lip teleports: "+lipStep);
                    if(previousAngle-angle>8.01f)errors.Add(id+" frame "+frame+" upright angle jumps: "+(previousAngle-angle));
                    rows.Add(id+","+frame+",0,"+tailFlow+","+angle+","+lipStep+","+clearance);
                    previous=source.PourPoint;previousAngle=angle;
                    if(frame==0||frame==6||frame==20){yield return null;Object.Destroy(Capture(camera,id+"-return-"+frame.ToString("D2")));}
                    source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                    var corrected=pose.Step(0,0,tailFlow,receiving);
                    Assert.That(Vector3.Angle(PourPose.Axis(source),Vector3.up),Is.EqualTo(angle).Within(.02f),"LateUpdate correction must not advance return again");
                }
                Assert.That(previousAngle,Is.LessThan(.02f));
            }
            File.WriteAllLines(Path.Combine(Output,"upright-frames.csv"),rows);Assert.That(errors,Is.Empty,string.Join("; ",errors.Take(12)));
        }
    }
}
