using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>
    /// 新搅拌动作（StirMotion）的验收截图和数据。真实场景、真实菜单和鼠标：杯里有金酒和冰，
    /// 玩家按下后从按下点开始画圈（圈心不在按下点），圈速和大小不均匀、慢慢漂移、每帧有手抖；先逆时针 3.2 s，停手 0.8 s，再顺时针 1.5 s，松手。
    /// 每帧检查勺头和勺杆不穿杯壁、杯底；记录勺头轨迹、相位、转速、轻碰次数和核心收到的强度；同一段鼠标按旧写法（勺头位置直接等于鼠标偏移）重算比较平滑度。
    /// </summary>
    public sealed class StirMotionCaptures
    {
        private BarSession s;private Camera close;private readonly List<Texture2D> tiles=new List<Texture2D>();
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private void Aim(string id){var view=View(id);s.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view),"真实射线目标 "+id);}
        private IEnumerator Pick(string id){yield return View(id).Item is Bottle?CapSteps.Uncap(s,View(id)):null;Aim(id);s.RightButton(Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Hands.SideOf(View(id).Item).HasValue,Is.True,"拿取 "+id+"；"+s.Clock.Error);}
        private void Choose(string action,string actor,string target)
        {
            var view=View(target);s.Player.AimAt(view.Item is Fixture&&view.Anchor("Surface")?view.Anchor("Surface").position:view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();
            int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id==action&&o.Actor==View(actor).Item&&(o.Target==View(target).Item||o.Target.Parent==View(target).Item));
            Assert.That(index,Is.GreaterThanOrEqualTo(0),action+" · "+actor+" → "+target+"；"+s.Clock.Error);s.SelectMenu(index,true);s.LeftButton(true,Now());
        }
        private IEnumerator Click(string action,string actor,string target)
        {
            Choose(action,actor,target);s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(1.05f);
            // 2026-10-29: adding ice now reaches the tool to the glass, tips it and brings the hands back (IceDeliveryMotion); input waits until that ends.
            float deadline=Time.realtimeSinceStartup+4;while(s.IceDeliveryBusy&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(s.IceDeliveryBusy,Is.False,"等待装冰回手后才执行下一步");Assert.That(s.Clock.Error,Is.Null,action);
        }
        private void Shot(string name){s.GetComponent<HeldItemRendering>().Refresh();tiles.Add(HoldFramingCaptures.Capture(close?close:s.Player.ViewCamera,"stir-"+name));}
        private static string F(float v)=>v.ToString("F4",CultureInfo.InvariantCulture);
        private static float Jerk(List<Vector3> p,List<float> dt)
        {
            double sum=0;int n=0;
            for(int i=3;i<p.Count;i++){float h=(dt[i]+dt[i-1]+dt[i-2])/3;if(h<=0)continue;var j=(p[i]-3*p[i-1]+3*p[i-2]-p[i-3])/(h*h*h);sum+=j.sqrMagnitude;n++;}
            return n>0?(float)System.Math.Sqrt(sum/n):0;
        }
        [UnityTest] public IEnumerator SpoonEntersOverTheRimStirsAlongTheWallAndLiftsOut()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;s.Player.ViewCamera.aspect=16f/9;
            yield return Pick("bottle.gin");Choose("pour.bottle_to_container","bottle.gin","glass.highball");s.PointerDelta(new Vector2(0,1000),Now()+.001);yield return new WaitForSecondsRealtime(1.2f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.6f);
            yield return CapSteps.PutDown(s,View("bottle.gin"));
            yield return Pick("tool.scoop");yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","glass.highball");
            yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","glass.highball");yield return CapSteps.PutDown(s,View("tool.scoop"));
            yield return Pick("tool.spoon");yield return new WaitForSecondsRealtime(.6f);
            var spoon=View("tool.spoon");var glass=View("glass.highball");var container=(Container)glass.Item;var binding=ItemView.StirBinding(glass,container);var shape=binding.Shape;var frame=binding.transform;
            var tip=spoon.Anchor("Tip");var grip=spoon.Anchor("Grip_R")??spoon.Anchor("Grip_L");var bowl=spoon.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="SpoonBowl");var vertices=bowl.sharedMesh.vertices;
            double liquid=container.Contents.LiquidMl;Assert.That(liquid,Is.GreaterThan(20));Assert.That(container.Contents.IceCubes,Is.GreaterThan(0));
            float fullRadius=0;
            // Every frame: below the rim, each bowl vertex and the shaft must be inside the cavity or clearly outside the glass, never in the wall or the base.
            string Clip()
            {
                foreach(var v in vertices)
                {
                    var p=frame.InverseTransformPoint(bowl.transform.TransformPoint(v));if(p.y>=shape.Top)continue;float r=Vector2.Distance(new Vector2(p.x,p.z),shape.Axis),wall=shape.RadiusAt(Mathf.Max(shape.Bottom,p.y));
                    if(r>wall+.0002f&&r<wall+.01f)return "bowl in the wall at "+p.ToString("F4");if(r<=wall+.0002f&&p.y<shape.Bottom-.0001f)return "bowl through the base at "+p.ToString("F4");
                }
                for(int k=0;k<=20;k++)
                {
                    var p=frame.InverseTransformPoint(Vector3.Lerp(tip.position,grip.position,k/20f));if(p.y>=shape.Top||p.y<shape.Bottom)continue;
                    float r=Vector2.Distance(new Vector2(p.x,p.z),shape.Axis),wall=shape.RadiusAt(p.y);if(r>wall-.0015f+.0002f&&r<wall+.01f)return "shaft in the wall at "+p.ToString("F4");
                }
                return null;
            }
            var random=new System.Random(11);Vector2 previous=Vector2.zero,mouse=Vector2.zero;float phi=Mathf.PI,mouseTurns=0,released=-1,stoppedAt=-1;
            var stages=new List<string>();var rows=new List<string>{"t,stage,mouseX,mouseY,mouseTurns,phase,spin,speed,clinks,tipX,tipY,tipZ,radial,oldX,oldZ,coreIntensity"};
            var path=new List<Vector3>();var oldPath=new List<Vector3>();var steps=new List<float>();
            float phaseAtCcw=0,phaseAtStop=0,phaseAtStopEnd=0,phaseAtCw=0,phaseAtCwEnd=0,coastSettled=-1,maxSpeed=0,minSubmerge=1,coreDuring=0;int clinksAtLoop=-1,clinks=0;string clip=null;var radials=new List<float>();
            var marks=new[]{.08f,.2f,.5f,1.4f,2.6f,3.6f,4.6f,5.62f,5.85f,6.6f};int next=0;Vector3 lastTip=Vector3.zero;bool first=true,lastNear=false;
            var newSpin=new List<float>();var oldSpin=new List<float>();var oldRadials=new List<float>();float newAngle=0,oldAngle=0;
            // Step up to the glass, as a player would, so the screenshots show the spoon in the glass rather than a distant counter.
            {var player=s.Player.transform;var toPlayer=Vector3.ProjectOnPlane(player.position-glass.transform.position,Vector3.up).normalized;
             var body=s.Player.Body;body.enabled=false;player.position=new Vector3(glass.transform.position.x,player.position.y,glass.transform.position.z)+toPlayer*.5f;body.enabled=true;
             s.Player.AimAt(glass.transform.position);yield return new WaitForSecondsRealtime(.6f);
             // A close-up camera for the screenshots only (all layers, so the held spoon on the foreground layer is drawn too); the game camera is untouched.
             close=new GameObject("StirCloseCamera").AddComponent<Camera>();close.enabled=false;close.cullingMask=~0;close.fieldOfView=38;close.nearClipPlane=.01f;
             var look=glass.transform.position+Vector3.up*glass.Size.y*.55f;close.transform.position=look+toPlayer*.17f+Vector3.up*.3f;close.transform.LookAt(look);}
            Choose("spoon.stir","tool.spoon","glass.highball");float start=Time.realtimeSinceStartup;var c=s.GestureControls;
            while(Time.realtimeSinceStartup-start<6.7f)
            {
                float t=Time.realtimeSinceStartup-start,dt=Time.unscaledDeltaTime;
                // A human circle: it starts at the press point (so the press point is on the circle), drifts, changes size and speed, and trembles.
                int direction=t<3.2f?1:t<4f?0:t<5.5f?-1:0;
                if(direction!=0)
                {
                    float rate=1.4f+.3f*Mathf.Sin(t*1.1f),radius=60+15*Mathf.Sin(t*.9f);phi+=direction*dt*2*Mathf.PI*rate;mouseTurns+=dt*rate;
                    var centre=new Vector2(60,0)+new Vector2(20,6)*t;mouse=centre+radius*new Vector2(Mathf.Cos(phi),Mathf.Sin(phi))+new Vector2((float)random.NextDouble()*6-3,(float)random.NextDouble()*6-3);
                    s.PointerDelta(mouse-previous,Now());previous=mouse;
                }
                if(t>=5.5f&&released<0){s.LeftButton(false,Now());released=t;}
                yield return null;float frameDt=Time.unscaledDeltaTime;
                var stir=s.StirDisplay;if(stir==null)continue;string stage=stir.StageName;
                if(stages.Count==0||stages[stages.Count-1]!=stage)stages.Add(stage);
                if(clip==null&&stage!="Idle"){var bad=Clip();if(bad!=null)clip=stage+" t="+t.ToString("F2")+": "+bad;}
                var local=frame.InverseTransformPoint(tip.position);float radial=Vector2.Distance(new Vector2(local.x,local.z),shape.Axis);
                // Tip speed from the first stirring frame on (choosing the menu turns the camera, and the held spoon with it).
                // Only near the glass (below 3 cm over the rim, within 1 cm of the wall): the trip between hand and glass follows the pour convention (about 0.2 s, any distance).
                bool near=local.y<shape.Top+.03f&&radial<shape.RadiusAt(Mathf.Clamp(local.y,shape.Bottom,shape.Top))+.01f;
                if(!first&&near&&lastNear&&frameDt>0)maxSpeed=Mathf.Max(maxSpeed,(tip.position-lastTip).magnitude/frameDt);first=false;lastTip=tip.position;lastNear=near;
                if(stage=="Loop"&&clinksAtLoop<0)clinksAtLoop=stir.ClinkSequence;clinks=stir.ClinkSequence;
                if(stage=="Loop"){fullRadius=Mathf.Max(fullRadius,spoon.StirSolution.Radius);minSubmerge=Mathf.Min(minSubmerge,binding.HeightForLiquid(container.Contents.LiquidMl)-local.y);}
                if(t<.9f)phaseAtCcw=stir.Phase;if(t<3.2f)phaseAtStop=stir.Phase;if(t<4f)phaseAtStopEnd=stir.Phase;if(t<4.4f)phaseAtCw=stir.Phase;if(t<5.5f)phaseAtCwEnd=stir.Phase;
                if(t>=3.2f&&t<4f&&coastSettled<0&&stir.Speed01<.05f)coastSettled=t-3.2f;
                if(t>1&&t<3.2f){radials.Add(radial);coreDuring=Mathf.Max(coreDuring,(float)s.Adapter.Agitation(container.CoreVesselId,.15));}
                // The old display: the bowl sat at the mouse offset from the press point, full radius at 36 px.
                var g=mouse;float oldR=fullRadius*Mathf.Clamp01(g.magnitude/36);var dir=g.sqrMagnitude>1e-6f?g.normalized:Vector2.right;var old=new Vector3(shape.Axis.x+dir.x*oldR,local.y,shape.Axis.y+dir.y*oldR);
                if(stage=="Loop"&&t>1&&t<3.2f)
                {
                    path.Add(local);oldPath.Add(old);steps.Add(frameDt);oldRadials.Add(oldR);
                    // Angular speed about the glass axis each frame: the new spoon versus the old mouse-offset rule.
                    float a=Mathf.Atan2(local.z-shape.Axis.y,local.x-shape.Axis.x),o=Mathf.Atan2(old.z-shape.Axis.y,old.x-shape.Axis.x);
                    if(path.Count>1&&frameDt>0){newSpin.Add(Mathf.Abs(Mathf.DeltaAngle(newAngle*Mathf.Rad2Deg,a*Mathf.Rad2Deg))*Mathf.Deg2Rad/frameDt);oldSpin.Add(Mathf.Abs(Mathf.DeltaAngle(oldAngle*Mathf.Rad2Deg,o*Mathf.Rad2Deg))*Mathf.Deg2Rad/frameDt);}
                    newAngle=a;oldAngle=o;
                }
                rows.Add(string.Join(",",F(t),stage,F(mouse.x),F(mouse.y),F(mouseTurns),F(stir.Phase),F(stir.Spin),F(stir.Speed01),stir.ClinkSequence.ToString(),F(local.x),F(local.y),F(local.z),F(radial),F(old.x),F(old.z),F((float)s.Adapter.Agitation(container.CoreVesselId,.15))));
                if(next<marks.Length&&t>=marks[next])Shot((next++ +1)+"-"+stage.ToLowerInvariant());
            }
            yield return new WaitForSecondsRealtime(.3f);Object.Destroy(close.gameObject);
            var hand=s.Hands.SideOf(spoon.Item)==HandSide.Left?s.Player.LeftAnchor:s.Player.RightAnchor;float home=Vector3.Distance(grip.position,hand.position);
            float jerkNew=Jerk(path,steps),jerkOld=Jerk(oldPath,steps);
            float Cv(List<float> v){if(v.Count<3)return -1;float m=v.Average();return Mathf.Sqrt(v.Select(x=>(x-m)*(x-m)).Average())/Mathf.Max(1e-6f,m);}
            float cvNew=Cv(newSpin),cvOld=Cv(oldSpin),oldMinRadial=oldRadials.Count>0?oldRadials.Min():0;
            float turnsCcw=(phaseAtStop-phaseAtCcw)/(2*Mathf.PI),turnsCw=(phaseAtCwEnd-phaseAtCw)/(2*Mathf.PI),coastTurns=(phaseAtStopEnd-phaseAtStop)/(2*Mathf.PI);
            float mouseCcw=0;{float rr=0;for(float tt=.9f;tt<3.2f;tt+=.001f)rr+=.001f*(1.4f+.3f*Mathf.Sin(tt*1.1f));mouseCcw=rr;}
            float meanRadial=radials.Count>0?radials.Average():0,minRadial=radials.Count>0?radials.Min():0;
            int w=480,h=270,columns=5,count=tiles.Count,rowsN=(count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rowsN,TextureFormat.RGB24,false);
            for(int i=0;i<count;i++){var tile=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rowsN-1-i/columns)*h+y,tile.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(tile);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"stir-motion.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"stir-motion.csv"),rows);
            string report="stages "+string.Join(" > ",stages)+"\nclipping "+(clip??"none")+"\nfastest tip speed near the glass "+maxSpeed.ToString("F2")+" m/s"
                +"\nspoon turns 0.9-3.2 s (counter-clockwise mouse) "+turnsCcw.ToString("F2")+" vs mouse "+mouseCcw.ToString("F2")+"\ncoast after the mouse stops: "+coastTurns.ToString("F2")+" turn, settled after "+coastSettled.ToString("F2")+" s"
                +"\nspoon turns 4.4-5.5 s (clockwise mouse) "+turnsCw.ToString("F2")+"\norbit radius: mean "+(meanRadial*1000).ToString("F1")+" mm, min "+(minRadial*1000).ToString("F1")+" mm, cavity fit "+(fullRadius*1000).ToString("F1")+" mm"
                +"\ntip below the surface by at least "+(minSubmerge*1000).ToString("F1")+" mm\nclinks while stirring "+(clinks-clinksAtLoop)+"\ncore stir intensity (peak, 0.15 s window) "+coreDuring.ToString("F2")+" for about "+(mouseCcw/2.3f).ToString("F2")+" turns/s; maxCirclesPerSecond "+c.MaxCirclesPerSecond
                +"\nangular speed variation about the glass axis (CV, same mouse): new "+cvNew.ToString("F2")+" vs old "+cvOld.ToString("F2")
                +"\nold rule: the bowl came within "+(oldMinRadial*1000).ToString("F1")+" mm of the axis"
                +"\njerk RMS of the tip (m/s^3; uneven test frame times dominate, reference only): new "+jerkNew.ToString("F0")+" vs old "+jerkOld.ToString("F0")+"\nback in the hand after release: "+(home*1000).ToString("F2")+" mm";
            File.WriteAllText(Path.Combine(HoldFramingCaptures.Output,"stir-motion.txt"),report);
            Assert.That(clip,Is.Null,"勺头和勺杆不能穿杯壁、杯底；"+report);
            Assert.That(stages.Where(x=>x!="Idle").ToArray(),Is.EqualTo(new[]{"Enter","Loop","Return"}),"先入杯，再搅，松手提出；"+report);
            Assert.That(turnsCcw,Is.InRange(mouseCcw*.75f,mouseCcw*1.2f),"勺子的圈数跟鼠标的圈数；"+report);Assert.That(turnsCw,Is.LessThan(-.8f),"鼠标反向，勺子也反向；"+report);
            Assert.That(coastTurns,Is.InRange(.02f,.6f),"停手后滑行一小段再停；"+report);Assert.That(coastSettled,Is.InRange(0f,.8f),report);
            Assert.That(minRadial,Is.GreaterThan(fullRadius*.85f),"搅拌时勺头一直贴着杯壁转，不跟着鼠标偏心；"+report);Assert.That(fullRadius,Is.GreaterThan(.015f),report);
            Assert.That(minSubmerge,Is.GreaterThan(.003f),"勺头一直在酒液里；"+report);Assert.That(clinks-clinksAtLoop,Is.GreaterThan(3),"有冰时勺子轻碰冰块；"+report);
            Assert.That(cvNew,Is.LessThan(cvOld*.5f),"同一段鼠标，新动作的转速波动不到旧写法的一半；"+report);Assert.That(maxSpeed,Is.LessThan(3f),"任何一帧都没有跳变；"+report);
            Assert.That(home,Is.LessThan(.001f),"松手后回到手上；"+report);
            Assert.That(s.Clock.Error,Is.Null);
        }
    }
}
