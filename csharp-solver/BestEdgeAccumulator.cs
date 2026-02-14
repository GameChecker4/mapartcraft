namespace MapartSolving
{
    public class BestEdgeAccumulator
    {
        private UndirectedEdge? _bestEdge;
        private int _highestBenefit;
        private int _highestHeight;

        public BestEdgeAccumulator() => Reset();

        public bool TryGet(out UndirectedEdge bestEdge)
        {
            bestEdge = _bestEdge ?? default;
            return _bestEdge.HasValue;
        }

        public bool TryGetAndReset(out UndirectedEdge bestEdge)
        {
            bool hasEdge = TryGet(out bestEdge);
            Reset();
            return hasEdge;
        }

        public void AddToCompare(UndirectedEdge edge, int benefit, int height)
        {
            if (benefit > _highestBenefit || benefit == _highestBenefit
                && (height > _highestHeight || height == _highestHeight
                    && (_bestEdge == null || edge.CompareTo(_bestEdge.Value) < 0)))
            {
                _highestBenefit = benefit;
                _highestHeight = height;
                _bestEdge = edge;
            }
        }

        public void Reset()
        {
            _bestEdge = null;
            _highestBenefit = int.MinValue;
            _highestHeight = int.MinValue;
        }
    }
}