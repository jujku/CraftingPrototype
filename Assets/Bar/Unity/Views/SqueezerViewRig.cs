using UnityEngine;
namespace Bar.Unity
{
    /// <summary>BarKit 榨汁器开合视图；只改变可见部件姿态，不产生液体。</summary>
    public sealed class SqueezerViewRig : MonoBehaviour
    {
        public enum FruitMode { Lemon, Lime }
        public FruitMode Mode = FruitMode.Lemon;
        [Range(0f, 1f)] public float OpenAmount;
        private Transform upper;
        private Transform middle;
        private Quaternion upperRest;
        private Quaternion middleRest;
        private Vector3 upperAxis;
        private Vector3 middleAxis;
        private float upperAngle;
        private float middleAngle;
        private bool ready;
        private float? inspection;
        private readonly SecondOrderFloat inspectionReturn=new SecondOrderFloat(12,1,0,1);
        private bool returning;
        private float? inspectionPeek;
        private readonly SecondOrderFloat peekReturn=new SecondOrderFloat(12,1,0,0);
        private bool peekReturning;
        /// <summary>只抬上杠杆窥视柠檬；不动承托碗、LoadSlot、Produce或压汁状态。</summary>
        public void SetPeekInspection(float? additionalDegrees)
        {
            if(!additionalDegrees.HasValue&&inspectionPeek.HasValue){peekReturn.Reset(inspectionPeek.Value);peekReturning=true;}
            inspectionPeek=additionalDegrees;if(additionalDegrees.HasValue)peekReturning=false;if(ready)Apply();
        }
        /// <summary>仅覆盖空榨汁器的可见开度，不改装载/压汁状态。</summary>
        public void SetInspection(float? amount)
        {
            if(!amount.HasValue&&inspection.HasValue){inspectionReturn.Reset(inspection.Value);returning=true;}
            inspection=amount;if(amount.HasValue)returning=false;
            if(ready)Apply();
        }
        private void Awake()
        {
            Bind();
            Apply();
        }
        private void LateUpdate()
        {
            AdvanceInspectionReturn(Time.unscaledDeltaTime);
            if (ready) Apply();
        }
        public void AdvanceInspectionReturn(float dt)
        {
            float left=dt;while(left>0){float h=Mathf.Min(left,1f/240);if(returning)inspectionReturn.Update(h,OpenAmount);if(peekReturning)peekReturn.Update(h,0);left-=h;}
            if(returning&&Mathf.Abs(inspectionReturn.Value-OpenAmount)<.0001f)returning=false;
            if(peekReturning&&Mathf.Abs(peekReturn.Value)<.0001f)peekReturning=false;
            if(ready)Apply();
        }
        /// <summary>把装载开合限幅到零到一；只更新姿态。</summary>
        public void SetOpenAmount(float value)
        {
            OpenAmount = Mathf.Clamp01(value);
            if (ready) Apply();
        }
        /// <summary>把下压进度映射为工作开度；本方法不扣汁。</summary>
        public void SetPressAmount(float pressure)
        {
            SetOpenAmount(.34f * (1f - Mathf.Clamp01(pressure)));
        }
        private Transform Node(string name)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name == name || child.name.EndsWith("::" + name)) return child;
            return null;
        }
        private bool Configure(Transform pivot, string prefix, out Vector3 axis, out float angle)
        {
            axis = Vector3.right;
            angle = 0;
            var closed = Node(prefix + "ClosedRay");
            var opened = Node(prefix + "OpenRay");
            if (!pivot || !pivot.parent || !closed || !opened) return false;
            var origin = pivot.localPosition;
            var from = (pivot.parent.InverseTransformPoint(closed.position) - origin).normalized;
            var to = (pivot.parent.InverseTransformPoint(opened.position) - origin).normalized;
            // FBX helper coordinates can be millimetric before the imported root scale.
            // Normalize rays before crossing so Unity's near-zero threshold does not erase the axis.
            axis = Vector3.Cross(from, to).normalized;
            angle = Vector3.Angle(from, to);
            return axis.sqrMagnitude > .9f;
        }
        private void Bind()
        {
            upper = Node("UpperPivot");
            middle = Node("MiddlePivot");
            ready = Configure(upper, "Upper", out upperAxis, out upperAngle) &&
                Configure(middle, "Middle", out middleAxis, out middleAngle);
            if (!ready)
            {
                Debug.LogError("SqueezerViewRig: missing hinge or motion reference nodes.", this);
                return;
            }
            upperRest = upper.localRotation;
            middleRest = middle.localRotation;
        }
        private void Apply()
        {
            if (!ready) return;
            float amount = Mathf.Clamp01(inspection??(returning?inspectionReturn.Value:OpenAmount));
            upper.localRotation = Quaternion.AngleAxis(upperAngle * amount+(inspectionPeek??(peekReturning?peekReturn.Value:0)), upperAxis) * upperRest;
            middle.localRotation = Quaternion.AngleAxis(Mode == FruitMode.Lemon ? middleAngle * amount : 0, middleAxis) * middleRest;
        }
    }
}
