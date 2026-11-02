using System;
namespace Bar.Domain
{
    /// <summary>只转发领域事件的会话总线；观察数据由宿主在事件发生时冻结。</summary>
    public sealed class DomainEventStream
    {
        public event Action<DomainEvent> Published;
        /// <summary>同步转发一个领域事件。</summary>
        /// <precondition>value 非 null。</precondition>
        /// <effects>调用当前 Published 订阅者，不解释观察包。</effects>
        public void Publish(DomainEvent value) { if(value==null)throw new ArgumentNullException(nameof(value)); Published?.Invoke(value); }
    }
}
