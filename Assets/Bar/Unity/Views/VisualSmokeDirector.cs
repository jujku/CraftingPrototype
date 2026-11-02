using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Bar.Domain;
using CraftingResearch.CoreV2;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity
{
    /// <summary>仅临时冒烟场景使用的真实核心演示；不会加入用户的 Bar 场景或订单流程。</summary>
    public sealed class VisualSmokeDirector : MonoBehaviour
    {
        public bool CaptureFrames;private BarSession session;private double elapsed,sampled;private int frame;private float fps;private string label="Loading";
        private IPourStream stream;private float poured,confirmedFlow;private float confirmedAt=-1;private ItemView tonicBottle;private Vector3 bottleHome;private Quaternion bottleRotation;private VisualSettings visual;
        private ItemView tonic,sour,shaker,spoon,scoop;private Vector3 scoopHome;private Glass tg,sg;private float start;private int nextIce;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]private static extern void VisualMetrics(double fps,double iceMs,string label);
        [DllImport("__Internal")]private static extern void BarCopyMetrics(double cpuMs);
#endif
        private string closeoutAblation="full";
        private void ConfigureCloseout()
        {
            string url=Application.absoluteURL;
            foreach(string mode in new[]{"noBottleLiquid","noCopy","noIceRefraction"})if(url.Contains("ablation="+mode))closeoutAblation=mode;
            IceRefractionFeature.ProfileWithoutCopy=closeoutAblation=="noCopy";
            Shader.SetGlobalFloat("_BarIceNoRefraction",closeoutAblation=="noIceRefraction"?1:0);
            if(closeoutAblation=="noBottleLiquid")foreach(var b in session.Views.Select(v=>v.GetComponent<BottleLiquidView>()).Where(v=>v))
            {b.enabled=false;if(b.Cavity)b.Cavity.gameObject.SetActive(false);}
        }
        private void OnDestroy(){IceRefractionFeature.ProfileWithoutCopy=false;Shader.SetGlobalFloat("_BarIceNoRefraction",0);}
        private IEnumerator Start()
        {
            yield return null;session=GetComponent<BarSession>();ConfigureCloseout();session.enabled=false;var input=FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b=>b.GetType().Name=="InputRouter");if(input)input.enabled=false;
            tonic=session.Views.First(v=>v.Item is Glass&&v.DefinitionId=="glass.highball");sour=session.Views.First(v=>v.Item is Glass&&v.DefinitionId=="glass.coupe");shaker=session.Views.First(v=>v.Item is CobblerShaker);spoon=session.Views.First(v=>v.Item is BarSpoon);tg=(Glass)tonic.Item;sg=(Glass)sour.Item;
            scoop=session.Views.First(v=>v.DefinitionId=="tool.scoop");scoopHome=scoop.transform.position;tonicBottle=session.Views.First(v=>v.DefinitionId=="bottle.tonic");bottleHome=tonicBottle.transform.position;bottleRotation=tonicBottle.transform.rotation;((Bottle)tonicBottle.Item).SetOpen(true);stream=PourStreamFactory.Create(gameObject);visual=JsonUtility.FromJson<CueSettings>(Resources.Load<CueSettingsAsset>("BarCueSettings").Json.text).visual;
            var w=session.Adapter.World;string id=((CobblerShaker)shaker.Item).Body.CoreVesselId;
            foreach(var pair in new[]{("spirit",45d),("syrup",20d),("lemon",25d)})w.Apply(new Operation{type="PourSource",to=id,ingredient=pair.Item1,ml=pair.Item2});w.Apply(new Operation{type="AddIce",to=id,grams=100});w.Apply(new Operation{type="Shake",target=id,seconds=12,intensity=.8});w.Apply(new Operation{type="StrainTransfer",from=id,to=sg.CoreVesselId});
            w.Apply(new Operation{type="PourSource",to=tg.CoreVesselId,ingredient="spirit",ml=45});session.Adapter.Advance(0);start=Time.realtimeSinceStartup;
        }
        private void Update()
        {
            if(!session||!tonic)return;float t=Time.realtimeSinceStartup-start;int phase=(int)(t/8)%4;var target=phase==1?sour:tonic;label=closeoutAblation+" | "+new[]{"Gin and tonic - clear","Gin sour - cloudy pale yellow","Adding core ice","Stirring local ice"}[phase];
            var camera=session.Player.ViewCamera;camera.transform.position=target.transform.position+new Vector3(-.12f,.14f,-.34f);camera.transform.LookAt(target.transform.position+Vector3.up*(target.Size.y*.52f));
            if(phase==2)scoop.transform.position+=tonic.transform.position+Vector3.up*(tonic.Size.y+.045f)-scoop.Anchor("HoldPoint").position;else scoop.transform.position=scoopHome;
            if(phase==2&&nextIce<3&&t%8>nextIce*1.1f){session.IceOrigins[tg.CoreVesselId]=scoop.Anchor("HoldPoint").position;session.Adapter.World.Apply(new Operation{type="AddIce",to=tg.CoreVesselId,grams=session.Adapter.World.Data.icePortionGrams});session.Adapter.Advance(0);nextIce++;}
            foreach(var rig in tonic.GetComponentsInChildren<LocalIceWorld>()){rig.PreviewSpoon=phase==3?spoon.Anchor("Tip"):null;rig.PreviewIntensity=phase==3?.8f:0;}
            if(phase==3){float a=t*4;spoon.PoseStirring(tonic,tg,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*36);}
            elapsed+=Time.unscaledDeltaTime;while(elapsed>=.1)
            {
                double timeBefore=session.Adapter.World.Time;
                if(poured<120)
                {
                    var vessel=session.Adapter.World.Vessels.First(v=>v.id==tg.CoreVesselId);double before=vessel.volume;double requested=Math.Min(3,120-poured);
                    var request=((Bottle)tonicBottle.Item).CreateLiquidRequest(tg,requested,110,t,.1,0);session.Adapter.Submit(request);
                    double actual=request.ActualMl??0;poured+=(float)actual;confirmedFlow=(float)(actual/.1);confirmedAt=Time.realtimeSinceStartup;
                }
                session.Adapter.Advance(Math.Max(0,.1-(session.Adapter.World.Time-timeBefore)));elapsed-=.1;
            }
            if(confirmedAt>=0&&Time.realtimeSinceStartup-confirmedAt<.15f&&phase==0)
            {
                tonicBottle.transform.rotation=Quaternion.Euler(0,0,110)*bottleRotation;tonicBottle.transform.position+=tonic.transform.position+new Vector3(-.06f,.23f,0)-tonicBottle.PourPoint;
                var binding=tonic.GetComponentInChildren<ContainerVisual>();var landing=tonic.PourPoint;landing.y=binding.transform.TransformPoint(Vector3.up*binding.Profile.HeightForMl(tg.Contents.LiquidMl)).y;
                float radius=PourPose.OutletRadius(tonicBottle);var velocity=PourPose.Axis(tonicBottle)*(confirmedFlow*1e-6f/(Mathf.PI*radius*radius));
                var lip=tonic.PourPoint+Vector3.up*.06f;
                if(PourTrajectory.PlaneTime(lip,velocity,tonic.PourPoint.y,PourSettings.Load().gravity,out float crossing)){var drift=PourTrajectory.Point(lip,velocity,crossing,PourSettings.Load().gravity)-tonic.PourPoint;lip-=new Vector3(drift.x,0,drift.z);}
                tonicBottle.transform.position+=lip-tonicBottle.PourPoint;
                var input=new PourStreamInput{Outlet=new Pose(tonicBottle.PourPoint,Quaternion.LookRotation(PourPose.Axis(tonicBottle))),ExitVelocity=velocity,ConfirmedMlPerSecond=confirmedFlow,LiquidPlaneY=landing.y,MouthCenter=tonic.PourPoint,MouthRadius=.027f};
                PourPose.SetImpactBoundary(ref input,PourPose.Binding(tonic));stream.Draw(input);
            }
            else{stream.Hide();tonicBottle.transform.SetPositionAndRotation(bottleHome,bottleRotation);}
            fps=Mathf.Lerp(fps,1/Mathf.Max(.001f,Time.unscaledDeltaTime),.08f);
            if(t-sampled>1){sampled=t;double ms=FindObjectsByType<LocalIceWorld>(FindObjectsSortMode.None).Sum(r=>r.StepMilliseconds);
#if UNITY_WEBGL && !UNITY_EDITOR
                VisualMetrics(fps,ms,label);
                BarCopyMetrics(IceRefractionFeature.CopyCpuMilliseconds);
#endif
            }
        }
        private void OnGUI(){GUI.color=Color.white;GUI.Label(new Rect(12,12,600,24),label+" | "+fps.ToString("F1")+" fps | local ice "+FindObjectsByType<LocalIceWorld>(FindObjectsSortMode.None).Sum(r=>r.StepMilliseconds).ToString("F3")+" ms");}
        private void LateUpdate()
        {
            if(!CaptureFrames||!session||!tonic)return;float t=Time.realtimeSinceStartup-start;if(t>32||t*10<frame)return;frame++;
            string dir=Path.Combine(Application.dataPath,"../VisualReports/frames/"+Mathf.Min(3,(int)(t/8)));Directory.CreateDirectory(dir);var rt=RenderTexture.GetTemporary(640,480,24);RenderPipeline.SubmitRenderRequest(session.Player.ViewCamera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(640,480,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,640,480),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(dir,frame.ToString("D5")+".png"),tex.EncodeToPNG());Destroy(tex);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
