using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Bar.Data;
using Bar.Domain;
using CraftingResearch.CoreV2;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只挂在临时 B 冒烟场景；真实核心参考输入与可独立切换的显示性能条件。</summary>
    public sealed class BStageDemo:MonoBehaviour
    {
        public bool Ready {get;private set;}public string Mode="sealed";public bool AutoRun=true;
        private BarSession session;private ItemView shaker,glass,strainer,cap;private CobblerShaker model;private LocalIceWorld ice;private ShakePresentation pose;
        private Vector3 home,glassHome;private Quaternion rotation,glassRotation;private float start,sampled,frameTotal,modelTime;private int frames;private string previousMode;
        private readonly List<float> costs=new List<float>();public Stats Last {get;private set;}
        [Serializable]public sealed class Stats {public string mode,ablation;public float seconds,fps,iceMedianMs,iceP95Ms;public int proxies,simulated,impacts,bubbles,soundVoices;public float frost,mist;public bool audioUnlocked,legacySkipReference;}
        #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]private static extern void BarBMetrics(string json);
        #endif
        private IEnumerator Start()
        {
            gameObject.name="BStageDemo";yield return null;yield return null;session=GetComponent<BarSession>();session.enabled=false;
            foreach(var input in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(input is PlayerController||input.GetType().Name=="InputRouter")input.enabled=false;
            shaker=session.Views.First(v=>v.Item is CobblerShaker);model=(CobblerShaker)shaker.Item;glass=session.Views.First(v=>v.DefinitionId=="glass.highball");
            strainer=session.Views.First(v=>v.Item is ShakerStrainer);cap=session.Views.First(v=>v.Item is ShakerCap);ice=shaker.GetComponentInChildren<LocalIceWorld>();ice.enabled=false;
            home=shaker.transform.position;rotation=shaker.transform.rotation;glassHome=glass.transform.position;glassRotation=glass.transform.rotation;pose=new ShakePresentation();
            var world=session.Adapter.World;foreach(var pair in new[]{("spirit",45d),("syrup",20d),("lemon",25d)})world.Apply(new Operation{type="PourSource",to=model.Body.CoreVesselId,ingredient=pair.Item1,ml=pair.Item2});
            world.Apply(new Operation{type="AddIce",to=model.Body.CoreVesselId,grams=160});world.Apply(new Operation{type="Shake",target=model.Body.CoreVesselId,seconds=12,intensity=.8});
            var target=(Glass)glass.Item;world.Apply(new Operation{type="PourSource",to=target.CoreVesselId,ingredient="spirit",ml=45});world.Apply(new Operation{type="PourSource",to=target.CoreVesselId,ingredient="tonic",ml=120});world.Apply(new Operation{type="AddIce",to=target.CoreVesselId,grams=160});session.Adapter.Advance(15);
            var core=world.Vessels.First(v=>v.id==model.Body.CoreVesselId);if(Math.Ceiling(core.ice/world.Data.icePortionGrams)<ice.Capacity)world.Apply(new Operation{type="AddIce",to=core.id,grams=world.Data.icePortionGrams});session.Adapter.Advance(0);
            session.Hands.TryPickUp(model);SetAssembly(true);ConfigureAblation();start=Time.realtimeSinceStartup;Ready=true;
            foreach(string mode in new[]{"sealed","debug","open","legacy","bubbles","condensation","frost","auto"})if(Application.absoluteURL.Contains("bmode="+mode))Mode=mode;
        }
        public void SetMode(string mode){Mode=mode;previousMode=null;costs.Clear();frameTotal=0;frames=0;}
        public void UnlockAudio(){if(session){session.Sound.Gate.Unlock();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
        private void SetAssembly(bool closed)
        {
            if(closed){if(model.StrainerSlot.IsEmpty)model.Attach((ShakerStrainer)strainer.Item);if(model.CapSlot.IsEmpty)model.Attach((ShakerCap)cap.Item);}
            else{if(!model.CapSlot.IsEmpty)model.Detach(model.CapSlot.Content);if(!model.StrainerSlot.IsEmpty)model.Detach(model.StrainerSlot.Content);}
            foreach(var part in new[]{strainer,cap})
            {if(part.Item.Parent==model){var slot=shaker.Anchor(part.Item.Location.Anchor);part.transform.SetParent(slot,false);var scale=slot.lossyScale;part.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;}else{part.transform.SetParent(null,true);part.transform.position=home+Vector3.right*.4f;}}
        }
        private string ablation="full";
        private void ConfigureAblation()
        {
            foreach(string name in new[]{"noBottleLiquid","noCopy","noIceRefraction"})if(Application.absoluteURL.Contains("ablation="+name))ablation=name;
            IceRefractionFeature.ProfileWithoutCopy=ablation=="noCopy";Shader.SetGlobalFloat("_BarIceNoRefraction",ablation=="noIceRefraction"?1:0);
            if(ablation=="noBottleLiquid")foreach(var bottle in session.Views.Select(v=>v.GetComponent<BottleLiquidView>()).Where(b=>b)){bottle.enabled=false;if(bottle.Cavity)bottle.Cavity.gameObject.SetActive(false);}
        }
        private void Update(){if(Ready&&AutoRun)Step(Time.unscaledDeltaTime,Time.realtimeSinceStartup-start);}
        public void Step(float dt,float time)
        {
            if(!Ready)return;string mode=Mode=="auto"?new[]{"sealed","debug","open","bubbles","condensation","frost"}[(int)(time/6)%6]:Mode;
            if(mode!=previousMode){SetAssembly(mode!="open");previousMode=mode;costs.Clear();frameTotal=0;frames=0;}
            var camera=session.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;
            bool close=mode=="sealed"||mode=="debug"||mode=="legacy"||mode=="open";bool debug=mode=="debug";
            if(session.DebugVisible!=debug)session.ToggleDebug();
            glass.transform.SetPositionAndRotation(glassHome,glassRotation);
            if(close)
            {
                camera.transform.position=new Vector3(.15f,1.35f,-.8f);camera.transform.rotation=Quaternion.Euler(10,0,0);
                shaker.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(.05f,-.13f,.6f)),rotation);pose.Apply(session,shaker,true,Math.Sin(time*6*Math.PI)*60,dt);ice.PreviewIntensity=1;
            }
            else
            {
                shaker.transform.SetPositionAndRotation(home,rotation);ice.PreviewIntensity=0;
                var target=mode=="frost"?shaker:glass;
                camera.transform.position=target.transform.position+new Vector3(.08f,.18f,-.36f);camera.transform.LookAt(target.transform.position+Vector3.up*(mode=="frost"?.105f:.075f));
                if(mode=="condensation"&&!session.Hands.SideOf(glass.Item).HasValue)session.Hands.TryPickUp(glass.Item);
            }
            modelTime+=dt;while(modelTime>=.1f){session.Adapter.Advance(.1);modelTime-=.1f;}
            if(mode!="legacy")ice.Advance(dt);else foreach(var root in ice.Displays)root.gameObject.SetActive(false);
            costs.Add(mode=="legacy"?0:(float)ice.StepMilliseconds);if(costs.Count>240)costs.RemoveAt(0);frameTotal+=dt;frames++;
            if(time-sampled>=.5f)
            {
                sampled=time;var ordered=costs.OrderBy(v=>v).ToArray();var state=session.Cues.SurfaceFor(mode=="frost"?model.Body.CoreVesselId:((Glass)glass.Item).CoreVesselId);
                Last=new Stats{mode=mode,ablation=ablation,seconds=time,fps=frames/Mathf.Max(.001f,frameTotal),iceMedianMs=ordered[ordered.Length/2],iceP95Ms=ordered[Mathf.Min(ordered.Length-1,(int)(ordered.Length*.95f))],proxies=ice.DisplayCount,simulated=mode=="legacy"?0:ice.DisplayCount,impacts=ice.Impacts.Count,bubbles=glass.GetComponentInChildren<BubbleStreams>().ActiveCount,soundVoices=session.Sound.Pool.ActiveCount,frost=state?.Frost??0,mist=state?.Mist??0,audioUnlocked=session.Sound.Gate.Unlocked,legacySkipReference=mode=="legacy"};
                #if UNITY_WEBGL && !UNITY_EDITOR
                BarBMetrics(JsonUtility.ToJson(Last));
                #endif
            }
        }
        private void OnGUI(){if(Ready&&Last!=null)GUI.Label(new Rect(12,12,700,28),"B / "+Last.mode+" / "+Last.fps.ToString("F1")+" FPS / ice P95 "+Last.iceP95Ms.ToString("F3")+" ms");}
        private void OnDestroy(){IceRefractionFeature.ProfileWithoutCopy=false;Shader.SetGlobalFloat("_BarIceNoRefraction",0);}
    }
}
