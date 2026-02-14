using System.Linq;

namespace MapartSolving
{
    public class EdgeChecker
    {
        private readonly MapartGraph _graph;
        private readonly HeightsData<HeightsUpdater.UpdateInfo> _initialHeights;

        private readonly HeightsUpdater _heightsUpdater = new();
        private readonly HeightsData<HeightsUpdater.UpdateInfo> _heights;

        public bool FindCacheOptimizationErrors { get; set; }

        public EdgeChecker(MapartGraph graph, HeightsData<HeightsUpdater.UpdateInfo> heights)
        {
            _graph = graph;
            _initialHeights = heights;
            _heights = new HeightsData<HeightsUpdater.UpdateInfo>(heights.Heights.Length);
        }

        public BenefitCache.Data Check(UndirectedEdge edge, out bool isCycled)
        {
            _initialHeights.CopyTo(_heights);
            var updateResult = _heightsUpdater.UpdateLikeEdgeIsActive(edge, _graph, _heights, true, false, FindCacheOptimizationErrors);
            var updatedHeights = updateResult.UpdatedHeights;
            var updInfo = updatedHeights.UpdateInfo;

            int edgeHeight = updatedHeights.Heights[edge.MinNode];
            isCycled = updInfo.IsCycled;

            int benefit = updInfo.UpdatedSomething ? updInfo.NewConnections!.Value : _graph.GetEdgeWide(edge.MinNode, edge.MaxNode);

            return new BenefitCache.Data
            {
                Benefit = benefit,
                UpdatedSomething = updInfo.UpdatedSomething,
                NewConnections = updInfo.NewConnections ?? 0,
                MaxHeight = updatedHeights.MaxHeight,
                EdgeHeight = edgeHeight,
                DebugUpdatedNodes = FindCacheOptimizationErrors
                    ? updateResult.UpdatedNodes!.Select(n => (n, updatedHeights[n])).ToList()
                    : null,
            };
        }
    }
}