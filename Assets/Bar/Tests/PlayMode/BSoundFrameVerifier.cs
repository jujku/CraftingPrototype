using System.Collections.Generic;
using Bar.Unity;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>和实际呈现帧一致，在倒液/线索 LateUpdate 之后核对声音同步。</summary>
    [DefaultExecutionOrder(500)]public sealed class BSoundFrameVerifier:MonoBehaviour
    {
        public BarSession Session;public int FlowFrames;public readonly List<string> Errors=new List<string>();
        private void LateUpdate(){var p=Session.GetComponent<PourPresentationDriver>()?.Presentation;if(p?.Visible==true){FlowFrames++;if(!Session.Sound.PourAudible)Errors.Add("visible flow without pour sample");}}
    }
}
