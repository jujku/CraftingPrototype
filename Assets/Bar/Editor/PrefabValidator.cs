using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;
using CraftingResearch.CoreV2;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>在 Editor 窗口逐项报告锚点、米制尺寸、碰撞、内腔容量和刻度问题。</summary>
    public sealed class PrefabValidator : EditorWindow
    {
        private List<string> lines=new List<string>();private Vector2 scroll;
        /// <summary>打开选中物品的验证窗口。</summary>
        /// <precondition>Project 或 Hierarchy 已选中带 ItemView 的物品根对象。</precondition>
        /// <effects>打开窗口并显示检查清单，不改模型或数据。</effects>
        [MenuItem("Bar/Validate Selected Prefab")]
        public static void Open(){var window=GetWindow<PrefabValidator>("物品预制体验证");window.Check();}
        private void Check(){lines=Validate(Selection.activeGameObject);Repaint();}
        private void OnGUI(){if(GUILayout.Button("重新检查当前选择"))Check();if(GUILayout.Button("烘焙当前物品液面曲线")){try{if(!Selection.activeGameObject)throw new ArgumentException("请选择物品根节点");VolumeProfileBaker.BakeAndSave(Selection.activeGameObject,"Assets/Bar/Art/VolumeProfiles");Check();}catch(Exception e){lines.Add("失败 · "+e.Message);}}scroll=EditorGUILayout.BeginScrollView(scroll);foreach(var line in lines)EditorGUILayout.HelpBox(line,line.StartsWith("通过")?MessageType.Info:MessageType.Error);EditorGUILayout.EndScrollView();}
        /// <summary>验证一个物品实例或预制体根节点。</summary>
        /// <precondition>数据 JSON 可加载；root 可为空，空选择会得到明确失败项。</precondition>
        /// <effects>返回每项结果及修复方法，不自动修复或改变源资源。</effects>
        public static List<string> Validate(GameObject root)
        {
            var result=new List<string>();var view=root?root.GetComponent<ItemView>():null;if(!view){result.Add("失败 · 选择物品根对象并挂 ItemView，填写 DefinitionId");return result;}
            try
            {
                var catalog=ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text,JsonUtility.FromJson<ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text));var definition=catalog[view.DefinitionId];
                void Add(bool ok,string message,string fix)=>result.Add((ok?"通过 · ":"失败 · ")+message+(ok?"":"；修复："+fix));
                var all=root.GetComponentsInChildren<Transform>(true);
                foreach(string name in definition.Anchors)Add(all.Any(t=>t.name==name),definition.Id+" 锚点 "+name,"按指南在根对象下创建同名节点");
                Add(root.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled&&!c.isTrigger),"存在可交互碰撞体","给物品根对象添加 BoxCollider 并覆盖外观");
                Add(Vector3.Distance(root.transform.lossyScale,Vector3.one)<.001f,"米制根缩放为 (1,1,1)","在模型导入或建模软件中应用缩放，保持 1 Unity 单位 = 1 m");
                var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.name!="LiquidVolume"&&r.name!="InnerCavity").ToArray();
                if(renderers.Length==0)Add(false,"缺少外观网格","添加启用的 MeshRenderer");else
                {
                    var bounds=new Bounds();bool first=true;foreach(var r in renderers){var b=r.localBounds;for(int k=0;k<8;k++){var v=b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1));v=root.transform.InverseTransformPoint(r.transform.TransformPoint(v));if(first){bounds=new Bounds(v,Vector3.zero);first=false;}else bounds.Encapsulate(v);}}
                    Vector3 expected=new Vector3((float)definition.Size.WidthCm,(float)definition.Size.HeightCm,(float)definition.Size.DepthCm)*.01f;
                    for(int i=0;i<3;i++)Add(Mathf.Abs(bounds.size[i]/expected[i]-1)<=.15f,"外观尺寸 "+"XYZ"[i]+" = "+bounds.size[i].ToString("F4")+" m","参考 "+expected[i].ToString("F4")+" m，误差须在 ±15% 内");
                }
                var visuals=root.GetComponentsInChildren<ContainerVisual>(true);int expectedCount=definition.Kind==ItemKind.Container?1:definition.Parts.Count(id=>catalog[id].Kind==ItemKind.Container);
                Add(visuals.Length==expectedCount,"容器部件数 "+visuals.Length+" / "+expectedCount,"壶身或杯子配置 1 个 ContainerVisual；量酒器 CupA/CupB 各 1 个");
                foreach(var visual in visuals)
                {
                    var d=catalog[visual.DefinitionId];Add(visual.LiquidVolume,d.Id+" 液体网格已绑定","创建 LiquidVolume 网格并赋值到 ContainerVisual.LiquidVolume");foreach(string name in d.Anchors)Add(visual.GetComponentsInChildren<Transform>(true).Any(t=>t.name==name),d.Id+" 锚点 "+name,"在该容器部件下添加同名节点");
                    VolumeProfile baked=null;try{baked=VolumeProfileBaker.Bake(visual.InnerCavity,visual.transform);bool working=visual.NominalCapacityIsWorkingFill;
                    Add(working?baked.CapacityMl>=d.CapacityMl&&baked.HeightForMl(d.CapacityMl)<baked.Heights[baked.Heights.Length-1]-.001f:Math.Abs(baked.CapacityMl/d.CapacityMl-1)<=.05,d.Id+" 内腔 "+baked.CapacityMl.ToString("F2")+" ml / 额定 "+d.CapacityMl+(working?"（工作液量，满口留有余量）":""),working?"工作液量必须装得下，且满口至少留1mm；重新烘焙真实内腔，不得缩放曲线凑容量":"修改内腔几何，误差须 ≤5%，不能仅修改 Profile 或 ItemView.Size");
                    Add(visual.Profile&&visual.Profile.Heights.Length==baked.Heights.Length&&visual.Profile.VolumesMl.Length==baked.VolumesMl.Length&&visual.Profile.Heights.Zip(baked.Heights,(a,b)=>Math.Abs(a-b)<=.0005).All(x=>x)&&visual.Profile.VolumesMl.Zip(baked.VolumesMl,(a,b)=>Math.Abs(a-b)<=d.CapacityMl*.005).All(x=>x),d.Id+" 已绑定当前几何的液面曲线","点击烘焙当前物品液面曲线并保存预制体");
                    foreach(double mark in d.MarkingsMl){var anchor=visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Mark_"+mark.ToString(System.Globalization.CultureInfo.InvariantCulture)+"ml");float h=baked.HeightForMl(mark);Add(anchor&&Mathf.Abs(visual.transform.InverseTransformPoint(anchor.position).y-h)<=.001f,d.Id+" 刻度 "+mark+" ml 应在 "+h.ToString("F4")+" m","创建 Mark_"+mark+"ml 节点并移动到所示局部高度（±1 mm），使模型刻线与它重合");}}
                    catch(Exception e){Add(false,d.Id+" 烘焙失败："+e.Message,"检查 InnerCavity 引用、Read/Write、封闭面和面方向");}finally{if(baked)DestroyImmediate(baked);}
                }
            }catch(Exception e){result.Add("失败 · 数据/定义引用错误："+e.Message+"；填写有效 DefinitionId 并确认 JSON 引用");}return result;
        }
    }
}
