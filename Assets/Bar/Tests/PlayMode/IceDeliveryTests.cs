using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
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
    /// <summary>真实菜单输入：工具先到杯口，冰接力滑落、逐角过口，左右手/手持杯/余冰/取消均不改变原始回执。</summary>
    public sealed class IceDeliveryTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../IceDeliveryReports"));
        [UnityTest]public IEnumerator ScoopReachesTableGlassBeforeTwoBlocksSlide()=>Run("tool.scoop","glass.highball",false,false,HandSide.Right);
        [UnityTest]public IEnumerator LeftTongsReachTableCoupeBeforeReleasing()=>Run("tool.tongs","glass.coupe",false,false,HandSide.Left);
        [UnityTest]public IEnumerator LeftScoopDeliversToOppositeHeldShaker()=>Run("tool.scoop","shaker.cobbler",true,false,HandSide.Left);
        [UnityTest]public IEnumerator RightTongsDeliverToOppositeHeldGlass()=>Run("tool.tongs","glass.highball",true,false,HandSide.Right);
        [UnityTest]public IEnumerator PartialScoopLeavesUnacceptedBlockOnTool()=>Run("tool.scoop","glass.highball",false,true,HandSide.Right);
        [UnityTest]public IEnumerator CancelDuringReachDoesNotConsumeIce()=>Run("tool.scoop","glass.highball",false,false,HandSide.Right,true);
        private IEnumerator Run(string toolId,string vesselId,bool held,bool partial,HandSide hand,bool cancel=false)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
            var tool=View(toolId);var target=View(vesselId);var vessel=((ILiquidReceiver)target.Item).ReceivingContainer;
            var binding=ItemView.StirBinding(target,vessel);var local=binding.GetComponent<LocalIceWorld>();
            Assert.That(s.Hands.SetDominant(hand),Is.True);Assert.That(s.Hands.TryPickUp(tool.Item),Is.True);
            if(held)Assert.That(s.Hands.TryPickUp(target.Item),Is.True);
            if(partial){s.Adapter.World.Apply(new Operation{type="AddIce",to=vessel.CoreVesselId,grams=120});s.Adapter.Advance(0);}
            var bin=(IceBin)View("fixture.icebin").Item;new TakeIceAction(()=>0).Begin(tool.Item,bin,s.Hands);
            yield return new WaitForSecondsRealtime(.5f);
            if(!held)s.Player.AimAt(target.GetComponent<Collider>().bounds.center);
            else s.Player.AimAt(View("fixture.sink").transform.position);
            Physics.SyncTransforms();s.Targeting();
            int selected=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="ice.add"&&o.Actor==tool.Item&&o.Target==target.Item);
            Assert.That(selected,Is.GreaterThanOrEqualTo(0));s.SelectMenu(selected,true);
            int loaded=((Tool)tool.Item).LoadedCubes;var initial=tool.transform.position;
            var originalHand=s.Hands.MainHand;string name=(cancel?"cancel":partial?"partial":toolId)+"-"+hand+(held?"-held":"-table");Directory.CreateDirectory(Output);
            Capture(s,target,name+"-01-held");s.LeftButton(true,Now());s.LeftButton(false,Now()+.001);
            Assert.That(s.IceDeliveryBusy,Is.True);Assert.That(((Tool)tool.Item).LoadedCubes,Is.EqualTo(loaded),"伸手前不能扣冰");
            s.ToggleMain(Now());s.ToggleHandedness(Now());
            float start=Time.realtimeSinceStartup;bool sawReach=false,sawTransfer=false,sawSlide=false,sawInside=false,sawTip=false;int transferred=0;float previous=0,maxFrameStep=0;var last=tool.transform.position;
            var rows=new List<string>{"seconds,toolX,toolY,toolZ,loaded,entering,displayCount"};bool cancelled=false;
            while(Time.realtimeSinceStartup-start<4)
            {
                yield return null;float time=Time.realtimeSinceStartup-start;
                maxFrameStep=Mathf.Max(maxFrameStep,Vector3.Distance(tool.transform.position,last));last=tool.transform.position;
                int current=((Tool)tool.Item).LoadedCubes;var rim=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Top,binding.Shape.Axis.y));
                if(current==loaded&&Vector3.Distance(tool.transform.position,initial)>.01f)sawReach=true;
                if(cancel&&!cancelled&&time>.12f){s.Cancel(Now());cancelled=true;}
                if(current<loaded&&!sawTransfer)
                {
                    sawTransfer=true;transferred=loaded-current;
                    Assert.That(time,Is.GreaterThan(.35f),"原动作必须等工具抵达后才开始");
                    var release=s.IceDeliveryDisplay.ReleasePosition;
                    Assert.That(Vector2.Distance(new Vector2(release.x,release.z),new Vector2(rim.x,rim.z)),Is.LessThan(.002f));
                    Assert.That(release.y-rim.y,Is.GreaterThan(IceAppearance.ConfiguredSide*.7f));
                    Capture(s,target,name+"-02-over-mouth");
                }
                sawTip|=s.IceDeliveryDisplay.TipFraction>.8f;
                foreach(var display in local.Displays)
                {
                    if(!display.gameObject.activeInHierarchy)continue;
                    var filter=display.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="IceSurface");var b=filter.sharedMesh.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var v=binding.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                        if(v.y>binding.Shape.Top+.001f){sawSlide|=sawTransfer;continue;}
                        Assert.That(v.y,Is.GreaterThanOrEqualTo(binding.Shape.Bottom-.001f));
                        Assert.That(new Vector2(v.x-binding.Shape.Axis.x,v.z-binding.Shape.Axis.y).magnitude,Is.LessThanOrEqualTo(binding.Shape.RadiusAt(v.y)+.0015f),"冰必须从口进入，不能横穿杯壁");sawInside=true;
                    }
                }
                if(sawTransfer&&previous<.75f&&time>=.75f)Capture(s,target,name+"-03-sliding");previous=time;
                rows.Add($"{time:F4},{tool.transform.position.x:F4},{tool.transform.position.y:F4},{tool.transform.position.z:F4},{current},{local.EnteringCount},{local.DisplayCount}");
                if(!s.IceDeliveryBusy&&time>.15f)break;
            }
            Assert.That(s.IceDeliveryBusy,Is.False);Assert.That(s.Hands.MainHand,Is.EqualTo(originalHand));Assert.That(s.Hands.Dominant,Is.EqualTo(hand));Assert.That(sawReach,Is.True);Assert.That(s.Clock.Error,Is.Null);
            var records=s.Journal.ReadLines().Select(SessionJournal.Decode).Where(r=>r.kind=="core_operation"&&r.operation.type=="AddIce").ToArray();
            if(cancel){Assert.That(sawTransfer,Is.False);Assert.That(records,Is.Empty);Assert.That(((Tool)tool.Item).LoadedCubes,Is.EqualTo(loaded));}
            else
            {
                Assert.That(sawTransfer&&sawSlide&&sawInside&&sawTip,Is.True);Assert.That(records.Length,Is.EqualTo(1));Assert.That(records[0].operation.grams,Is.EqualTo((partial?1:loaded)*40));
                Assert.That(transferred,Is.EqualTo(partial?1:loaded));Assert.That(((Tool)tool.Item).LoadedCubes,Is.EqualTo(partial?1:0));Assert.That(local.EnteringCount,Is.Zero);
                if(!partial)new LogReplay().Run(s.Journal.ReadLines());
            }
            Capture(s,target,name+"-04-returned");File.WriteAllLines(Path.Combine(Output,name+".csv"),rows);
            File.WriteAllText(Path.Combine(Output,name+"-result.txt"),$"reach={sawReach}\ntransfer={sawTransfer}\nslide={sawSlide}\ninside={sawInside}\ntransferred={transferred}\nleftover={((Tool)tool.Item).LoadedCubes}\nmaxFrameStep={maxFrameStep}\n");
            s.Cancel(Now());
        }
        private static void Capture(BarSession s,ItemView target,string name)
        {
            var go=new GameObject("Ice delivery evidence");var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Player.ViewCamera);camera.enabled=false;camera.cullingMask=-1;
            var binding=target.GetComponentInChildren<ContainerVisual>();var at=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Top,binding.Shape.Axis.y));
            camera.transform.position=at+new Vector3(.3f,.28f,-.45f);camera.transform.LookAt(at+Vector3.up*.05f);camera.fieldOfView=48;
            var rt=RenderTexture.GetTemporary(960,720,24);var old=RenderTexture.active;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            Object.Destroy(image);RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(go);
        }
    }
}
