using System;
using System.Collections;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Bar.Unity
{
    /// <summary>三单循环；场景重载恢复物品，新世界隔离尝试，同一日志续写。</summary>
    public sealed class OrderLoop:MonoBehaviour
    {
        private sealed class Continuation {public SessionLogWriter writer;public int index,attempt;public string hash;public bool debugExposed;}
        private static Continuation pending;
        private BarSession session;private SessionLogWriter writer;private string dataHash;
        private int index;private bool showing,presented,reloading,closed,cancelled;
        public OrderCatalog Catalog {get;private set;}
        public OrderDefinition Current=>Catalog.orders[Math.Min(index,Catalog.orders.Length-1)];
        public int AttemptIndex {get;private set;}=1;
        public bool Complete {get;private set;}
        public bool ModalOpen=>showing||Complete||reloading;
        public bool SettingsOpen=>session.Settings.IsOpen;
        public bool DebugVisible=>session.DebugVisible;
        public string[] FeedbackIds {get;private set;}=Array.Empty<string>();
        public OrderDimension[] Dimensions {get;private set;}=Array.Empty<OrderDimension>();
        public string InitialWorldHash {get;private set;}
        public string InitialLayout {get;private set;}
        /// <summary>在容器注册前标记尝试；重试复用写入器，外部换场景则开始新会话。</summary>
        public SessionLogWriter Open(BarSession owner,SessionHeader header,string directory)
        {
            session=owner;Catalog=OrderCatalog.Load();var bytes=Resources.Load<OrderDataAsset>("BarOrders").Json.bytes;
            dataHash=SessionLogWriter.Hash("orders.json",bytes).sha256;
            if(pending!=null)
            {
                var saved=pending;pending=null;
                if(saved.hash!=dataHash){saved.writer.Dispose();throw new InvalidOperationException("重试期间订单数据发生变化");}
                writer=saved.writer;index=saved.index;AttemptIndex=saved.attempt;writer.Connect(owner.Adapter.Events);owner.Adapter.DebugExposed=saved.debugExposed;
            }
            else
            {
                header.dataHashes=(header.dataHashes??Array.Empty<DataHash>()).Concat(new[]{SessionLogWriter.Hash("orders.json",bytes)}).ToArray();
                writer=new SessionLogWriter(owner.Adapter.Events,header,directory);
            }
            writer.SetAttempt(Current.id,AttemptIndex);
            owner.Adapter.Publish("order_started",new LogRecord{orderDataSHA256=dataHash});
            return writer;
        }
        public void Ready(Font font)
        {
            InitialWorldHash=LogReplay.SnapshotHash(session.Adapter.World);
            InitialLayout=string.Join("\n",session.Views.OrderBy(v=>v.InstanceId).Select(v=>v.InstanceId+"|"+v.transform.position.ToString("R")+"|"+v.transform.rotation.ToString("R")+"|"+v.Item.Location.Kind+"|"+(v.Item is LemonHalf l?l.JuiceRemainingMl.ToString("R",System.Globalization.CultureInfo.InvariantCulture):v.Item is Bottle b?b.RemainingMl.ToString("R",System.Globalization.CultureInfo.InvariantCulture):"")));
            var overlay=gameObject.AddComponent<OrderOverlay>();overlay.Initialize(this,font);
            session.Adapter.Events.Published+=Observe;
        }
        private void Observe(DomainEvent e)
        {
            if(showing||Complete||e.Kind!="core_operation"||e.Observation is not LogRecord row||row.rejected||row.operation?.type!="Serve")return;
            var glass=session.Views.Select(v=>v.Item).OfType<Glass>().Single(g=>g.CoreVesselId==row.operation.from);
            Dimensions=Catalog.Evaluate(Current,row.served,glass.GlassType==GlassType.Coupe?"coupe":"highball");FeedbackIds=Catalog.Displayed(Dimensions);
            // Queue display until the core event has unwound; log only when OnGUI actually presents it.
            showing=true;presented=false;
        }
        /// <summary>记录实际送到屏幕的评语；无界面渲染时不伪造已展示。</summary>
        public void MarkPresented()
        {
            if(!showing||presented)return;presented=true;
            var served=session.Adapter.World.Vessels.Last(v=>v.served).Snapshot();
            var glass=session.Views.Select(v=>v.Item).OfType<Glass>().Single(g=>g.CoreVesselId==served.id);
            session.Adapter.Publish("order_feedback",new LogRecord{served=served,servedGlass=glass.GlassType==GlassType.Coupe?"coupe":"highball",dimensions=Dimensions,feedbackIds=FeedbackIds});
        }
        private void Update()
        {
            if(ModalOpen){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        }
        private void LateUpdate()
        {
            if(ModalOpen&&session.Hud)session.Hud.enabled=false;
            if(showing&&!cancelled){cancelled=true;session.Cancel(session.EventTime(Time.realtimeSinceStartupAsDouble));}
        }
        // A user can stop Play mode while a scene transfer is queued. Let Close release that writer.
        private void OnApplicationQuit(){if(pending?.writer==writer)pending=null;}
        /// <summary>反馈后再做一次或下一单；结束页不自动跳回。</summary>
        public void Advance(bool retry)
        {
            if(!showing||reloading||!presented)return;
            session.Adapter.Publish("order_advanced",new LogRecord{advance=retry?"retry":"next"});
            if(!retry&&index+1==Catalog.orders.Length){showing=false;Complete=true;return;}
            reloading=true;
            pending=new Continuation{writer=writer,index=retry?index:index+1,attempt=retry?AttemptIndex+1:1,hash=dataHash,debugExposed=session.Adapter.DebugExposed};
            StartCoroutine(Reload());
        }
        private IEnumerator Reload()
        {
            // A new Core World is created by the new BarSession. No physics equations or action rules change.
            yield return null;
            var load=SceneManager.LoadSceneAsync(gameObject.scene.path);
            if(load==null){pending=null;reloading=false;throw new InvalidOperationException("无法重新载入当前吧台场景");}
        }
        public void Close()
        {
            if(closed)return;closed=true;session.Adapter.Events.Published-=Observe;
            if(pending?.writer!=writer)writer?.Dispose();
        }
    }
}
