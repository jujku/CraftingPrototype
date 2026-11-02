using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using System.IO;
using CraftingResearch.CoreV2;
using UnityEngine;

namespace Bar.Unity
{
    /// <summary>协调固定视角双手、悬停菜单、右键拿放及交给唯一核心的倒料输入。</summary>
    public sealed class BarSession : MonoBehaviour
    {
        /// <summary>冻结来源可追溯的核心数据资源。</summary>
        public TextAsset ModelJson;
        /// <summary>物品定义资源。</summary>
        public TextAsset ItemsJson;
        /// <summary>控制参数资源。</summary>
        public TextAsset ControlsJson;
        /// <summary>场景内的第一人称控制器。</summary>
        public PlayerController Player;
        /// <summary>领域双手状态。</summary>
        public Hands Hands { get; private set; }
        /// <summary>唯一物理世界适配器。</summary>
        public WorldAdapter Adapter { get; private set; }
        /// <summary>固定外部节拍调度器。</summary>
        public SimulationClock Clock { get; private set; }
        /// <summary>准星命中的场景物品；固定在屏幕下方的持物不会挡住射线。</summary>
        public ItemView Target { get; private set; }
        /// <summary>只有执行中的动作锁视角，悬停菜单不锁视角。</summary>
        public bool LookLocked => IceDeliveryBusy || preparingPour!=null || pourIntent || Clock != null && Hands.IsBusy;
        public bool StationShuffleAllowed
        {
            get{var active=Clock.Active;return !IceDeliveryBusy&&preparingPour==null&&!actionQueued&&active!=null&&!active.IsEnded&&active.Target!=null&&active.Target.Location.Kind==LocationKind.Surface&&(active.Action.InputStyle==InputStyle.HoldTilt||active.Action.InputStyle==InputStyle.HoldCircle);}
        }
        public void LeaveStation(ActiveAction expected,double time)
        {
            Clock.Enqueue(time,c=>{if(!ReferenceEquals(c.Active,expected)||expected.IsEnded)return;pourIntent=false;CancelPourPreparation();c.Stop(true);Adapter.Publish("view_locomotion_action_cancelled",new LogRecord{actionId=expected.Action.Id,actorId=expected.Actor.InstanceId,targetId=expected.Target.InstanceId,reason="left_station",inputTimeSeconds=time});});
        }
        /// <summary>准星物品的菜单或双手动作菜单是否可见。</summary>
        public bool MenuOpen => !IceDeliveryBusy && !inputSuspended && !pourIntent && !actionQueued && !Hands.IsBusy && (menu.Count>0 || Target && Target.Item is not BarCounter);
        /// <summary>当前悬停菜单的只读动作候选；不包含拿起和放下。</summary>
        private IReadOnlyList<ActionOption> menuView;
        public IReadOnlyList<ActionOption> MenuOptions => menuView??=menu.AsReadOnly();
        public int SelectedMenuIndex=>menuIndex;
        public string SecondaryHint {get;private set;}
        public Vector3 HudTargetPoint=>Target&&Target.Item is not BarCounter?Target.transform.position+Target.transform.up*Target.Size.y*.5f:hitPoint;
        public bool HudHolding=>pourIntent;
        public float HudPressFraction=>(float)press;
        public bool HudSuspended=>inputSuspended;
        public SpeakeasyHud Hud {get;private set;}
        private ItemView menuTarget;
        /// <summary>已接受倒酒输入或核心正在消费倒酒动作。</summary>
        /// <summary>正在准备或进行中的倒入的接收物（供观察镜头只读）；没有则为空。</summary>
        public Item PourTarget => preparingPour!=null?preparingPour.Target:Clock?.Active!=null&&!Clock.Active.IsEnded&&Clock.Active.Action.InputStyle==InputStyle.HoldTilt?Clock.Active.Target:null;
        public bool PourMode => pourIntent || Clock?.Active != null && !Clock.Active.IsEnded && Clock.Active.Action.InputStyle==InputStyle.HoldTilt;
        /// <summary>F3 调试面板状态，初始关闭。</summary>
        public bool DebugVisible { get; private set; }
        public SystemStatePanel DebugPanel { get; private set; }
        /// <summary>场景实例视图的只读集合。</summary>
        public IEnumerable<ItemView> Views => views.Values;
        private readonly Dictionary<string,ItemView> views = new Dictionary<string,ItemView>();
        private readonly Dictionary<string,Vector3> placements = new Dictionary<string,Vector3>();
        private readonly Dictionary<string,Quaternion> placementRotations = new Dictionary<string,Quaternion>();
        // Only a hand/slot-to-surface transition owns the root pose. Idle authored poses remain editable.
        private readonly HashSet<string> returningToSurface = new HashSet<string>();
        private readonly List<ActionOption> menu = new List<ActionOption>();
        public readonly Dictionary<string,Vector3> IceOrigins=new Dictionary<string,Vector3>();
        public readonly Dictionary<string,IceDeliveryBatch> IceDeliveries=new Dictionary<string,IceDeliveryBatch>();
        private IceDeliveryMotion iceDelivery;
        private ActionOption preparingIce;
        private int icePreparationVersion;
        private float iceFallSeconds;
        public bool IceDeliveryBusy=>iceDelivery?.Busy==true;
        public IceDeliveryMotion IceDeliveryDisplay=>iceDelivery;
        private void CompleteIcePreparation(double time)
        {
            if(preparingIce==null||iceDelivery?.Ready!=true)return;
            var option=preparingIce;preparingIce=null;int version=icePreparationVersion;
            Clock.Enqueue(time,c=>
            {
                if(version!=icePreparationVersion)return;
                try
                {
                    if(!option.Action.CanExecute(option.Actor,option.Target,Hands)){iceDelivery.Cancel();return;}
                    c.Begin(option);
                }
                finally{actionQueued=false;if(iceDelivery.Ready)iceDelivery.Cancel();}
            });
        }
        private void CancelIcePreparation()
        {preparingIce=null;icePreparationVersion++;iceDelivery?.Cancel();}
        private readonly Dictionary<string,int> toolIceCapacities=new Dictionary<string,int>();
        public int ToolIceCapacity(Container container)=>toolIceCapacities.TryGetValue(container.CoreVesselId,out int capacity)?capacity:int.MaxValue;
        public SessionLogWriter Journal {get;private set;}
        public CuePresenter Cues {get;private set;}
        public BSoundDirector Sound {get;private set;}
        public OrderLoop Orders {get;private set;}
        public LanguageSettings Settings {get;private set;}
        public bool OrderInputBlocked=>Settings.IsOpen || Orders&&Orders.ModalOpen;
        private readonly List<ActionOption> deferredMenu=new List<ActionOption>();
        private Controls controls;
        private void Deferred(ActionOption option,string button)
        {
            var moving=views.Values.FirstOrDefault(v=>v.IsAnimating);
            if(option==null&&moving==null)return;
            Adapter.Publish("interaction_deferred",new LogRecord{deferred=true,reason="view_animation",actionId=option?.Action.Id??button,
                actorId=option?.Actor?.InstanceId??moving?.InstanceId,targetId=option?.Target?.InstanceId??Target?.InstanceId,hand=option?.Hand?.ToString()??Hands.MainHand.ToString(),
                before=Adapter.States(option?.Actor??moving?.Item,option?.Target??Target?.Item),after=Adapter.States(option?.Actor??moving?.Item,option?.Target??Target?.Item),actualAmount=0});
        }
        private ActionResolver resolver;
        private int menuIndex;
        private string menuSelectionInput="default";
        private Vector3 hitPoint, hitNormal;
        private double epoch, pendingAngle, confirmedAt=-1;
        private bool pourIntent, actionQueued, inputSuspended;
        private InputStyle heldInputStyle;
        private ActionOption preparingPour;
        private Item pourSource;
        private int preparationVersion;
        private double pourReadyAt=double.PositiveInfinity,pourPreparationStarted;
        private void EnsurePourPresentation()
        {if(!pourPresentation){pourPresentation=gameObject.AddComponent<PourPresentationDriver>();pourPresentation.Session=this;}pourPresentation.Initialize();}
        private double PreparedTilt(double angle,double inputTime)
        {
            if(inputTime>=pourReadyAt||pourSource is not Bottle bottle)return angle;
            return Math.Min(angle,Math.Max(0,new TiltFlowModel(controls).StartAngle(bottle.RemainingMl,bottle.CapacityMl,bottle.Definition.Id)-.5));
        }
        private void CompletePourPreparation(double time)
        {
            if(preparingPour==null||pourPresentation?.Presentation?.Ready!=true)return;
            var option=preparingPour;preparingPour=null;pourReadyAt=time;
            Adapter.Publish("pour_preparation_ended",new LogRecord{actionId=option.Action.Id,actorId=option.Actor.InstanceId,targetId=option.Target.InstanceId,outcome="ready",inputTimeSeconds=time,durationSeconds=Adapter.World.Time-pourPreparationStarted});
            int version=preparationVersion;double angle=pendingAngle,x=gestureX,y=gestureY,p=press;
            Clock.Enqueue(time,c=>
            {
                if(version!=preparationVersion)return;
                try
                {
                    if(option.Action.Id!="pour.bottle_to_container")c.Begin(option);
                    c.SetTilt(angle);c.SetGesture(x,y,p);
                    if(option.Action.Id=="pour.jigger_to_container")RootView(option.Actor).AnimatePour((float)controls.JiggerPourSeconds,RootView(option.Target));
                }
                finally{actionQueued=false;}
            });
        }
        private void CancelPourPreparation()
        {if(preparingPour!=null)Adapter.Publish("pour_preparation_ended",new LogRecord{actionId=preparingPour.Action.Id,actorId=preparingPour.Actor.InstanceId,targetId=preparingPour.Target.InstanceId,outcome="cancelled",durationSeconds=Adapter.World.Time-pourPreparationStarted,actualAmount=0});preparingPour=null;preparationVersion++;actionQueued=false;pourPresentation?.Presentation?.ReleaseContinuous();}
        private double gestureX,gestureY,press;
        private string message;
        private Font font;
        private LineRenderer pourStream;
        private Material streamMaterial;
        private PourPresentationDriver pourPresentation;
        public PourPresentationDriver PourDisplay=>pourPresentation;
        private ShakePresentation shakePresentation;
        public ShakePresentation ShakeDisplay=>shakePresentation;
        private StirMotion stirMotion;
        /// <summary>吧勺搅拌的画面动作（相位、转速、轻碰计数），声音和冰块漩涡读取它。</summary>
        public StirMotion StirDisplay=>stirMotion;
        /// <summary>手势参数（只读），画面层与领域用同一套圆周测量规则。</summary>
        public Controls GestureControls=>controls;
        /// <summary>将 Input System 的绝对时间转换成场景输入时间。</summary>
        /// <precondition>absoluteTime 来自相对 realtimeSinceStartup 的输入事件时间。</precondition>
        /// <effects>只返回从场景初始化起经过的非负秒数。</effects>
        public double EventTime(double absoluteTime) => Math.Max(0,absoluteTime-epoch);
        private void Awake()
        {
            if(!ModelJson||!ItemsJson||!ControlsJson||!Player)throw new InvalidOperationException("场景数据引用缺失，请检查 BarSession 的数据和玩家绑定。");
            var model=JsonUtility.FromJson<ModelData>(ModelJson.text);OpticalSettings.Apply(model,Resources.Load<CueSettingsAsset>("BarCueSettings").Optics.text);var catalog=ItemCatalog.Load(ItemsJson.text,model);controls=ItemCatalog.LoadControls(ControlsJson.text);
            var factory=new ItemFactory(catalog,id=>model.ingredients.Single(x=>x.id==id).temperature,new System.Random(20261021).NextDouble);
            LiquidRenderSetup.Install();Hands=new Hands();Hands.SetDominant(PlayerPrefs.GetString(HandednessKey,"Right")=="Left"?HandSide.Left:HandSide.Right);Adapter=new WorldAdapter(model);resolver=ActionResolver.CreateU4(controls,model.icePortionGrams,model.iceDensity,new System.Random(20261021).NextDouble,ToolIceCapacity);Clock=new SimulationClock(Adapter,Hands);epoch=Time.realtimeSinceStartupAsDouble;
            var cueAsset=Resources.Load<CueSettingsAsset>("BarCueSettings")?.Json;
            if(!cueAsset)throw new InvalidOperationException("缺少线索映射 BarCues.json");
            var header=new SessionHeader{model=model,participantId=Guid.NewGuid().ToString("N"),iceTransferPolicy="whole-blocks-cavity-and-volume-v1",targetFrameRate=Application.targetFrameRate,
                measuredFrameRate=Time.unscaledDeltaTime>0?1/Time.unscaledDeltaTime:0,
                dataHashes=new[]{SessionLogWriter.Hash("model-v2.json",ModelJson.bytes),SessionLogWriter.Hash("items.json",ItemsJson.bytes),SessionLogWriter.Hash("controls.json",ControlsJson.bytes),SessionLogWriter.Hash("BarCues.json",cueAsset.bytes),SessionLogWriter.Hash("Optics.json",Resources.Load<CueSettingsAsset>("BarCueSettings").Optics.bytes),SessionLogWriter.Hash("BarBSettings.json",Resources.Load<TextAsset>("BarBSettings").bytes),SessionLogWriter.Hash("BarAudioCatalog.json",Resources.Load<TextAsset>("BarAudioCatalog").bytes),SessionLogWriter.Hash("BPresentationManifest.json",Resources.Load<TextAsset>("BPresentationManifest").bytes)}};
            header.dataHashes=header.dataHashes.Concat(new[]{SessionLogWriter.Hash("BarPourSettings.json",Resources.Load<TextAsset>("BarPourSettings").bytes)}).ToArray();
            header.handedness=Hands.Dominant.ToString();PresentationRig.FillHeader(header,Player.ViewCamera);
            header.dataHashes=header.dataHashes.Concat(new[]{SessionLogWriter.Hash("BarStirSettings.json",Resources.Load<TextAsset>("BarStirSettings").bytes)}).ToArray();
            if(Player.Locomotion)header.dataHashes=header.dataHashes.Concat(new[]{SessionLogWriter.Hash("BarLocomotion.json",Player.Locomotion.Configuration.bytes),SessionLogWriter.Hash("BarFootsteps.json",Player.Locomotion.Footsteps.AudioBanks.bytes)}).ToArray();
            header.dataHashes=header.dataHashes.Concat(new[]{SessionLogWriter.Hash("BarIceDeliverySettings.json",Resources.Load<TextAsset>("BarIceDeliverySettings").bytes)}).ToArray();
            Orders=gameObject.AddComponent<OrderLoop>();Journal=Orders.Open(this,header,Path.Combine(Application.persistentDataPath,"BarLogs"));
            foreach(var view in FindObjectsByType<ItemView>(FindObjectsSortMode.None).OrderBy(v=>v.InstanceId))
            {
                var item=factory.Create(view.DefinitionId,view.InstanceId);if(item is Jigger j)j.Flip();
                // The cap is modelled inside the bottle; split it off before Bind so the bottle's highlight no longer covers it.
                if(item is Bottle bottle&&bottle.Cap!=null)
                {
                    var cap=DetachablePartView.Extract(view,"Cap",bottle.CapSlot.Name,bottle.Cap.Definition.Id,bottle.Cap.InstanceId);
                    if(!cap)throw new InvalidOperationException("酒瓶模型缺少瓶盖节点 Cap："+view.InstanceId);
                    cap.Bind(bottle.Cap,model.icePortionGrams,model.iceDensity);views.Add(bottle.Cap.InstanceId,cap);placementRotations[bottle.Cap.InstanceId]=Quaternion.identity;
                }
                view.Bind(item,model.icePortionGrams,model.iceDensity);views.Add(item.InstanceId,view);Adapter.Register(item);placements[item.InstanceId]=view.transform.position;placementRotations[item.InstanceId]=view.transform.rotation;
                if(item is Bottle liquidBottle){var liquidView=view.gameObject.AddComponent<BottleLiquidView>();var ingredient=model.ingredients.First(i=>i.id==liquidBottle.IngredientId);liquidView.Initialize(liquidBottle,ingredient.optical,(float)ingredient.co2GL);}
            }
            Adapter.Events.Published+=e=>
            {
                if(e.Observation is not LogRecord row||row.operation?.type!="AddIce"||row.actorId==null||!views.TryGetValue(row.actorId,out var tool))return;
                if(iceDelivery?.Ready==true&&iceDelivery.Tool==tool)
                {
                    int count=(int)Math.Round((row.operation.grams??0)/Adapter.World.Data.icePortionGrams);
                    if(count>0)IceDeliveries[row.operation.to]=iceDelivery.Commit(count,iceFallSeconds);
                    IceOrigins[row.operation.to]=iceDelivery.ReleasePosition;
                }
                else IceOrigins[row.operation.to]=tool.LoadedIceOrigin;
            };
            var cueSettings=JsonUtility.FromJson<CueSettings>(cueAsset.text);iceFallSeconds=cueSettings.visual.iceEntrySeconds;Cues=gameObject.AddComponent<CuePresenter>();Cues.Initialize(this,cueSettings);
            foreach(var view in views.Values)foreach(var binding in view.GetComponentsInChildren<ContainerVisual>())
            {
                Container container=view.Item is Jigger j?(binding.DefinitionId==j.CupA.Definition.Id?j.CupA:j.CupB):view.Item is CobblerShaker sh?sh.Body:view.Item as Container;
                var rig=binding.GetComponent<LocalIceWorld>();if(container!=null&&rig)toolIceCapacities[container.CoreVesselId]=Math.Min(rig.Capacity,cueSettings.visual.maxIce);
            }
            Adapter.Events.Published+=e=>
            {if(e.Kind=="tool_ice_discarded"&&e.Observation is LogRecord discarded&&discarded.toolIceCubes>0&&views.TryGetValue(discarded.actorId,out var tool)&&views.TryGetValue(discarded.targetId,out var sink))IceWasteFlight.Play(tool,sink,discarded.toolIceCubes.Value);};
            Sound=gameObject.AddComponent<BSoundDirector>();Sound.Initialize(this);
            #if UNITY_WEBGL && !UNITY_EDITOR
            font=Resources.Load<Font>("BarUISubsetSC");
#else
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},16);
#endif
            Settings=new LanguageSettings(this);
            Hud=gameObject.AddComponent<SpeakeasyHud>();Hud.Initialize(this,font);
            Orders.Ready(font);
            DebugPanel=new SystemStatePanel(this,cueSettings);
            CapturePointer();
        }
        public void ResumePointer()=>CapturePointer();
        private void CapturePointer(){inputSuspended=false;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        private void Update()
        {
            double frameTime=EventTime(Time.realtimeSinceStartupAsDouble);CompletePourPreparation(frameTime);CompleteIcePreparation(frameTime);
            Clock.AdvanceFrame(frameTime);
            foreach(var view in views.Values)
            {
                view.SetPressVisual(null);
                if (view.Item.IsDiscarded)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }
                if (view.Item.Location.Kind == LocationKind.Attached)
                {
                    returningToSurface.Add(view.InstanceId);
                    if (views.TryGetValue(view.Item.Location.OwnerId, out var owner))
                    {
                        var slot = owner.Anchor(view.Item.Location.Anchor);
                        if (slot)
                        {
                            view.gameObject.SetActive(true);
                            view.transform.SetParent(slot, false);
                            Vector3 scale = slot.lossyScale;
                            view.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
                            view.transform.localPosition = Vector3.zero;
                            view.transform.localRotation = Quaternion.identity;
                        }
                    }
                    continue;
                }
                view.gameObject.SetActive(true);
                Transform anchor=view.Item.Location.Kind==LocationKind.LeftHand?Player.LeftAnchor:view.Item.Location.Kind==LocationKind.RightHand?Player.RightAnchor:null;
                if(anchor)
                {
                    returningToSurface.Add(view.InstanceId);
                    var grip=view.Anchor(view.Item.Location.Kind==LocationKind.LeftHand?"Grip_L":"Grip_R");if(!grip)grip=view.Anchor(view.Item.Location.Kind==LocationKind.LeftHand?"Grip_R":"Grip_L");Vector3 gripPosition=grip?view.transform.InverseTransformPoint(grip.position):Vector3.zero;Quaternion gripRotation=grip?Quaternion.Inverse(view.transform.rotation)*grip.rotation:Quaternion.identity;
                    view.transform.SetParent(anchor,false);
                    view.transform.localScale = Vector3.one;
                    view.transform.localRotation=Quaternion.Inverse(gripRotation);view.transform.localPosition=-(view.transform.localRotation*gripPosition);
                }
                else
                {
                    if(view.Item is Glass served&&served.IsServed)
                    {
                        var mat=views.Values.FirstOrDefault(v=>v.InstanceId==view.Item.Location.OwnerId);
                        if(mat)
                        {
                            var servedPosition=mat.transform.position+Vector3.up*(mat.Size.y+.002f);
                            if(!placements.TryGetValue(view.InstanceId,out var previous)||(servedPosition-previous).sqrMagnitude>1e-8f)
                            {placements[view.InstanceId]=servedPosition;returningToSurface.Add(view.InstanceId);}
                        }
                    }
                    if(returningToSurface.Contains(view.InstanceId)&&placements.TryGetValue(view.InstanceId,out var position))
                    {
                        view.transform.SetParent(null,true);
                        view.transform.localScale = Vector3.one;
                        float blend=1-Mathf.Exp(-18*Time.unscaledDeltaTime);
                        var rotation=placementRotations[view.InstanceId];
                        view.transform.position=Vector3.Lerp(view.transform.position,position,blend);
                        view.transform.rotation=Quaternion.Slerp(view.transform.rotation,rotation,blend);
                        if((view.transform.position-position).sqrMagnitude<1e-8f&&Quaternion.Angle(view.transform.rotation,rotation)<.05f)
                        {
                            view.transform.SetPositionAndRotation(position,rotation);
                            returningToSurface.Remove(view.InstanceId);
                        }
                    }
                    else
                    {
                        // Remember scene edits for the next pickup, rather than pulling an idle object back every frame.
                        placements[view.InstanceId]=view.transform.position;
                        placementRotations[view.InstanceId]=view.transform.rotation;
                    }
                }
            }
            UpdatePourView();iceDelivery?.Advance(Time.unscaledDeltaTime);Targeting();
        }
        private void UpdatePourView()
        {
            if(pourStream)pourStream.enabled=false;
            if(!pourPresentation){pourPresentation=gameObject.AddComponent<PourPresentationDriver>();pourPresentation.Session=this;}
            pourPresentation.Tilt=(float)pendingAngle;pourPresentation.Press=(float)press;
            pourPresentation.PresentNow();
            var active=Clock.Active;bool shaking=active!=null&&!active.IsEnded&&active.Action.InputStyle==InputStyle.HoldOscillate;
            (shakePresentation??=new ShakePresentation()).Apply(this,shaking?RootView(active.Actor):null,shaking,gestureY,Time.unscaledDeltaTime);
            // 2026-10-28: the stir runs every frame like the shake, so the spoon can enter over the rim and lift back out after release.
            bool stirring=active!=null&&!active.IsEnded&&active.Action.InputStyle==InputStyle.HoldCircle;
            (stirMotion??=new StirMotion()).Apply(this,stirring?RootView(active.Actor):null,stirring?RootView(active.Target):null,stirring?active.Target as Container:null,stirring,gestureX,gestureY,Time.unscaledDeltaTime);
            if(active==null||active.IsEnded)return;
            if(active.Action.InputStyle==InputStyle.HoldPress)RootView(active.Actor)?.SetPressVisual((float)press);
        }
        private ItemView RootView(Item item)
        {if(item==null)return null;while(item.Parent!=null)item=item.Parent;return views.TryGetValue(item.InstanceId,out var view)?view:null;}
        /// <summary>按相机中心射线更新准星目标和悬停菜单。</summary>
        /// <precondition>Player.ViewCamera 存在，场景物品具有碰撞体。</precondition>
        /// <effects>忽略随相机固定的手持物，更新场景命中与高亮，并添加可执行的双手动作入口。</effects>
        public void Targeting()
        {
            Target?.Highlight(false);Target=null;var camera=Player.ViewCamera;
            foreach(var hit in Physics.RaycastAll(camera.transform.position,camera.transform.forward,2.5f).OrderBy(h=>h.distance))
            {
                var view=hit.collider.GetComponentInParent<ItemView>();
                if(!view||!view.gameObject.activeInHierarchy||Hands.SideOf(view.Item).HasValue)continue;
                if (view.Item.Location.Kind == LocationKind.Attached && views.TryGetValue(view.Item.Location.OwnerId, out var owner)) view = owner;
                if (Hands.SideOf(view.Item).HasValue) continue;
                if (view.Item is BarCounter && view.TryGetComponent<PlacementSurface>(out var surface) && surface.IsOpening(hit.point)) continue;
                Target=view;hitPoint=hit.point;hitNormal=hit.normal;break;
            }
            Target?.Highlight(true);RefreshMenu();
        }
        private static bool IsOwnership(ActionOption option)=>option.Action.Id=="item.pick_up"||option.Action.Id=="item.place";
        private void RefreshMenu()
        {
            var selected=Target==menuTarget&&menuIndex<menu.Count?menu[menuIndex]:null;menuTarget=Target;menu.Clear();deferredMenu.Clear();SecondaryHint=null;
            if(IceDeliveryBusy||inputSuspended||pourIntent||actionQueued||Hands.IsBusy){menuIndex=0;return;}
            if(Target)foreach(var option in resolver.Resolve(Target.Item,Hands))
            {if(IsOwnership(option)){if(SecondaryHint==null)SecondaryHint=option.Action.Id=="item.place"?"[右键] 放置":"[右键] 拿起";}else menu.Add(option);}
            // Held items have a fixed screen offset and cannot be aimed at with the centre ray.
            // Keep the same Domain rules available through an explicitly labelled hand target.
            foreach(var held in new[]{Hands.Left,Hands.Right})
                if(held!=null)foreach(var option in resolver.Resolve(held,Hands).Where(o=>!IsOwnership(o)))
                    if(!menu.Any(o=>SameOption(o,option)))menu.Add(option);
            deferredMenu.AddRange(menu.Where(o=>RootView(o.Actor)?.IsAnimating==true||RootView(o.Target)?.IsAnimating==true));
            menu.RemoveAll(o => RootView(o.Actor)?.IsAnimating == true || RootView(o.Target)?.IsAnimating == true);
            menu.RemoveAll(o => o.Action.Id=="shaker.wipe" && (Cues==null || !Cues.HasSurfaceWater(o.Target)));
            var ordered=menu.OrderBy(o=>HudText.Priority(o,Target?Target.Item:null,Hands)).ThenBy(o=>o.Hand==Hands.MainHand?0:1).ToArray();menu.Clear();menu.AddRange(ordered);
            int retained=selected==null?-1:menu.FindIndex(o=>SameOption(o,selected));menuIndex=retained<0?0:retained;if(retained<0)menuSelectionInput="default";
        }
        private static bool SameOption(ActionOption a,ActionOption b)=>a.Action.Id==b.Action.Id&&ReferenceEquals(a.Actor,b.Actor)&&ReferenceEquals(a.Target,b.Target);
        /// <summary>处理左键主动作或悬停菜单确认。</summary>
        /// <precondition>time 已通过 EventTime 转换；选择倒酒时须持续按住左键。</precondition>
        /// <effects>重新命中并校验菜单，入队所选制作动作；从不拿起或放下。释放保留倒酒尾部时间；Esc 后首次点击只恢复输入。</effects>
        public void LeftButton(bool down,double time)
        {
            if(down)GetComponent<HoldViewRig>()?.InterruptInspections("mouse.left");
            if(Sound&&Sound.Gate&&!Sound.Gate.Unlocked)return;
            if(!down)
            {
                pourIntent=false;int version=preparationVersion;
                Clock.Enqueue(time,c=>{if(version==preparationVersion&&heldInputStyle!=InputStyle.Click&&time<pourReadyAt)CancelPourPreparation();c.Stop();});
                return;
            }
            if(inputSuspended){CapturePointer();Targeting();return;}
            if(IceDeliveryBusy||actionQueued||Hands.IsBusy||pourIntent){Deferred(null,"input.busy");return;}
            Targeting();if(menu.Count>0)Choose(menu[menuIndex],time);else if(deferredMenu.Count>0)Deferred(deferredMenu[0],"mouse.left");
        }
        /// <summary>用右键直接拿起准星物品，或在吧台空处放下主手物品。</summary>
        /// <precondition>无进行中的动作与排队动作；拿起需空手，放下需命中吧台上表面。</precondition>
        /// <effects>从领域候选中只选拿放命令；双手都有物品时按主手放下，Tab 可先换主手；不打开或关闭菜单。</effects>
        public void RightButton(double time)
        {
            GetComponent<HoldViewRig>()?.InterruptInspections("mouse.right");
            if(Sound&&Sound.Gate&&!Sound.Gate.Unlocked)return;
            if(inputSuspended){CapturePointer();Targeting();return;}
            if(IceDeliveryBusy||actionQueued||Hands.IsBusy||pourIntent){Deferred(null,"input.busy");return;}
            Targeting();if(!Target)return;
            var option=resolver.Resolve(Target.Item,Hands).FirstOrDefault(IsOwnership);
            if(option!=null)Choose(option,time);
            else if(Target.Item is not Fixture)message="双手已满或当前物品不能拿起；对准空吧台右键放下主手物品";
        }
        /// <summary>滚轮或数字键选择悬停菜单项。</summary>
        /// <precondition>当前至少有一条可执行制作动作；direct=true 时 index 为从零开始的序号。</precondition>
        /// <effects>只更新高亮，保持视角自由；目标或候选改变时保留仍有效的选择，否则回到首项。</effects>
        public void SelectMenu(int index,bool direct)
        {if(!MenuOpen||menu.Count==0)return;menuIndex=direct?Mathf.Clamp(index,0,menu.Count-1):(menuIndex+index+menu.Count)%menu.Count;menuSelectionInput=direct?"direct":"wheel";}
        /// <summary>将鼠标位移转换为视角或倒酒倾角。</summary>
        /// <precondition>time 是输入事件的场景时间；Esc 释放输入时忽略位移。</precondition>
        /// <effects>按住倒酒时累计限幅倾角并入队；悬停菜单不阻止视角移动，移动后立即刷新候选。</effects>
        public void PointerDelta(Vector2 delta,double time)
        {
            if(Sound&&Sound.Gate&&!Sound.Gate.Unlocked)return;
            if(inputSuspended)return;
            if(pourIntent)
            {
                if(heldInputStyle==InputStyle.HoldTilt){pendingAngle=Math.Max(0,Math.Min(controls.MaximumTiltDegrees,pendingAngle+delta.y*controls.TiltDegreesPerInput));double angle=pendingAngle;Clock.Enqueue(time,c=>c.SetTilt(PreparedTilt(angle,time)));}
                else{gestureX+=delta.x;gestureY+=delta.y;press=Math.Max(0,Math.Min(1,-gestureY/controls.PressTravel));double x=gestureX,y=gestureY,p=press;if(preparingPour==null)Clock.Enqueue(time,c=>c.SetGesture(x,y,p));}
            }
            else if(!LookLocked){Player.Look(delta);Physics.SyncTransforms();Targeting();}
        }
        /// <summary>切换当前主手。</summary>
        /// <precondition>另一只手有物品且领域双手未被动作锁定。</precondition>
        /// <effects>将 ToggleMain 命令排到输入时间线上，并在下一次目标刷新后按新主手排列候选。</effects>
        public void ToggleMain(double time){GetComponent<HoldViewRig>()?.InterruptInspections("tab");Clock.Enqueue(time,c=>{if(!IceDeliveryBusy)c.Hands.ToggleMain();});}
        /// <summary>惯用手偏好在本机的存储键（WebGL 下也可用）。</summary>
        public const string HandednessKey="Bar.Handedness";
        /// <summary>切换左右手习惯（V3）。</summary>
        /// <precondition>双手未被动作锁定。</precondition>
        /// <effects>入队 Hands.SetDominant：手里的东西换到另一只手，以后拿起按镜像的偏好手；成功后记住偏好，记录 handedness_changed。</effects>
        public void ToggleHandedness(double time)
        {GetComponent<HoldViewRig>()?.InterruptInspections("handedness");Clock.Enqueue(time,c=>{if(IceDeliveryBusy)return;var next=c.Hands.Dominant==HandSide.Left?HandSide.Right:HandSide.Left;if(c.Hands.SetDominant(next)){PlayerPrefs.SetString(HandednessKey,next.ToString());PlayerPrefs.Save();}else message="动作进行中不能切换左右手";});}
        /// <summary>把玩空量酒器（V3，只做展示）。</summary>
        /// <precondition>没有进行中的动作；主手（否则另一只手）拿着量酒器，且两端都没有液体和冰。</precondition>
        /// <effects>发布 view_inspect 事件（记录日志），HoldViewRig 据此播放检视动作；不改领域和核心，不换端。</effects>
        private int inspectionSequence;
        public void Inspect(double time)
        {
            Clock.Enqueue(time,c=>
            {
                if(IceDeliveryBusy||c.Hands.IsBusy||c.Active!=null&&!c.Active.IsEnded||preparingPour!=null||pourIntent)return;
                var side=InspectionPolicy.Supported(c.Hands.Get(c.Hands.MainHand))?c.Hands.MainHand:c.Hands.MainHand==HandSide.Left?HandSide.Right:HandSide.Left;
                var item=c.Hands.Get(side);if(!InspectionPolicy.Supported(item))return;
                if(!InspectionPolicy.CanInspect(item)){message=InspectionPolicy.Rejection(item);return;}
                if(GetComponent<HoldViewRig>()?.Inspecting(side)==true)return;
                var bottle=item as Bottle;
                Adapter.Publish("view_inspect",new LogRecord{actionId=InspectionPolicy.ActionId(item),actorId=item.InstanceId,hand=side.ToString(),activationId="inspect-"+(++inspectionSequence),before=Adapter.States(item),inspection=new InspectionObservation{definitionId=item.Definition.Id,loaded=InspectionPolicy.Loaded(item),carbonated=InspectionPolicy.Carbonated(item),bottleRemainingMl=bottle?.RemainingMl,bottleCapacityMl=bottle?.CapacityMl,bottleOpen=bottle?.IsOpen,produceId=(item as Squeezer)?.Produce?.ProduceItem.InstanceId,produceRemainingMl=(item as Squeezer)?.Produce?.RemainingMl}});
            });
        }
        /// <summary>取消制作动作并释放鼠标输入。</summary>
        /// <precondition>可在任意交互状态调用。</precondition>
        /// <effects>隐藏悬停菜单，解除倒酒意图并入队取消；再次点击 Game 仅恢复输入，避免意外执行动作。</effects>
        public void Cancel(double time){GetComponent<HoldViewRig>()?.InterruptInspections("escape");menu.Clear();pourIntent=false;CancelPourPreparation();CancelIcePreparation();inputSuspended=true;Clock.Enqueue(time,c=>c.Stop(true));Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        /// <summary>切换调试面板。</summary>
        /// <precondition>场景已初始化。</precondition>
        /// <effects>翻转 DebugVisible，不改变物理或输入时间。</effects>
        public void ToggleDebug(){DebugVisible=!DebugVisible;Adapter.DebugExposed|=DebugVisible;Adapter.Publish("debug_toggled",new LogRecord{debugVisible=DebugVisible});}
        private void Choose(ActionOption option,double time)
        {
            if(option==null)return;
            if(IceDeliveryBusy)return;
            if (RootView(option.Actor)?.IsAnimating == true || RootView(option.Target)?.IsAnimating == true) {Deferred(option,"mouse");return;}
            message=null;
            if(option.Action.Id=="item.place")
            {
                if(hitNormal.y<.9f){message="请对准吧台上表面的空位置";return;}
                var view=views[option.Actor.InstanceId];var point=new Vector3(hitPoint.x,hitPoint.y+.002f,hitPoint.z);
                var surface = Target.GetComponent<PlacementSurface>();
                if (surface && !surface.CanPlace(point, view.Size))
                {
                    message = "这里是水槽、冰槽开口或台面边缘，请选择完整的空台面";
                    return;
                }
                foreach(var other in views.Values)if(other!=view&&other.Item is not Fixture&&other.Item.Parent==null&&other.Item.Location.Kind==LocationKind.Surface&&Vector2.Distance(new Vector2(point.x,point.z),new Vector2(other.transform.position.x,other.transform.position.z))<(view.Size.x+other.Size.x)*.5f+.012f){message="这里已有物品，请选择空位置";return;}
                placements[view.InstanceId]=point;
            }
            if(option.Action.Id=="pour.bottle_to_container"||option.Action.Id=="pour.barspoon_guided"){var bottle=(Bottle)option.Actor;Adapter.Publish("action_selected",new LogRecord{actionId=option.Action.Id,actorId=option.Actor.InstanceId,targetId=option.Target.InstanceId,hand=option.Hand.ToString(),selectedMenuIndex=menuIndex,defaultActionId=menu[0].Action.Id,selectionInput=menuSelectionInput,inputTimeSeconds=time,bottleRemainingMl=bottle.RemainingMl,bottleCapacityMl=bottle.CapacityMl,availableActionIds=menu.Select(o=>o.Action.Id).ToArray(),before=Adapter.States(option.Actor,option.Target)});}
            heldInputStyle=option.Action.InputStyle;pourIntent=heldInputStyle==InputStyle.HoldTilt||heldInputStyle==InputStyle.HoldPress||heldInputStyle==InputStyle.HoldOscillate||heldInputStyle==InputStyle.HoldCircle;pendingAngle=gestureX=gestureY=press=0;actionQueued=true;menu.Clear();
            if(option.Action.Id=="ice.add"&&option.Target is ILiquidReceiver receiver)
            {
                iceDelivery??=new IceDeliveryMotion();
                if(iceDelivery.Begin(RootView(option.Actor),RootView(option.Target),receiver.ReceivingContainer,Player.ViewCamera.transform))
                {
                    preparingIce=option;icePreparationVersion++;
                    Adapter.Publish("ice_delivery_prepared",new LogRecord{actionId=option.Action.Id,actorId=option.Actor.InstanceId,targetId=option.Target.InstanceId,hand=option.Hand?.ToString(),actualAmount=0,reason="reach_before_add_ice"});
                    return;
                }
            }
            if(PourPresentation.Supported(option.Action.Id))
            {
                EnsurePourPresentation();preparingPour=option;pourPreparationStarted=Adapter.World.Time;pourSource=option.Actor;pourReadyAt=double.PositiveInfinity;preparationVersion++;
                pourPresentation.Presentation.Prepare(option);
                if(option.Action.Id=="pour.bottle_to_container")Clock.Enqueue(time,c=>{try{c.Begin(option);}finally{actionQueued=false;}});
                return;
            }
            Clock.Enqueue(time,c=>
            {
                try{c.Begin(option);if(option.Action.Id=="pour.jigger_to_container")RootView(option.Actor).AnimatePour((float)controls.JiggerPourSeconds,RootView(option.Target));}
                catch{pourIntent=false;throw;}
                finally{actionQueued=false;}
            });
        }
        private void OnApplicationFocus(bool focus){if(!focus&&Clock!=null)Cancel(EventTime(Time.realtimeSinceStartupAsDouble));}
        private void OnDisable(){GetComponent<HoldViewRig>()?.InterruptInspections("scene.unloaded");CancelPourPreparation();CancelIcePreparation();if(Clock!=null)Clock.Stop(true);Journal?.Flush();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        private void OnDestroy(){DebugPanel?.Dispose();if(Orders)Orders.Close();else Journal?.Dispose();if(streamMaterial)Destroy(streamMaterial);if(font&&!Application.isMobilePlatform&&Application.platform!=RuntimePlatform.WebGLPlayer)Destroy(font);}
        private void OnGUI()
        {
            if(Clock==null)return;var displayFont=UiText.DisplayFont(font);GUI.skin.font=displayFont;GUI.skin.label.fontSize=16;GUI.color=Color.white;
            Hud?.DrawNotice(Clock.Error??message);
            if(DebugVisible&&!Settings.IsOpen)DebugPanel.Draw(displayFont);
            Settings.Draw(displayFont);
        }
    }
}
