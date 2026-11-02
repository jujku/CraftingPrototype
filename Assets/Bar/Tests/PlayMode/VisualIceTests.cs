using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using Bar.Logging;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    internal sealed class VisualIceTests
    {
        [UnityTest]public IEnumerator CaptureFourVisualSequences()
        {yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;var s=Object.FindFirstObjectByType<BarSession>();s.gameObject.AddComponent<VisualSmokeDirector>().CaptureFrames=true;yield return new WaitForSecondsRealtime(32.5f);}
        [UnityTest]public IEnumerator BothCcdModesContainIceWithoutChangingCore()
        {
            var report="";
            foreach(var mode in new[]{CollisionDetectionMode.Continuous,CollisionDetectionMode.ContinuousSpeculative})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;var view=s.Views.First(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;var rig=view.GetComponentInChildren<LocalIceWorld>();rig.Detection=mode;
                s.Adapter.World.Apply(new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="tonic",ml=140});s.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=60});s.Adapter.Advance(0);
                string before=LogReplay.SnapshotHash(s.Adapter.World);var origin=view.transform.position;double ms=0;
                for(int i=0;i<180;i++){view.transform.position=origin+new Vector3(Mathf.Sin(i*.71f)*.4f,Mathf.Cos(i*.4f)*.15f,0);view.transform.rotation=Quaternion.Euler(Mathf.Sin(i*.3f)*60,0,Mathf.Cos(i*.35f)*65);rig.Advance(1f/60);ms+=rig.StepMilliseconds;}
                Assert.That(rig.DisplayCount,Is.EqualTo(System.Math.Ceiling(60/s.Adapter.World.Data.icePortionGrams)));Assert.That(rig.WallEscapes,Is.Zero);Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(before));report+=mode+": avg "+(ms/180).ToString("R")+" ms, wall escapes 0, core hash "+before+"\n";
                // Large ice plus the moving kinematic spoon must keep whole meshes in
                // the cavity. The old centre-only trigger missed partially escaped cubes.
                view.transform.SetPositionAndRotation(origin,Quaternion.identity);
                var spoon=s.Views.First(v=>v.Item is BarSpoon);rig.PreviewSpoon=spoon.Anchor("Tip");rig.PreviewIntensity=.8f;
                var binding=view.GetComponentInChildren<ContainerVisual>();var cavity=binding.InnerCavity.sharedMesh.bounds;
                for(int i=0;i<240;i++)
                {
                    spoon.PoseStirring(view,glass,new Vector2(Mathf.Cos(i*.18f),Mathf.Sin(i*.18f))*36);rig.Advance(1f/60);
                    foreach(var display in binding.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="IceSurface"))
                    {
                        var b=display.sharedMesh.bounds;
                        for(int j=0;j<8;j++)
                        {
                            var corner=b.center+Vector3.Scale(b.extents,new Vector3((j&1)==0?-1:1,(j&2)==0?-1:1,(j&4)==0?-1:1));
                            var local=binding.InnerCavity.transform.InverseTransformPoint(display.transform.TransformPoint(corner));
                            Assert.That(local.x,Is.InRange(cavity.min.x-.001f,cavity.max.x+.001f));Assert.That(local.z,Is.InRange(cavity.min.z-.001f,cavity.max.z+.001f));
                        }
                    }
                }
                Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(before));report+="moving spoon: 240 steps, full mesh bounds contained, core unchanged\n";
            }
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"../VisualReports"));File.WriteAllText(Path.Combine(Application.dataPath,"../VisualReports/ccd-comparison.txt"),report);
        }
    }
}
