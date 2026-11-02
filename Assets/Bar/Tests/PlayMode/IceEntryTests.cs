using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>从低于杯口的侧面持冰点出发，按真实显示网格角点验证先过杯口再入内腔。</summary>
    public sealed class IceEntryTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../IceEntryReports"));
        [UnityTest]public IEnumerator EveryContainerReceivesIceThroughItsMouth()=>Check(false);
        [UnityTest]public IEnumerator EntryFollowsMovingAndTiltingReceivingFrames()=>Check(true);
        private static Vector3[] Corners(MeshFilter filter,Transform frame)
        {
            var bounds=filter.sharedMesh.bounds;var result=new Vector3[8];
            for(int i=0;i<8;i++)result[i]=frame.InverseTransformPoint(filter.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return result;
        }
        private static bool CutsWall(Vector3[] corners,CavityShape shape)
        {
            var centre=corners.Aggregate(Vector3.zero,(a,b)=>a+b)/8;
            float radial=new Vector2(centre.x-shape.Axis.x,centre.z-shape.Axis.y).magnitude;
            float support=corners.Max(v=>new Vector2(v.x-centre.x,v.z-centre.z).magnitude);
            float outer=Enumerable.Range(0,shape.Samples).Max(shape.RadiusOf);
            if(radial>outer+support+.001f)return false; // Entirely outside, before the lift/traverse gate.
            foreach(var corner in corners)
            {
                if(corner.y>shape.Top+.0005f)continue;
                if(corner.y<shape.Bottom-.001f)return true;
                if(new Vector2(corner.x-shape.Axis.x,corner.z-shape.Axis.y).magnitude>shape.RadiusAt(corner.y)+.001f)return true;
            }
            return false;
        }
        private IEnumerator Check(bool moving)
        {
            Directory.CreateDirectory(Output);var rows=new List<string>{"container,moving,frame,cube,centreX,centreY,centreZ,lowestCornerY,rimY"};int oldViolations=0;
            for(int scenario=0;scenario<5;scenario++)
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
                var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
                string id=scenario==0?"glass.highball":scenario==1?"glass.coupe":scenario==2?"shaker.cobbler":"jigger.dual";
                var view=session.Views.First(v=>v.DefinitionId==id);if(scenario==4){((Jigger)view.Item).Flip();yield return new WaitForSeconds(.4f);}
                Container vessel=view.Item is CobblerShaker sh?sh.Body:view.Item is Jigger j?j.ActiveCup:(Container)view.Item;
                var binding=PourPose.Binding(view,vessel);var rig=binding.GetComponent<LocalIceWorld>();rig.enabled=false;
                var shape=binding.Shape;var home=view.transform.position;var rotation=view.transform.rotation;
                int cubes=Mathf.Min(3,rig.Capacity);var originLocal=new Vector3(shape.Axis.x+.25f,shape.Bottom+.025f,shape.Axis.y-.04f);var from=binding.transform.TransformPoint(originLocal);
                session.IceOrigins[vessel.CoreVesselId]=from;
                // The 30 ml end cannot accept a full 40 g portion under the unchanged core capacity rule.
                double grams=System.Math.Min(session.Adapter.World.Data.icePortionGrams*cubes,vessel.CapacityMl*session.Adapter.World.Data.iceDensity*.9);
                session.Adapter.World.Apply(new Operation{type="AddIce",to=vessel.CoreVesselId,grams=grams});session.Adapter.Advance(0);
                string before=LogReplay.SnapshotHash(session.Adapter.World);bool sawGate=false,sawInside=false;
                var camera=session.Player.ViewCamera;camera.fieldOfView=42;camera.aspect=640f/480;
                var rim=binding.transform.TransformPoint(new Vector3(shape.Axis.x,shape.Top,shape.Axis.y));camera.transform.position=rim+new Vector3(.14f,.19f,-.32f);camera.transform.LookAt(rim+Vector3.up*.01f);
                for(int frame=0;frame<84;frame++)
                {
                    if(moving){view.transform.position=home+new Vector3(.025f*Mathf.Sin(frame*.04f),.008f*Mathf.Sin(frame*.02f),0);view.transform.rotation=Quaternion.Euler(0,0,8*Mathf.Sin(frame*.035f))*rotation;}
                    rig.Advance(.22f/80);
                    var meshes=binding.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="IceSurface"&&f.gameObject.activeInHierarchy).ToArray();Assert.That(meshes.Length,Is.EqualTo(cubes));
                    for(int cube=0;cube<meshes.Length;cube++)
                    {
                        var corners=Corners(meshes[cube],binding.transform);var centre=corners.Aggregate(Vector3.zero,(a,b)=>a+b)/8;float low=corners.Min(v=>v.y);
                        Assert.That(CutsWall(corners,shape),Is.False,vessel.Definition.Id+" frame "+frame+" cube "+cube+" cuts side/bottom before entering the mouth");
                        if(low>shape.Top+.04f)sawGate=true;if(low<=shape.Top&&centre.y>shape.Bottom)sawInside=true;
                        rows.Add(vessel.Definition.Id+","+moving+","+frame+","+cube+","+centre.x+","+centre.y+","+centre.z+","+low+","+shape.Top);
                    }
                    if(!moving&&scenario==0&&(frame==12||frame==40||frame==68))
                    {var image=PourCaptureTests.Capture(camera,"ice-entry-"+frame);File.Copy(Path.Combine(PourCaptureTests.Output,"ice-entry-"+frame+".png"),Path.Combine(Output,"entry-"+frame+".png"),true);Object.Destroy(image);}
                }
                Assert.That(sawGate,Is.True,"the whole block must clear the rim before crossing toward the centre");
                if(scenario<3)Assert.That(sawInside,Is.True,"fitting ice should actually enter");
                Assert.That(rig.EnteringCount,Is.Zero);Assert.That(session.IceOrigins.ContainsKey(vessel.CoreVesselId),Is.False,"entry origin must be consumed, never reused on reopening");
                Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(before),"animation must not advance or rewrite the model");
                if(!moving)
                {
                    // Replay the exact former interpolation against the same final block and geometry.
                    var mesh=binding.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="IceSurface");var offsets=Corners(mesh,binding.transform);var display=mesh.transform;while(display.parent!=binding.transform)display=display.parent;var root=binding.transform.InverseTransformPoint(display.position);
                    for(int sample=0;sample<=80;sample++)
                    {
                        float t=sample/80f;var old=Vector3.Lerp(originLocal,root,t)+binding.transform.InverseTransformVector(Vector3.up)*(.06f*Mathf.Sin(t*Mathf.PI));
                        if(CutsWall(offsets.Select(v=>old+v-root).ToArray(),shape))oldViolations++;
                    }
                }
                view.transform.SetPositionAndRotation(home,rotation);
            }
            if(!moving)Assert.That(oldViolations,Is.GreaterThan(0),"regression fixture must reject the former direct-to-landing curve");
            File.WriteAllLines(Path.Combine(Output,moving?"moving-frames.csv":"static-frames.csv"),rows);File.WriteAllText(Path.Combine(Output,moving?"moving-result.txt":"static-result.txt"),"wallCuts=0 oldCurveViolationSamples="+oldViolations);
        }
    }
}
