using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// <summary>真实场景回归：一铲一层两块，两铲装满 highball，第三铲装不进、勺留两块，再从水槽口丢弃。</summary>
    public sealed class IceScoopTransferTests
    {
        [UnityTest] public IEnumerator PartialScoopKeepsTwoBlocksAndDiscardsThemToSinkWithAnAuditableReceipt()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
            var scoopView=View("tool.scoop");var scoop=(IceScoop)scoopView.Item;var bin=(IceBin)View("fixture.icebin").Item;
            var glass=(Glass)View("glass.highball").Item;var sinkView=View("fixture.sink");var sink=(Sink)sinkView.Item;int stock=bin.StockCubes;
            var resolver=(ActionResolver)typeof(BarSession).GetField("resolver",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            ActionOption Option(Item target,string id)=>resolver.Resolve(target,session.Hands).Single(o=>o.Action.Id==id);
            Assert.That(session.Hands.TryPickUp(scoop),Is.True);Assert.That(session.ToolIceCapacity(glass),Is.EqualTo(4));
            // a scoop holds one layer of two cubes; two scoops fill the highball, a third cannot go in and stays on the scoop.
            new TakeIceAction(()=>0).Begin(scoop,bin,session.Hands);Assert.That(scoop.LoadedCubes,Is.EqualTo(2));
            session.Clock.Begin(Option(glass,"ice.add"));session.Clock.Active.End();Assert.That(glass.Contents.IceCubes,Is.EqualTo(2));
            new TakeIceAction(()=>0).Begin(scoop,bin,session.Hands);session.Clock.Begin(Option(glass,"ice.add"));session.Clock.Active.End();Assert.That(scoop.LoadedCubes,Is.Zero);
            new TakeIceAction(()=>0).Begin(scoop,bin,session.Hands);
            Assert.That(glass.Contents.IceCubes,Is.EqualTo(4));Assert.That(scoop.LoadedCubes,Is.EqualTo(2));Assert.That(bin.StockCubes,Is.EqualTo(stock-6));
            Assert.That(resolver.Resolve(glass,session.Hands).Any(o=>o.Action.Id=="ice.add"),Is.False);
            var rig=View("glass.highball").GetComponentInChildren<LocalIceWorld>();rig.Advance(.25f);Assert.That(rig.DisplayCount,Is.EqualTo(4));Assert.That(rig.DisplayCapped,Is.False);
            yield return null;yield return null;
            Assert.That(scoopView.GetComponentsInChildren<MeshFilter>().Count(f=>f.name=="IceSurface"&&f.gameObject.activeInHierarchy),Is.EqualTo(2),"余冰必须继续显示在冰勺上");
            string before=LogReplay.SnapshotHash(session.Adapter.World);session.Clock.Begin(Option(sink,"ice.discard"));
            Assert.That(scoop.LoadedCubes,Is.Zero);Assert.That(sink.DiscardedToolIceCubes,Is.EqualTo(2));Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(before));
            Assert.That(sinkView.GetComponentInChildren<IceWasteFlight>().DisplayCount,Is.EqualTo(2));
            var lines=session.Journal.ReadLines();var rows=lines.Select(SessionJournal.Decode).ToArray();
            var amounts=rows.Where(r=>r.kind=="core_operation"&&r.operation.type=="AddIce").Select(r=>r.operation.grams).ToArray();
            Assert.That(amounts,Is.EqualTo(new[]{80d,80d}));var discard=rows.Single(r=>r.kind=="tool_ice_discarded");
            Assert.That(discard.toolIceCubes,Is.EqualTo(2));Assert.That(discard.actualAmount,Is.EqualTo(80));Assert.That(discard.amountUnit,Is.EqualTo("g"));
            Assert.That(rows[0].header.iceTransferPolicy,Is.EqualTo("whole-blocks-cavity-and-volume-v1"));new LogReplay().Run(lines);
            Assert.That(session.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(session.Adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../IceScoopReports"));Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output,"partial-scoop-session.jsonl"),lines);
            File.WriteAllText(Path.Combine(output,"result.txt"),"scooped=6\ncontainer=4\nremainingBeforeDiscard=2\nsinkRawWaste=2\ncoreAddIceGrams=80,80\ndiscardGrams=80\ncoreChangedByRawDiscard=false\nreplay=passed\n");
            yield return new WaitForSecondsRealtime(.3f);Assert.That(sinkView.GetComponentInChildren<IceWasteFlight>(),Is.Null);Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(before));
            Assert.That(resolver.Resolve(sink,session.Hands).Any(o=>o.Action.Id=="ice.discard"),Is.False);Assert.That(resolver.Resolve(bin,session.Hands).Any(o=>o.Action.Id=="ice.take"),Is.True);
        }
    }
}

