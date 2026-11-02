using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>在呈现帧末应用快照线索并发布同一份显示值；所有材质实例只存在于运行时。</summary>
    [DefaultExecutionOrder(100)] public sealed class CuePresenter : MonoBehaviour
    {
        private sealed class Binding
        {
            internal LiquidMotion Motion=new LiquidMotion();internal ItemView View;internal ContainerVisual Visual;internal Container Container;
            internal Material OriginalLiquid, Liquid;internal MeshRenderer[] Shells;internal bool[] ShellEnabled;
            internal MeshRenderer[] Overlay;internal BubbleStreams Bubbles;internal double LastVolume;
            internal SurfaceAppearance Surface;internal CraftingResearch.CoreV2.Vessel Core;internal PresentationSnapshot Snapshot=new PresentationSnapshot();internal LiquidSurface LiquidSurface;internal IceVisual Ice;internal MaterialPropertyBlock Block=new MaterialPropertyBlock();
            internal CueFrame Last;
        }
        private readonly List<Binding> bindings=new List<Binding>();
        private readonly Plane[] planes=new Plane[6];private readonly RaycastHit[] hits=new RaycastHit[64];private CueFrame[] output;private FoamPresentation foam;private BarSession session;private CueSettings settings;private CueEvaluator evaluator;private CueSampler sampler;
        public bool DebugSeeInside=true;
        public IReadOnlyList<CueFrame> Current=>output??Array.Empty<CueFrame>();
        public SurfaceEvolution SurfaceFor(string vesselId){foreach(var b in bindings)if(b.Container.CoreVesselId==vesselId)return b.Surface.State;return null;}
        /// <summary>为场景已有容器创建运行时线索。</summary>
        /// <precondition>物品和核心均已绑定；配置有效。</precondition>
        /// <effects>创建临时渲染与声音对象，不保存 Art。</effects>
        /// <summary>V2 擦干：收到 shaker.wipe 的 action_performed 后擦掉该壶外壁的雾、霜和水。</summary>
        private void OnDomainEvent(DomainEvent e)
        {
            if(e.Kind!="action_performed"||e.Observation is not LogRecord r||r.actionId!="shaker.wipe")return;
            foreach(var b in bindings)if(b.View.InstanceId==r.targetId&&b.Container is ShakerBody)b.Surface?.State.WipeDry();
        }
        /// <summary>壶外壁上是否有可擦的雾、霜或水；菜单只在有东西可擦时给出“擦干”。</summary>
        public bool HasSurfaceWater(Item item)
        {
            foreach(var b in bindings)if(b.View.Item==item&&b.Container is ShakerBody&&b.Surface!=null){var state=b.Surface.State;if(state.Mist+state.Frost+state.Wet>.06f)return true;}
            return false;
        }
        public void Initialize(BarSession session,CueSettings settings)
        {
            this.session=session;this.settings=settings;evaluator=new CueEvaluator(settings);sampler=new CueSampler{Interval=settings.sampleInterval,Threshold=settings.sampleThreshold,SampleSoundsOnTick=true};
            foam=new FoamPresentation(session.Adapter,BPresentationSettings.Load().foam);
            session.Adapter.Events.Published+=OnDomainEvent;
            // Runtime synthesis removed; sample playback is reserved for milestone B.
            foreach(var view in session.Views)
            foreach(var visual in view.GetComponentsInChildren<ContainerVisual>(true))
            {
                var c=view.Item is CobblerShaker sh?sh.Body:view.Item is Jigger j?(visual.DefinitionId==j.CupA.Definition.Id?j.CupA:j.CupB):view.Item as Container;
                if(c==null||!visual.LiquidVolume)continue;
                var b=new Binding{View=view,Visual=visual,Container=c,OriginalLiquid=visual.LiquidVolume.sharedMaterial};
                b.Liquid=new Material(Shader.Find("Bar/CueLiquid"));visual.LiquidVolume.sharedMaterial=b.Liquid;
                b.Shells=view.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponentInParent<ItemView>()==view&&r!=visual.LiquidVolume&&r.name!="InnerCavity"&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.enabled).ToArray();
                b.Visual.LiquidVolume.gameObject.layer=31;b.Liquid.renderQueue=2498;
                if(c is Glass)foreach(var shell in b.Shells){var materials=shell.materials;foreach(var mat in materials)mat.renderQueue=mat.shader.name=="Bar/Glass"&&mat.GetFloat("_Cull")<1.5f?3020:3030;}
                var localIce=visual.gameObject.AddComponent<LocalIceWorld>();localIce.Initialize(session,view,c,visual,settings.visual);
                b.Bubbles=visual.gameObject.AddComponent<BubbleStreams>();b.Bubbles.Initialize(visual.transform,visual.Shape,localIce);
                b.ShellEnabled=b.Shells.Select(r=>r.enabled).ToArray();
                b.Overlay=Array.Empty<MeshRenderer>();b.Surface=new SurfaceAppearance(view,visual,c,b.Shells);
                b.Core=session.Adapter.World.Vessels.Single(v=>v.id==c.CoreVesselId);b.LiquidSurface=visual.GetComponent<LiquidSurface>();if(!b.LiquidSurface)b.LiquidSurface=visual.gameObject.AddComponent<LiquidSurface>();b.Ice=visual.GetComponent<IceVisual>();b.Last=new CueFrame{liquidRgba=new double[4]};bindings.Add(b);
            }
            output=new CueFrame[bindings.Count];for(int i=0;i<bindings.Count;i++)output[i]=bindings[i].Last;
        }
        private void LateUpdate(){if(session&&session.Adapter!=null)Present();}
        /// <summary>呈现并发布每容器本帧线索。</summary>
        /// <precondition>已 Initialize，且在 ItemView 更新后调用。</precondition>
        /// <effects>只改运行时 Renderer、气泡和 AudioSource；发布领域观察事件。</effects>
        public void Present()=>Present(Time.unscaledDeltaTime);
        public void Present(float deltaTime)
        {
            if(session.Sound)session.Sound.Present();
            GeometryUtility.CalculateFrustumPlanes(session.Player.ViewCamera,planes);
            foreach(var b in bindings)
            {
                var core=b.Core;var snapshot=b.Snapshot.Read(core,session.Adapter.World.Data.ethanolDensity);
                bool active=b.View.gameObject.activeInHierarchy&&(!(b.View.Item is Jigger j)||ReferenceEquals(j.ActiveCup,b.Container));
                bool seeInside=b.Container is ShakerBody&&session.DebugVisible&&DebugSeeInside;
                bool sealedShaker=b.View.Item is CobblerShaker shaker&&shaker.State==ShakerState.Sealed;
                bool onScreen=active&&GeometryUtility.TestPlanesAABB(planes,b.Visual.LiquidVolume.bounds)&&!Occluded(b);
                var cue=evaluator.Evaluate(snapshot,core.kind,session.Adapter.World.Room,session.Adapter.Agitation(core.id,settings.soundHoldSeconds),session.Adapter.World.Data.icePortionGrams,sealedShaker,seeInside,onScreen,Time.frameCount,b.Last);
                // An opaque steel shell and its lid remain opaque in normal mode. Debug hides only runtime renderers.
                for(int i=0;i<b.Shells.Length;i++)if(b.Shells[i])b.Shells[i].enabled=b.ShellEnabled[i]&&!seeInside;
                bool showLiquid=active&&snapshot.volumeMl>1e-8&&(!sealedShaker||seeInside);
                b.Visual.LiquidVolume.enabled=showLiquid;
                if(snapshot.optical!=null){var o=snapshot.optical;double r=Math.Exp(-o[0]*5),g=Math.Exp(-o[1]*5),blue=Math.Exp(-o[2]*5);cue.liquidRgba[0]=r;cue.liquidRgba[1]=g;cue.liquidRgba[2]=blue;cue.liquidRgba[3]=1-(r+g+blue)/3*(1-Math.Min(1,o[3]));}
                float localHeight=b.Visual.Profile?b.Visual.HeightForLiquid(snapshot.volumeMl):0;
                var color=new Color((float)cue.liquidRgba[0],(float)cue.liquidRgba[1],(float)cue.liquidRgba[2],(float)cue.liquidRgba[3]);b.Liquid.SetColor(BShaderIds.BaseColor,color);b.Motion.Apply(b.Liquid,b.Visual.transform,snapshot,settings.visual,deltaTime,(float)session.Adapter.Agitation(core.id,.15),InspectionPolicy.LiquidWaveLimit(session,b.View,b.Visual,localHeight));
                var block=b.Block;b.Visual.LiquidVolume.GetPropertyBlock(block);block.SetColor(BShaderIds.BaseColor,color);b.Visual.LiquidVolume.SetPropertyBlock(block);
                float height=b.Visual.Profile?b.Visual.transform.TransformPoint(Vector3.up*localHeight).y:0;
                b.Liquid.SetFloat(BShaderIds.FillHeight,height);block.SetFloat(BShaderIds.FillHeight,height);b.Visual.LiquidVolume.SetPropertyBlock(block);
                var hand=session.Hands.SideOf(b.View.Item);var grip=hand.HasValue?b.View.Anchor(hand.Value==Bar.Data.HandSide.Left?"Grip_L":"Grip_R"):null;
                if(hand.HasValue&&!grip)grip=b.View.Anchor(hand.Value==Bar.Data.HandSide.Left?"Grip_R":"Grip_L");
                b.Surface.Present(snapshot,height,grip,deltaTime,(float)session.Adapter.Agitation(b.Container.CoreVesselId,.15));
                cue.frost=onScreen&&!seeInside&&b.Container is ShakerBody?b.Surface.State.Frost:0;
                cue.condensation=onScreen&&b.Container is Glass?b.Surface.State.Mist:0;
                var surface=b.LiquidSurface;if(surface&&b.Visual.InnerCavity)surface.Draw(b.Visual.InnerCavity,height,b.Liquid,showLiquid,b.Motion.Normal);
                if(sealedShaker&&!seeInside&&b.Ice)b.Ice.Show(0,.01f,Vector3.zero,.1f,.1f);
                cue.foam=cue.liquidVisible?foam.Height(core.id,snapshot.optical?[3]??0):0;b.Liquid.SetFloat(BShaderIds.FoamHeight,(float)cue.foam);b.Liquid.SetColor(BShaderIds.FoamColor,new Color(.94f,.92f,.85f));
                foreach(var layer in b.Overlay)
                {
                    layer.enabled=active&&!seeInside&&(cue.frost>0||cue.condensation>0);
                    var mpb=b.Block;mpb.SetFloat(BShaderIds.Kind,b.Container is Glass?1:0);mpb.SetFloat(BShaderIds.Strength,(float)(b.Container is Glass?cue.condensation*settings.condensationOpacity:cue.frost*settings.frostOpacity));layer.SetPropertyBlock(mpb);
                }
                float activity=Mathf.Max((float)session.Adapter.Agitation(core.id,settings.soundHoldSeconds),snapshot.volumeMl>b.LastVolume+.1?1:0);b.LastVolume=snapshot.volumeMl;
                b.Bubbles.Present((float)(snapshot.co2GL??0),(float)(snapshot.temperature??session.Adapter.World.Room),height,b.Motion.Normal,activity,cue.liquidVisible,deltaTime,b.Liquid);
                cue.bubbleCount=cue.liquidVisible?b.Bubbles.ActiveCount:0;
                cue.soundPlaying=session.Sound&&session.Sound.Pool.Playing(core.id);cue.soundEmitted=session.Sound&&session.Sound.Emitted(core.id);cue.iceSound=session.Sound?session.Sound.Pool.Gain(core.id,true):0;
                b.Last=cue;
            }
            session.Adapter.CueFrameNumber=Time.frameCount;session.Adapter.DisplayedCues=output;if(sampler.Sample(session.Adapter.World.Time,output))session.Adapter.Publish("cue_frame",new LogRecord());
            var statistics=sampler.FrameStatistics(session.Adapter.World.Time,deltaTime);if(statistics!=null)session.Adapter.Publish("frame_statistics",statistics);
        }
        private bool Occluded(Binding b)
        {
            if(session.Hands.SideOf(b.View.Item).HasValue||session.PourDisplay?.Presentation?.DisplayReceiver==b.View)return false;
            var origin=session.Player.ViewCamera.transform.position;var delta=b.Visual.LiquidVolume.bounds.center-origin;
            int count=Physics.RaycastNonAlloc(origin,delta.normalized,hits,delta.magnitude);float nearest=float.MaxValue;bool occluded=false;
            for(int i=0;i<count;i++){var hit=hits[i];if(hit.distance>=nearest)continue;var view=hit.collider.GetComponentInParent<ItemView>();if(!view)continue;nearest=hit.distance;occluded=view!=b.View&&!(view.Item!=null&&view.Item.Parent==b.View.Item);}
            return occluded;
        }
        private void OnDestroy()
        {
            foam?.Dispose();foreach(var b in bindings){b.Surface?.Dispose();if(b.Visual&&b.Visual.LiquidVolume)b.Visual.LiquidVolume.sharedMaterial=b.OriginalLiquid;for(int i=0;i<b.Shells.Length;i++)if(b.Shells[i])b.Shells[i].enabled=b.ShellEnabled[i];foreach(var r in b.Overlay)if(r)Destroy(r.gameObject);if(b.Bubbles)Destroy(b.Bubbles);if(b.Liquid)Destroy(b.Liquid);}
        }
    }
}
