using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// 新摇壶动作（起壶上抛 720° → 接住 → 按设计轨迹摇 → 松手回位）的验收截图和数据。
    /// 用一段不规则、带抖动的鼠标往复（像真人手）驱动，记录壶在相机里的位置；同一段鼠标输入按旧算法（壶位置直接等于鼠标相位）重算一遍，
    /// 比较两者的急动度（加速度的变化率），说明新动作不再随鼠标抖。核心照旧由鼠标往复决定强度。
    /// </summary>
    public sealed class ShakeMotionCaptures
    {
        private BarSession s;private readonly List<Texture2D> tiles=new List<Texture2D>();
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private void Aim(string id){var view=View(id);s.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view),"真实射线目标 "+id);}
        private IEnumerator Pick(string id){yield return View(id).Item is Bottle?CapSteps.Uncap(s,View(id)):null;Aim(id);s.RightButton(Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Hands.SideOf(View(id).Item).HasValue,Is.True,"拿取 "+id+"；"+s.Clock.Error);}
        private void Choose(string action,string actor,string target)
        {
            if(s.Hands.SideOf(View(target).Item).HasValue){s.Player.AimAt(CapSteps.TablePoint(s,.9f,-.25f));Physics.SyncTransforms();s.Targeting();}else{var view=View(target);s.Player.AimAt(view.Item is Fixture&&view.Anchor("Surface")?view.Anchor("Surface").position:view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();}
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
        private void Shot(string name){s.GetComponent<HeldItemRendering>().Refresh();tiles.Add(HoldFramingCaptures.Capture(s.Player.ViewCamera,"shake-"+name));}
        private static string F(float v)=>v.ToString("F4",CultureInfo.InvariantCulture);
        /// <summary>按三阶差分求急动度的均方根（米/秒³），dt 取各帧实际间隔。</summary>
        private static float Jerk(List<Vector3> p,List<float> dt)
        {
            double sum=0;int n=0;
            for(int i=3;i<p.Count;i++){float h=(dt[i]+dt[i-1]+dt[i-2])/3;if(h<=0)continue;var j=(p[i]-3*p[i-1]+3*p[i-2]-p[i-3])/(h*h*h);sum+=j.sqrMagnitude;n++;}
            return n>0?(float)System.Math.Sqrt(sum/n):0;
        }
        [UnityTest] public IEnumerator TossCatchAndDesignedStrokeFollowAnIrregularMouse()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;s.Player.ViewCamera.aspect=16f/9;
            yield return Pick("jigger.dual");yield return Pick("bottle.gin");
            Choose("pour.bottle_to_container","bottle.gin","jigger.dual");s.PointerDelta(new Vector2(0,1000),Now()+.001);yield return new WaitForSecondsRealtime(1f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.6f);
            yield return CapSteps.PutDown(s,View("bottle.gin"));yield return Click("pour.jigger_to_container","jigger.dual","shaker.cobbler");yield return CapSteps.PutDown(s,View("jigger.dual"));
            yield return Pick("tool.scoop");yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","shaker.cobbler");
            yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","shaker.cobbler");yield return CapSteps.PutDown(s,View("tool.scoop"));
            yield return Pick("shaker.strainer");yield return Click("shaker.attach_strainer","shaker.strainer","shaker.cobbler");yield return Pick("shaker.cap");yield return Click("shaker.attach_cap","shaker.cap","shaker.cobbler");
            yield return Pick("shaker.cobbler");yield return new WaitForSecondsRealtime(.8f);Shot("0-held");
            var shaker=View("shaker.cobbler");var camera=s.Player.ViewCamera;var body=((CobblerShaker)shaker.Item).Body.CoreVesselId;
            var renderers=shaker.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
            Vector3 Pivot(){var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return camera.transform.InverseTransformPoint(b.center);}
            var restPivot=Pivot();
            // An uneven human stroke: the rate drifts between about 1.3 and 2.2 strokes a second, the reach varies, and every frame has hand tremor.
            var random=new System.Random(7);float previous=0,phase=0,worst=1;double gesture=0;var stages=new List<string>();var marks=new[]{.05f,.2f,.32f,.45f,.62f,.9f,1.6f,2.3f,3.1f,4.4f,5.6f};int next=0;
            var newPath=new List<Vector3>();var oldPath=new List<Vector3>();var steps=new List<float>();var rows=new List<string>{"t,stage,gestureY,phase,hz,amplitude,x,y,z,oldAlong,slams,coreIntensity"};
            int reversalsAtLoop=-1,slamsAtLoop=-1;float loopStart=-1;var slamTimes=new List<float>();var reversalTimes=new List<float>();int lastSlams=0,lastReversals=0;
            Choose("shaker.shake","shaker.cobbler","shaker.cobbler");float start=Time.realtimeSinceStartup;var pose=s.ShakeDisplay;
            while(Time.realtimeSinceStartup-start<6.5f)
            {
                float t=Time.realtimeSinceStartup-start,dt=Time.unscaledDeltaTime;
                float rate=1.75f+.45f*Mathf.Sin(t*1.3f);phase+=dt*2*Mathf.PI*rate;float reach=140+40*Mathf.Sin(t*.7f+1);
                float target=Mathf.Sin(phase)*reach+(float)(random.NextDouble()*2-1)*22;
                s.PointerDelta(new Vector2(0,target-previous),Now());gesture+=target-previous;previous=target;yield return null;
                pose=s.ShakeDisplay;if(pose==null)continue;
                if(stages.Count==0||stages[stages.Count-1]!=pose.StageName)stages.Add(pose.StageName);
                if(pose.StageName=="Loop"&&loopStart<0){loopStart=t;reversalsAtLoop=pose.ReversalSequence;slamsAtLoop=pose.SlamSequence;}
                if(pose.SlamSequence!=lastSlams){slamTimes.Add(t);lastSlams=pose.SlamSequence;}if(pose.ReversalSequence!=lastReversals){reversalTimes.Add(t);lastReversals=pose.ReversalSequence;}
                var pivot=Pivot();
                // The old display: the tin's position along its tilted axis was the mouse phase itself (180 px a cycle, 10 cm travel, 1.2 cm arc).
                float oldPhase=(float)gesture*2*Mathf.PI/180;var oldAxis=Quaternion.AngleAxis(40,Vector3.forward)*Vector3.up;var old=restPivot+oldAxis*(Mathf.Sin(oldPhase)*.05f)+Vector3.right*((1-Mathf.Cos(oldPhase))*.012f);
                if(pose.StageName=="Loop"&&t-loopStart>.6f){newPath.Add(pivot);oldPath.Add(old);steps.Add(dt);worst=Mathf.Min(worst,HoldFramingCaptures.Measure(camera,shaker).Visible);}
                rows.Add(string.Join(",",F(t),pose.StageName,F((float)gesture),F(pose.Phase),F(pose.Frequency),F(pose.Amplitude),F(pivot.x),F(pivot.y),F(pivot.z),F(Mathf.Sin(oldPhase)*.05f),pose.SlamSequence.ToString(),F((float)s.Adapter.Agitation(body,.15))));
                if(next<marks.Length&&t>=marks[next])Shot((next++ +1)+"-"+pose.StageName.ToLowerInvariant());
            }
            int catches=pose.CatchSequence;
            s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.15f);Shot("12-release");yield return new WaitForSecondsRealtime(2.6f);Shot("13-after");
            float home=(Pivot()-restPivot).magnitude;
            float jerkNew=Jerk(newPath,steps),jerkOld=Jerk(oldPath,steps);
            float Cv(List<float> times,float from){var gaps=new List<float>();for(int i=1;i<times.Count;i++)if(times[i-1]>=from)gaps.Add(times[i]-times[i-1]);if(gaps.Count<3)return -1;float mean=gaps.Average();return Mathf.Sqrt(gaps.Select(g=>(g-mean)*(g-mean)).Average())/mean;}
            float cvSlam=Cv(slamTimes,loopStart+.6f),cvReversal=Cv(reversalTimes,loopStart+.6f);
            int w=480,h=270,columns=4,count=tiles.Count,rowsN=(count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rowsN,TextureFormat.RGB24,false);
            for(int i=0;i<count;i++){var tile=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rowsN-1-i/columns)*h+y,tile.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(tile);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"shake-motion.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"shake-motion.csv"),rows);
            string report="stages "+string.Join(" > ",stages)+"\ncatches "+catches+"\nloop starts at "+loopStart.ToString("F2")+" s\nmouse reversals in loop "+(pose.ReversalSequence-reversalsAtLoop)+", ice slams in loop "+(pose.SlamSequence-slamsAtLoop)
                +"\njerk RMS (m/s^3, same mouse input): new "+jerkNew.ToString("F0")+" vs old "+jerkOld.ToString("F0")+"\ninterval variation (CV): ice slams "+cvSlam.ToString("F2")+" vs mouse reversals "+cvReversal.ToString("F2")
                +"\nworst visible fraction in the stroke "+worst.ToString("F3")+"\nback to the hold after release: "+(home*100).ToString("F2")+" cm";
            File.WriteAllText(Path.Combine(HoldFramingCaptures.Output,"shake-motion.txt"),report);
            Assert.That(stages.Where(x=>x!="Idle").Take(4),Is.EqualTo(new[]{"Windup","Toss","Chamber","Loop"}),"起壶：蓄力 → 上抛 → 接住 → 摇；"+report);Assert.That(catches,Is.EqualTo(1));
            Assert.That(pose.SlamSequence-slamsAtLoop,Is.GreaterThan(10),"摇壶时冰块按轨迹撞壁");Assert.That(jerkNew,Is.LessThan(jerkOld*.5f),"同样的鼠标输入，新动作的急动度不到旧算法的一半；"+report);
            Assert.That(worst,Is.GreaterThan(.7f),"摇壶时壶身大部分在画面内");Assert.That(home,Is.LessThan(.02f),"松手后回到持握位");
        }
    }
}
