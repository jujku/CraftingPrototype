using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>空量酒器检视动作和左右手习惯镜像的验收截图与断言。只动表现和手的归属，不改核心。</summary>
    public sealed class InspectHandednessCaptures
    {
        private BarSession s;private readonly List<Texture2D> tiles=new List<Texture2D>();private readonly List<string> events=new List<string>();
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private void Aim(string id){var view=View(id);s.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view),"真实射线目标 "+id);}
        private IEnumerator Pick(string id){Aim(id);s.RightButton(Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Hands.SideOf(View(id).Item).HasValue,Is.True,"拿取 "+id+"；"+s.Clock.Error);}
        private void Shot(string name){s.GetComponent<HeldItemRendering>().Refresh();tiles.Add(HoldFramingCaptures.Capture(s.Player.ViewCamera,"v3-"+name));}
        /// <summary>杯口轴与“杯 → 眼”方向的夹角，以及刻度是否在离眼较远的那面内壁（这样从杯口望进去正好看到）。</summary>
        private static float MouthAngle(ItemView jig,string cup,Camera camera,out bool farWall)
        {
            Vector3 Shell(string name)=>jig.GetComponentsInChildren<Transform>().First(t=>t.name==name).GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Shell").bounds.center;
            var shell=Shell(cup);var open=(shell-Shell(cup=="CupA"?"CupB":"CupA")).normalized;var eye=(camera.transform.position-shell).normalized;
            var marks=jig.GetComponentsInChildren<Transform>().First(t=>t.name==cup).GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Geometry_Mark")).Select(r=>r.bounds.center).ToArray();
            var centroid=marks.Aggregate(Vector3.zero,(a,b)=>a+b)/marks.Length;farWall=Vector3.Dot(Vector3.ProjectOnPlane(centroid-shell,open),eye)<0;return Vector3.Angle(open,eye);
        }
        [TearDown] public void RestoreHandedness(){PlayerPrefs.DeleteKey(BarSession.HandednessKey);PlayerPrefs.Save();}
        [UnityTest] public IEnumerator EmptyJiggerInspectReturnsToRestAndHandednessMirrors()
        {
            PlayerPrefs.DeleteKey(BarSession.HandednessKey);
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;s.Player.ViewCamera.aspect=16f/9;
            s.Adapter.Events.Published+=e=>{var r=e.Observation as LogRecord;events.Add(e.Kind+":"+r?.actionId+":"+r?.hand);};var rig=s.GetComponent<HoldViewRig>();Assert.That(s.Hands.Dominant,Is.EqualTo(HandSide.Right));
            yield return Pick("jigger.dual");var jig=View("jigger.dual");var jigger=(Jigger)jig.Item;Assert.That(s.Hands.Left,Is.SameAs(jigger),"右手习惯：量酒器默认进左手");
            yield return new WaitForSecondsRealtime(.8f);Shot("0-rest");var anchor=s.Player.LeftAnchor;var restPosition=anchor.localPosition;var restRotation=anchor.localRotation;
            var end=jigger.ActiveEnd;double inputs=s.Adapter.World.Inputs.m;var camera=s.Player.ViewCamera;float worst=1;var stages=new[]{1f,1f,1f};
            // F: three stages over 3.2 s, then back to the hold pose with the same end up.
            s.Inspect(Now());float start=Time.realtimeSinceStartup;var marks=new[]{.55f,1.25f,1.9f,2.55f,2.9f};int next=0;var mouths=new List<string>();
            while(Time.realtimeSinceStartup-start<4f)
            {
                yield return null;float t=Time.realtimeSinceStartup-start;
                if(t>.1f&&t<3.1f){float seen=HoldFramingCaptures.Measure(camera,jig).Visible;worst=Mathf.Min(worst,seen);int stage=t<1.45f?0:t<2.35f?1:2;stages[stage]=Mathf.Min(stages[stage],seen);Assert.That(rig.Inspecting(HandSide.Left),Is.True,"检视进行中 t="+t);}
                if(next<marks.Length&&t>=marks[next])
                {
                    // Stage 1 must show the big cup's marks, then the small cup's: the mouth turned toward the eye and the marked wall on the far side.
                    if(next<2){var cup=next==0?"CupA":"CupB";float angle=MouthAngle(jig,cup,camera,out bool far);mouths.Add(cup+" "+angle.ToString("F1")+"° far="+far);Assert.That(angle,Is.InRange(20f,50f),cup+" 杯口对着视线");Assert.That(far,Is.True,cup+" 刻度在对面内壁");}
                    Shot((next++ +1)+"-inspect");
                }
            }
            Assert.That(events,Does.Contain("view_inspect:jigger.inspect:Left"),"检视记入日志");
            Assert.That(rig.Inspecting(HandSide.Left),Is.False);Assert.That(Vector3.Distance(anchor.localPosition,restPosition),Is.LessThan(.01f),"检视后回到持握位");
            Assert.That(Quaternion.Angle(anchor.localRotation,restRotation),Is.LessThan(3f),"检视后朝向复原");Assert.That(jigger.ActiveEnd,Is.EqualTo(end),"检视不换端");Assert.That(s.Adapter.World.Inputs.m,Is.EqualTo(inputs),"检视不改核心");
            Shot("6-after");
            // Any action interrupts it smoothly.
            s.Inspect(Now());yield return new WaitForSecondsRealtime(.5f);Assert.That(rig.Inspecting(HandSide.Left),Is.True);
            s.Player.AimAt(CapSteps.TablePoint(s,.9f,-.25f));Physics.SyncTransforms();s.Targeting();int flip=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="jigger.flip");Assert.That(flip,Is.GreaterThanOrEqualTo(0));
            s.SelectMenu(flip,true);s.LeftButton(true,Now());s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(.1f);Assert.That(rig.Inspecting(HandSide.Left),Is.False,"开始动作就打断检视");
            yield return new WaitForSecondsRealtime(1.2f);Assert.That(Vector3.Distance(anchor.localPosition,restPosition),Is.LessThan(.02f),"打断后平滑回到持握位");
            // H: mirror to left-handed. What is held changes hands; later pick-ups follow the mirrored preference.
            yield return Pick("bottle.gin");Assert.That(s.Hands.Right,Is.SameAs(View("bottle.gin").Item));yield return new WaitForSecondsRealtime(.6f);Shot("7-right-handed");
            s.ToggleHandedness(Now());yield return new WaitForSecondsRealtime(.9f);Shot("8-left-handed");
            Assert.That(s.Hands.Dominant,Is.EqualTo(HandSide.Left));Assert.That(s.Hands.Right,Is.SameAs(jigger));Assert.That(s.Hands.Left,Is.SameAs(View("bottle.gin").Item));
            Assert.That(PlayerPrefs.GetString(BarSession.HandednessKey),Is.EqualTo("Left"),"偏好记在本机");Assert.That(events.Any(x=>x.StartsWith("handedness_changed:")&&x.EndsWith(":Left")),Is.True,"切换记入日志");
            yield return CapSteps.PutDown(s,View("bottle.gin"));yield return CapSteps.PutDown(s,jig);yield return Pick("jigger.dual");Assert.That(s.Hands.Right,Is.SameAs(jigger),"左手习惯：量酒器进右手");
            s.Inspect(Now());yield return new WaitForSecondsRealtime(.55f);Assert.That(rig.Inspecting(HandSide.Right),Is.True);Assert.That(MouthAngle(jig,"CupA",camera,out bool leftFar),Is.InRange(20f,50f));Assert.That(leftFar,Is.True);Shot("9-left-handed-inspect");yield return new WaitForSecondsRealtime(3f);
            s.ToggleHandedness(Now());yield return new WaitForSecondsRealtime(.3f);Assert.That(s.Hands.Dominant,Is.EqualTo(HandSide.Right));Assert.That(s.Hands.Left,Is.SameAs(jigger));
            int w=480,h=270,columns=3,rows=(tiles.Count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rows,TextureFormat.RGB24,false);
            for(int i=0;i<tiles.Count;i++){var tile=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rows-1-i/columns)*h+y,tile.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(tile);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"v3.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllText(Path.Combine(HoldFramingCaptures.Output,"v3.txt"),"worst jigger visible fraction while inspecting: "+worst.ToString("F3")+" (by stage "+string.Join(", ",stages.Select(v=>v.ToString("F3")))+")\nstage 1 mouth angle to eye: "+string.Join("; ",mouths));
            Assert.That(worst,Is.GreaterThan(.8f),"检视全程量酒器基本都在画面内");
        }
    }
}
