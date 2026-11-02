using System;
using System.Linq;
using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>会话的只读倒液协调器；五类出液动作共用站位和 IPourStream，不提交或修改请求。</summary>
    public sealed class PourPresentation
    {
        private readonly BarSession session;private IPourStream stream;
        private GuidedPourPose guidedPose;
        public double EntryLossPerSecond=>lastReceipt==null?0:lastReceipt.Co2LostOnEntryGrams/Math.Max(.001,lastReceipt.DurationSeconds);
        private ActiveAction observed;private ItemView source,receiver;private PourPose pose;private float elapsed,returnElapsed;
        private Vector3 lastSource,lastReceiver;private Quaternion lastRotation,lastReceiverRotation;
        private OperationRequest lastReceipt;private float receiptAt;private readonly float clickSeconds;
        private float flowFrom,flowTarget,flowElapsed,shownFlow;
        private bool released;
        private ActionOption prepared;
        private ActiveAction preparedPredecessor;
        public bool Ready=>(pose!=null||guidedPose!=null)&&!released&&(guidedPose!=null?guidedPose.Ready:pose.Progress>=1);
        public ItemView DisplayReceiver=>pose!=null||guidedPose!=null?receiver:null;
        public static bool Supported(string id)=>id=="pour.barspoon_guided"||id=="pour.bottle_to_container"||id=="pour.jigger_to_container"||id=="shaker.pour"||id=="lemon.press"||id=="sink.empty";
        public void Prepare(ActionOption option)
        {
            prepared=option;preparedPredecessor=session.Clock.Active;observed=null;BeginPose(option.Actor,option.Target,option.Action.Id=="pour.barspoon_guided");
        }
        private void BeginPose(Item actor,Item target,bool guided=false)
        {
            source=Root(actor);receiver=Root(target);elapsed=returnElapsed=0;released=false;
            lastReceipt=null;receiptAt=0;flowFrom=flowTarget=flowElapsed=shownFlow=0;
            guidedPose=guided?new GuidedPourPose(session,source,receiver):null;
            pose=guided?null:new PourPose();if(!guided)pose.Begin(source,receiver,session.Player.ViewCamera.transform,session.Hands.SideOf(receiver.Item).HasValue);
        }
        public void ReleaseContinuous(){released=true;}
        public PourStreamInput LastInput {get;private set;}
        public bool Visible=>stream!=null&&stream.Visible;
        public PourPresentation(BarSession owner){session=owner;clickSeconds=(float)Bar.Data.ItemCatalog.LoadControls(owner.ControlsJson.text).JiggerPourSeconds;}
        private ItemView Root(Item item){while(item?.Parent!=null)item=item.Parent;return session.Views.FirstOrDefault(v=>ReferenceEquals(v.Item,item));}
        public void Tick(float dt,float tilt,float press)
        {
            var active=session.Clock.Active;
            if(active!=observed&&active!=null&&Supported(active.Action.Id)&&(prepared==null||active!=preparedPredecessor))
            {
                if(prepared!=null&&active.Actor==prepared.Actor&&active.Target==prepared.Target&&active.Action.Id==prepared.Action.Id){observed=active;prepared=null;}
                else if(prepared==null){BeginPose(active.Actor,active.Target,active.Action.Id=="pour.barspoon_guided");observed=active;}
            }
            if(pose==null&&guidedPose==null)return;elapsed+=dt;
            var action=prepared?.Action??observed?.Action;if(action==null)return;
            bool click=action.Id=="pour.jigger_to_container"||action.Id=="sink.empty";
            float duration=clickSeconds;
            var request=prepared==null?observed?.Requests.LastOrDefault(r=>r.ActualMl.HasValue&&r.AmountUnit=="ml"):null;
            bool freshReceipt=request!=null&&request!=lastReceipt&&request.ActualMl>0;
            bool ended=released||prepared==null&&(active!=observed||observed.IsEnded&&!freshReceipt&&(!click||elapsed>=duration+PourSettings.Load().alignSeconds));
            if(ended)
            {
                stream?.Hide();if(guidedPose!=null){if(guidedPose.Return(dt)){guidedPose=null;pose=null;}return;}returnElapsed+=dt;float a=Mathf.SmoothStep(0,1,Mathf.Clamp01(returnElapsed/PourSettings.Load().returnSeconds));
                var home=source.transform.position;var rotation=source.transform.rotation;source.transform.SetPositionAndRotation(Vector3.Lerp(lastSource,home,a),Quaternion.Slerp(lastRotation,rotation,a));
                if(session.Hands.SideOf(receiver.Item).HasValue){var target=receiver.transform.position;var r=receiver.transform.rotation;receiver.transform.SetPositionAndRotation(Vector3.Lerp(lastReceiver,target,a),Quaternion.Slerp(lastReceiverRotation,r,a));}
                if(a>=1)pose=null;return;
            }
            float flow=request==null?0:(float)request.ActualMl.Value/(float)Math.Max(request.DurationSeconds,click?duration:.001);
            if(request!=lastReceipt)
            {
                // Input/rendering run faster than core confirmation. A different live angle
                // does not invalidate an accepted receipt or restart the pouring pose.
                bool continueFlow=lastReceipt!=null&&shownFlow>0;
                lastReceipt=request;receiptAt=Mathf.Max(elapsed,PourSettings.Load().alignSeconds);
                flowFrom=continueFlow?shownFlow:flow;flowTarget=flow;flowElapsed=0;
            }
            if(!click)
            {
                bool valid=request!=null&&request==session.Adapter.LastRequest&&flow>0&&elapsed-receiptAt<=PourSettings.Load().confirmationWindowSeconds;
                if(!valid)flowFrom=flowTarget=shownFlow=0;
                else
                {
                    flowElapsed+=dt;
                    shownFlow=Mathf.Lerp(flowFrom,flowTarget,Mathf.Clamp01(flowElapsed/PourSettings.Load().flowTransitionSeconds));
                }
                flow=shownFlow;
            }
            // Single-click actions rotate while approaching, then hold their pouring angle until return.
            float angle=click?PourSettings.Load().clickTiltDegrees:tilt;
            var targetContainer=((prepared?.Target??observed?.Target) as ILiquidReceiver)?.ReceivingContainer;
            LastInput=guidedPose!=null?guidedPose.Step(dt,angle,flow,targetContainer):pose.Step(dt,angle,flow,targetContainer);
            if(stream==null)stream=PourStreamFactory.Create(session.gameObject);
            var ingredient=session.Adapter.World.Data.ingredients.FirstOrDefault(i=>i.id==request?.IngredientId);
            var optical=ingredient?.optical??session.Adapter.World.Vessels.FirstOrDefault(v=>v.id==request?.TargetVesselId)?.Snapshot().optical;
            if(optical!=null)stream.Tint=new Color(Mathf.Exp(-(float)optical[0]*3),Mathf.Exp(-(float)optical[1]*3),Mathf.Exp(-(float)optical[2]*3));
            if((guidedPose!=null?guidedPose.Ready:pose.Progress>=1)&&flow>0)stream.Draw(LastInput);else stream.Hide();
            lastSource=source.transform.position;lastRotation=source.transform.rotation;lastReceiver=receiver.transform.position;lastReceiverRotation=receiver.transform.rotation;
        }
    }
}
