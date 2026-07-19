using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core.Monitor
{
    public class CoreHistory
    {
        private readonly Queue<float> _q;
        private readonly int _capacity;
        public CoreHistory(int capacity) { _capacity = capacity < 1 ? 1 : capacity; _q = new Queue<float>(_capacity); }
        public void Push(float v) { if (_q.Count >= _capacity) _q.Dequeue(); _q.Enqueue(v); }
        public float Min => _q.Count == 0 ? 0 : _q.Min();
        public float Max => _q.Count == 0 ? 0 : _q.Max();
        public float Avg => _q.Count == 0 ? 0 : _q.Average();
        public IReadOnlyCollection<float> Samples => _q;
    }
}
