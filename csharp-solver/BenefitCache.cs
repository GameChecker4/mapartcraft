using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MapartSolving
{
    public class BenefitCache : IEnumerable<KeyValuePair<UndirectedEdge, BenefitCache.Data>>
    {
        private readonly Dictionary<UndirectedEdge, Data> _data = new();
        public readonly List<UndirectedEdge> DirtyEdges = new();

        public Data Get(UndirectedEdge edge) => _data[edge];
        public bool TryGet(UndirectedEdge edge, out Data data) => _data.TryGetValue(edge, out data);
        public bool TryAdd(UndirectedEdge edge, Data data) => _data.TryAdd(edge, data);
        public void Remove(UndirectedEdge edge) => _data.Remove(edge);

        public void MarkDirty(UndirectedEdge edge)
        {
            if (_data.Remove(edge))
                DirtyEdges.Add(edge);
        }

        public void MarkAllDirty()
        {
            DirtyEdges.AddRange(_data.Keys);
            _data.Clear();
        }

        public void Clear()
        {
            _data.Clear();
            DirtyEdges.Clear();
        }

        public void CopyCacheTo(BenefitCache other)
        {
            other._data.Clear();
            foreach (var pair in _data)
                other._data[pair.Key] = pair.Value;
        }

        private readonly List<UndirectedEdge> _comparisonResults = new();
        public List<UndirectedEdge> CompareWithValid(BenefitCache validCache, out int timesOfValidCacheIsNotExists)
        {
            _comparisonResults.Clear();
            timesOfValidCacheIsNotExists = 0;
            foreach (var edge in _data.Keys)
            {
                var data = _data[edge];
                if (!validCache.TryGet(edge, out var validData))
                {
                    timesOfValidCacheIsNotExists++;
                    continue;
                }
                if (data.Benefit != validData.Benefit ||
                    data.UpdatedSomething != validData.UpdatedSomething ||
                    data.NewConnections != validData.NewConnections ||
                    data.DebugUpdatedNodes!
                        .Zip(validData.DebugUpdatedNodes!, (a, b) => (a, b))
                        .Any(c => !c.a.Equals(c.b)))
                {
                    _comparisonResults.Add(edge);
                }
            }
            return _comparisonResults;
        }

        public IEnumerator<KeyValuePair<UndirectedEdge, Data>> GetEnumerator() => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Data
        {
            public int Benefit;
            public bool UpdatedSomething;
            public int NewConnections;
            public int MaxHeight;
            public int EdgeHeight;
            public List<(int node, int height)>? DebugUpdatedNodes;
        }
    }
}