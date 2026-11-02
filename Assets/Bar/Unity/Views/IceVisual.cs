using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读核心等效冰块数，用完整块和按体积缩放的余块表现融化。</summary>
    public sealed class IceVisual : MonoBehaviour
    {
        private readonly List<Transform> cubes=new List<Transform>();
        public IReadOnlyList<Transform> Cubes=>cubes;
        /// <summary>显示指定冰块数。</summary>
        /// <precondition>数量非负；side 为 BarCues 中完整冰块的米制边长。</precondition>
        /// <effects>复用最多 128 个 BarKit 冰模型，最后一块按剩余体积开立方缩放；不修改质量。</effects>
        public void Show(double count,float side,Vector3 bottom,float width,float depth)
        {
            int visible=Mathf.Min(128,Mathf.CeilToInt((float)count));
            while(cubes.Count<visible){var go=IceAppearance.Create(cubes.Count,transform);go.name="Ice cube (snapshot)";cubes.Add(go.transform);}
            int cols=Mathf.Max(1,Mathf.FloorToInt(width/side)),rows=Mathf.Max(1,Mathf.FloorToInt(depth/side));
            for(int i=0;i<cubes.Count;i++){cubes[i].gameObject.SetActive(i<visible);if(i>=visible)continue;float fraction=(float)System.Math.Min(1,System.Math.Max(0,count-i));cubes[i].localScale=Vector3.one*side*Mathf.Pow(fraction,1f/3f);cubes[i].localPosition=bottom+new Vector3(((i%cols)-(cols-1)*.5f)*side,side*(.5f+i/(cols*rows)),((i/cols%rows)-(rows-1)*.5f)*side);cubes[i].localRotation=Quaternion.Euler(i*13%17,i*37%90,i*11%13);}
        }
        /// <summary>将工具持冰的网格中心排列在本节点原点。</summary>
        /// <precondition>数量非负，side 为完整冰块的米制边长；宿主应位于米制根下。</precondition>
        /// <effects>单块中心精确位于原点，多块紧凑排列后按可见块中心平均值居中；不修改冰量。</effects>
        public void ShowCentered(double count, float side)
        {
            Show(count, side, Vector3.zero, side * 2, side * 2);
            int visible = Mathf.Min(128, Mathf.CeilToInt((float)count));
            if (visible == 0) return;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < visible; i++) centre += cubes[i].localPosition;
            centre /= visible;
            for (int i = 0; i < visible; i++) cubes[i].localPosition -= centre;
        }
    }
}
