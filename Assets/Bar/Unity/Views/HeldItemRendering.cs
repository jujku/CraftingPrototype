using System.Collections.Generic;
using System.Linq;
using Bar.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity
{
    /// <summary>手持及当前倒液/搅拌对象的独立相机层；清除世界深度后绘制，保持原光学管线与物品间深度。</summary>
    [DefaultExecutionOrder(150)]public sealed class HeldItemRendering:MonoBehaviour
    {
        private BarSession session;private Camera world,overlay;private int originalMask;private bool originalPost,postTransferred;
        private UniversalAdditionalCameraData worldData,overlayData;
        private PlayerEnvironmentSettings settings;
        private readonly Dictionary<Transform,int> originalLayers=new Dictionary<Transform,int>();
        private readonly List<Transform> hierarchy=new List<Transform>(256),remove=new List<Transform>(256);private readonly List<Collider> colliders=new List<Collider>(8);private readonly List<PourRibbon> streams=new List<PourRibbon>(4);private CharacterController body;
        private readonly HashSet<Transform> wanted=new HashSet<Transform>();
        private ForegroundShadowBridge shadows;
        public int ShadowProxyCount=>shadows?.ActiveCount??0;
        public Camera OverlayCamera=>overlay;
        public bool HasForeground=>wanted.Count>0;
        public void Initialize(BarSession owner)
        {
            if(world)return;session=owner;body=owner.Player.GetComponent<CharacterController>();world=owner.Player.ViewCamera;settings=PlayerEnvironmentSettings.Load();
            if(settings.Held<0||settings.Liquid<0)throw new System.InvalidOperationException("Missing BarHeld/BarHeldLiquid layers");
            originalMask=world.cullingMask;worldData=world.GetUniversalAdditionalCameraData();originalPost=worldData.renderPostProcessing;
            var go=new GameObject("Held items foreground camera");go.transform.SetParent(world.transform,false);overlay=go.AddComponent<Camera>();overlay.CopyFrom(world);
            overlayData=overlay.GetUniversalAdditionalCameraData();overlayData.renderType=CameraRenderType.Overlay;overlay.clearFlags=CameraClearFlags.Depth;
            overlayData.requiresColorOption=CameraOverrideOption.On;overlayData.requiresDepthOption=CameraOverrideOption.On;
            worldData.cameraStack.Add(overlay);world.cullingMask=originalMask&~settings.Mask;
            shadows=new ForegroundShadowBridge(transform,settings);
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;Refresh();
        }
        private void BeforeCamera(ScriptableRenderContext context,Camera camera){if(camera==world)Refresh();}
        private void LateUpdate(){if(world)Refresh();}
        private void Add(Transform root){if(!root)return;root.GetComponentsInChildren(true,hierarchy);foreach(var t in hierarchy)wanted.Add(t);}
        public void Refresh()
        {
            if(!session||!world||!overlay)return;wanted.Clear();
            foreach(var view in session.Views)if(view&&view.Item!=null&&session.Hands.SideOf(view.Item).HasValue)Add(view.transform);
            var presentation=session.PourDisplay?.Presentation;
            var receiver=presentation?.DisplayReceiver;
            if(!receiver&&session.Clock.Active is {IsEnded:false} active&&PourPresentation.Supported(active.Action.Id))
            {var item=active.Target;while(item?.Parent!=null)item=item.Parent;receiver=session.Views.FirstOrDefault(v=>v.Item==item);}
            // Keep the vessel in the spoon's depth buffer through entry, stirring and lift-out.
            // StirMotion retains its target during Return, after the core action has ended.
            if(!receiver&&session.StirDisplay?.Target is Container stirringVessel)
            {Item item=stirringVessel;while(item.Parent!=null)item=item.Parent;receiver=session.Views.Single(v=>v.Item==item);}
            if(receiver&&receiver.Item is not Fixture)Add(receiver.transform);
            if(wanted.Count>0){session.GetComponents(streams);foreach(var stream in streams)foreach(var root in stream.RenderRoots)Add(root);}
            remove.Clear();foreach(var t in originalLayers.Keys)
            {
                if(t&&wanted.Contains(t))continue;
                if(t){t.gameObject.layer=originalLayers[t];SetBodyIgnore(t,false);var patch=t.GetComponent<IceTransmissionPatch>();if(patch)patch.SyncLayer();}remove.Add(t);
            }
            foreach(var t in remove)originalLayers.Remove(t);
            foreach(var t in wanted)
            {
                if(!originalLayers.ContainsKey(t))originalLayers.Add(t,t.gameObject.layer);
                t.gameObject.layer=originalLayers[t]==31?settings.Liquid:settings.Held;SetBodyIgnore(t,true);
            }
            foreach(var t in wanted){var patch=t.GetComponent<IceTransmissionPatch>();if(patch)patch.SyncLayer();}
            bool activeCamera=wanted.Count>0;overlay.CopyFrom(world);overlay.transform.localPosition=Vector3.zero;overlay.transform.localRotation=Quaternion.identity;
            overlay.cullingMask=settings.Mask;overlay.enabled=activeCamera;overlayData.renderType=CameraRenderType.Overlay;overlay.clearFlags=CameraClearFlags.Depth;
            // Transfer post processing only on ownership changes; leave explicit base-camera
            // settings alone when no foreground is present (also needed by optical diagnostics).
            if(activeCamera&&!postTransferred){originalPost=worldData.renderPostProcessing;worldData.renderPostProcessing=false;postTransferred=true;}
            else if(!activeCamera&&postTransferred){worldData.renderPostProcessing=originalPost;postTransferred=false;}
            overlayData.renderPostProcessing=activeCamera&&originalPost;
            shadows?.Sync(activeCamera,receiver&&receiver.Item.Location.Kind==LocationKind.Surface?receiver.transform:null,originalLayers);
        }
        private void SetBodyIgnore(Transform t,bool ignore)
        {if(!session||!session.Player)return;if(!body)return;t.GetComponents(colliders);foreach(var collider in colliders)if(collider&&collider!=body)Physics.IgnoreCollision(body,collider,ignore);}
        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            shadows?.Dispose();
            foreach(var pair in originalLayers)if(pair.Key){pair.Key.gameObject.layer=pair.Value;SetBodyIgnore(pair.Key,false);}
            if(world){world.cullingMask=originalMask;if(worldData){if(postTransferred)worldData.renderPostProcessing=originalPost;if(overlay)worldData.cameraStack.Remove(overlay);}}
            if(overlay)Destroy(overlay.gameObject);
        }
    }
}
