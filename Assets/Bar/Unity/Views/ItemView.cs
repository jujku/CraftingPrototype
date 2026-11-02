using Bar.Domain;
using UnityEngine;

namespace Bar.Unity
{
    /// <summary>把场景物体绑定到领域实例，只读核心快照显示液面与部件状态。</summary>
    public sealed class ItemView : MonoBehaviour
    {
        /// <summary>物品定义标识，场景构建器按 items.json 填写。</summary>
        public string DefinitionId;
        /// <summary>场景内唯一实例标识。</summary>
        public string InstanceId;
        /// <summary>当前运行时领域物品，初始化前为空。</summary>
        public Item Item { get; private set; }
        /// <summary>用于灰盒显示的实际米制尺寸。</summary>
        public Vector3 Size;
        /// <summary>空量酒器换端的显示时长，单位秒，不改变领域动作耗时。</summary>
        [Min(.05f)] public float JiggerFlipSeconds = .35f;
        /// <summary>旧场景兼容字段；实际搅拌深度现按勺头和内腔求解，参数在 BarStirSettings.json。</summary>
        [Min(0)] public float StirDepth = .06f;
        /// <summary>旧场景兼容字段；实际轨迹半径现按勺头与杯壁间余量求解。</summary>
        [Min(0)] public float StirRadius = .008f;
        /// <summary>量酒器换端或倒出显示期间暂缓接收新的交互。</summary>
        public bool IsAnimating => orientation && Item is Jigger j && (j.ActiveCup.Definition.Id != shownCup || flipElapsed < flipDuration || pourAnimation > 0);
        private Renderer[] renderers;
        private Transform liquid, strainerVisual, capVisual;
        private MaterialPropertyBlock[][] highlightBlocks;
        private bool highlighted;
        private float pourAnimation, pourDuration, flipElapsed, flipDuration;
        private Transform orientation;
        private Quaternion orientationBase, flipStart, flipPose;
        private string shownCup;
        private ItemView pourReceiver;
        private ContainerVisual[] containers;
        private float iceSide;
        private IceVisual toolIce;
        private SqueezerViewRig squeezerRig;
        private TongsViewRig tongsRig;
        private ScoopIceRig scoopRig;
        public Vector3 LoadedIceOrigin
        {
            get
            {
                if(toolIce){Transform top=null;foreach(var cube in toolIce.Cubes)if(cube.gameObject.activeSelf&&(!top||transform.InverseTransformPoint(cube.position).y>transform.InverseTransformPoint(top.position).y))top=cube;if(top)return top.position;}
                var hold=Anchor("HoldPoint");return (hold?hold.position:transform.position)+(Item is IceScoop?transform.up*iceSide:Vector3.zero);
            }
        }
        private ItemView pouringStrainer;
        private Transform strainerPourLip;
        private float? pressVisual;
        private StirPresentation stirring;
        public void SetPressVisual(float? amount)
        {
            pressVisual = amount;
        }
        private static Transform FindNode(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
        /// <summary>绑定领域物品并缓存灰盒可视节点。</summary>
        /// <precondition>item 非空，实例标识与本场景条目一致；灰盒网格和碰撞体已建立。</precondition>
        /// <effects>保存绑定、缓存渲染器和液面、隔冰器、上盖节点；不改变核心状态。</effects>
        public void Bind(Item item,double iceGrams,double iceDensity)
        {
            squeezerRig = GetComponent<SqueezerViewRig>();
            Item = item;Size=new Vector3((float)item.Definition.Size.WidthCm,(float)item.Definition.Size.HeightCm,(float)item.Definition.Size.DepthCm)*.01f;iceSide=IceAppearance.ConfiguredSide;containers=GetComponentsInChildren<ContainerVisual>(true);
            if(item is IceTongs)
            {
                tongsRig=GetComponent<TongsViewRig>();if(!tongsRig)tongsRig=gameObject.AddComponent<TongsViewRig>();
                tongsRig.Initialize(Anchor("HoldPoint"));
            }
            if(item is IceScoop){scoopRig=GetComponent<ScoopIceRig>();if(!scoopRig)scoopRig=gameObject.AddComponent<ScoopIceRig>();scoopRig.Initialize();}
            renderers = GetComponentsInChildren<MeshRenderer>(true);
            highlightBlocks = new MaterialPropertyBlock[renderers.Length][];
            orientation = transform.Find("Orientation");
            if (orientation && item is Jigger jigger)
            {
                orientationBase = orientation.localRotation;
                shownCup = jigger.ActiveCup.Definition.Id;
                flipPose = orientationBase * Quaternion.Euler(jigger.ActiveCup.NominalMl == 30 ? 180 : 0, 0, 0);
                flipStart = flipPose;
                orientation.localRotation = flipPose;
                flipDuration = Mathf.Max(.05f,JiggerFlipSeconds);
                flipElapsed = flipDuration;
            }
            liquid = transform.Find("LiquidVolume"); strainerVisual = transform.Find("StrainerVisual"); capVisual = transform.Find("CapVisual");
        }
        /// <summary>查询具名锚点，先查根节点的直接子节点，再查部件层级。</summary>
        /// <precondition>name 是模型协议中的完整锚点名。</precondition>
        /// <effects>返回 Transform 或空值，不移动节点。</effects>
        public Transform Anchor(string name){var direct=transform.Find(name);if(direct)return direct;foreach(var t in GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
        /// <summary>实际出液口世界位置：雪克壶按装配选择滤盖/壶身，量酒器使用朝上端；保留锚点编辑。</summary>
        public Vector3 PourPoint
        {
            get
            {
                if(Item is CobblerShaker shaker && !shaker.StrainerSlot.IsEmpty)
                {
                    var attached=shaker.StrainerSlot.Content;
                    if(!pouringStrainer || !ReferenceEquals(pouringStrainer.Item,attached) || !strainerPourLip)
                    {
                        pouringStrainer=null;strainerPourLip=null;
                        // Domain ownership chooses the part; a detached view can remain here until the next Update.
                        foreach(var part in GetComponentsInChildren<ItemView>(true))
                            if(ReferenceEquals(part.Item,attached)){pouringStrainer=part;strainerPourLip=part.Anchor("PourLip");break;}
                    }
                    if(strainerPourLip)return strainerPourLip.position;
                }
                string definition=Item is Jigger j?j.ActiveCup.Definition.Id:Item is CobblerShaker sh?sh.Body.Definition.Id:Item.Definition.Id;
                if(containers!=null)foreach(var visual in containers)if(visual.DefinitionId==definition){var lip=FindNode(visual.transform, "PourLip");if(lip)return lip.position;}
                var rootLip=Anchor(Item is Squeezer?"Outlet":"PourLip");return rootLip?rootLip.position:transform.position+transform.up*Size.y;
            }
        }
        /// <summary>更新当前准星目标高亮。</summary>
        /// <precondition>Bind 已完成，渲染器仍存在。</precondition>
        /// <effects>使用材质属性块改变基础颜色，不复制材质或修改物品数据。</effects>
        public void Highlight(bool selected)
        {
            if (renderers == null || highlighted == selected) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer) continue;
                if (!selected)
                {
                    if (highlightBlocks[i] == null) continue;
                    for (int slot = 0; slot < highlightBlocks[i].Length; slot++)
                        renderer.SetPropertyBlock(highlightBlocks[i][slot].isEmpty ? null : highlightBlocks[i][slot], slot);
                    highlightBlocks[i] = null;
                    continue;
                }
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.name == "InnerCavity" || renderer.name == "LiquidVolume") continue;
                bool helper = false;
                foreach (var binding in containers)
                    if (renderer == binding.LiquidVolume || binding.InnerCavity && renderer.transform == binding.InnerCavity.transform) helper = true;
                if (helper) continue;
                var materials = renderer.sharedMaterials;
                highlightBlocks[i] = new MaterialPropertyBlock[materials.Length];
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original, slot);
                    highlightBlocks[i][slot] = original;
                    var material = materials[slot];
                    if (!material) continue;
                    string colorName = material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;
                    if (colorName == null) continue;
                    var block = new MaterialPropertyBlock();
                    if (original.isEmpty) renderer.GetPropertyBlock(block);
                    else renderer.GetPropertyBlock(block, slot);
                    int property = Shader.PropertyToID(colorName);
                    var color = block.HasProperty(property) ? block.GetColor(property) : material.GetColor(property);
                    var tinted = Color.Lerp(color, new Color(1,.72f,.2f,color.a), Item is Fixture ? .08f : .18f);
                    block.SetColor(property, tinted);
                    renderer.SetPropertyBlock(block, slot);
                }
            }
            highlighted = selected;
        }
        /// <summary>开始量酒器整杯倒出的视图动画。</summary>
        /// <precondition>动作已在领域层接受，不用于判定实际转移量。</precondition>
        /// <effects>记录剩余动画秒数；动画仅读取，不调用 World。</effects>
        public void AnimatePour(float seconds, ItemView receiver = null) { pourDuration = Mathf.Max(.01f, seconds); pourAnimation = pourDuration; pourReceiver = receiver; }
        /// <summary>将吧勺 Tip 插入实际目标内腔，并按输入偏移沿口沿内侧搅拌。</summary>
        /// <precondition>搅拌动作有效，receiver 对应 target；工具含 Tip，容器绑定可读内腔。</precondition>
        /// <effects>仅改变工具显示姿态；保留握点，插入深度/半径按内腔限制，不修改液体或核心。</effects>
        public bool PoseStirring(ItemView receiver, Container target, Vector2 input, float tiltDegrees = 0, float lift = 0)
        {
            var binding = StirBinding(receiver, target);
            var tip = Anchor("Tip");
            var grip = Anchor("Grip_R");
            if (!grip) grip = Anchor("Grip_L");
            if (!binding || !binding.InnerCavity || !tip || !grip) return false;
            (stirring??=new StirPresentation()).Apply(this,binding,tip,grip,input,binding.Profile?binding.HeightForLiquid(target.Contents.LiquidMl):binding.Shape.Top,tiltDegrees,lift);
            return true;
        }
        /// <summary>最近一次搅拌姿态的求解结果（半径、抬升等），没有搅拌过时为空。</summary>
        public StirPresentation StirSolution => stirring;
        /// <summary>receiver 上与 target 对应的容器显示绑定。</summary>
        public static ContainerVisual StirBinding(ItemView receiver, Container target)
        {
            if (!receiver || target == null || receiver.containers == null) return null;
            foreach (var candidate in receiver.containers)
                if (candidate.DefinitionId == target.Definition.Id) return candidate;
            return null;
        }
        private MaterialPropertyBlock liquidBlock;
        private void LateUpdate()
        {
            if (Item == null) return;
            if (squeezerRig && Item is IProduceHolder holder)
            {
                squeezerRig.SetOpenAmount(holder.Produce == null ? 1 : .34f);
                if (pressVisual.HasValue) squeezerRig.SetPressAmount(pressVisual.Value);
            }
            if (Item is Jigger oriented)
            {
                if (orientation)
                {
                    if (shownCup != oriented.ActiveCup.Definition.Id)
                    { shownCup = oriented.ActiveCup.Definition.Id; flipStart = flipPose; flipDuration = Mathf.Max(.05f,JiggerFlipSeconds); flipElapsed = 0; }
                    flipElapsed = Mathf.Min(flipDuration, flipElapsed + Time.unscaledDeltaTime);
                    var target = orientationBase * Quaternion.Euler(oriented.ActiveCup.NominalMl == 30 ? 180 : 0, 0, 0);
                    flipPose = Quaternion.Slerp(flipStart, target, Mathf.SmoothStep(0,1,flipElapsed / flipDuration));
                    pourAnimation = Mathf.Max(0,pourAnimation-Time.unscaledDeltaTime);
                    orientation.localRotation = flipPose;
                }
            }
            if (Item is CobblerShaker shaker) { if (strainerVisual) strainerVisual.gameObject.SetActive(!shaker.StrainerSlot.IsEmpty); if (capVisual) capVisual.gameObject.SetActive(!shaker.CapSlot.IsEmpty); }
            foreach(var binding in containers)
            {
                Container container=Item is CobblerShaker sh?sh.Body:Item is Jigger jg?(binding.DefinitionId==jg.CupA.Definition.Id?jg.CupA:jg.CupB):Item as Container;
                bool active=container!=null&&(!(Item is Jigger jigger)||ReferenceEquals(container,jigger.ActiveCup));
                if(binding.LiquidVolume)
                {
                    binding.LiquidVolume.enabled=active&&container.Contents.LiquidMl>1e-8;
                    if(!binding.LiquidVolume.enabled){var surface=binding.GetComponent<LiquidSurface>();if(surface&&binding.InnerCavity)surface.Draw(binding.InnerCavity,0,binding.LiquidVolume.sharedMaterial,false);}
                    if(active&&binding.Profile){float height=binding.HeightForLiquid(container.Contents.LiquidMl);var block=liquidBlock??=new MaterialPropertyBlock();binding.LiquidVolume.GetPropertyBlock(block);block.SetFloat(BShaderIds.FillHeight,binding.transform.TransformPoint(Vector3.up*height).y);binding.LiquidVolume.SetPropertyBlock(block);var surface=binding.GetComponent<LiquidSurface>();if(!surface)surface=binding.gameObject.AddComponent<LiquidSurface>();surface.Draw(binding.InnerCavity,binding.transform.TransformPoint(Vector3.up*height).y,binding.LiquidVolume.sharedMaterial,binding.LiquidVolume.enabled);}
                }
                var ice=binding.GetComponent<IceVisual>();if(!ice)ice=binding.gameObject.AddComponent<IceVisual>();
                var bounds = new Bounds(Vector3.zero, Size);
                if (binding.InnerCavity)
                {
                    var meshBounds = binding.InnerCavity.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var vertex = meshBounds.center + Vector3.Scale(meshBounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        var local = binding.transform.InverseTransformPoint(binding.InnerCavity.transform.TransformPoint(vertex));
                        if (corner == 0) bounds = new Bounds(local, Vector3.zero);
                        else bounds.Encapsulate(local);
                    }
                }
                // A rectangular grid must fit inside the round cavity, including rotated cube corners.
                // The tool HoldPoint path below remains centred separately.
                if(!binding.GetComponent<LocalIceWorld>())ice.Show(active?container.Contents.IceCubes:0,iceSide,new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),bounds.size.x*.70710678f,bounds.size.z*.70710678f);
            }
            if(Item is Tool tool&&(Item is IceScoop||Item is IceTongs))
            {
                if (!toolIce)
                {
                    // Keep the generated ice under the metric root, outside the FBX's 100x scale.
                    var iceHolder = new GameObject("Held ice (snapshot)");
                    iceHolder.transform.SetParent(transform, false);
                    toolIce = iceHolder.AddComponent<IceVisual>();
                }
                var hold = Anchor("HoldPoint");
                toolIce.transform.SetPositionAndRotation(
                    hold ? hold.position : transform.TransformPoint(Vector3.up * Size.y * .7f),
                    scoopRig?scoopRig.HoldingRotation(hold):hold ? hold.rotation : transform.rotation);
                toolIce.ShowCentered(tool.LoadedCubes, iceSide);
                if(scoopRig)scoopRig.Arrange(toolIce,iceSide);
                if(tongsRig)tongsRig.Advance(tool.LoadedCubes>0,toolIce.transform,Time.unscaledDeltaTime);
            }
        }
    }
}
