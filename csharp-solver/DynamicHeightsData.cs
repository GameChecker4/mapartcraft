namespace MapartSolving
{
    public class DynamicHeightsData
    {
        private readonly MapartGraph _graph;
        private readonly HeightsUpdater _heightsUpdater = new();
        private readonly HeightsData<HeightsUpdater.UpdateInfo> _heights;
        private readonly HeightsData<HeightsUpdater.UpdateInfo> _releasedHeights;

        public HeightsData<HeightsUpdater.UpdateInfo> Heights => _releasedHeights;

        public DynamicHeightsData(MapartGraph graph)
        {
            _graph = graph;
            _releasedHeights = new HeightsData<HeightsUpdater.UpdateInfo>(graph.ComputeNodeHeights());
            _heights = new HeightsData<HeightsUpdater.UpdateInfo>(_releasedHeights);
        }

        public DynamicHeightsData(DynamicHeightsData dynamicHeightsData)
        {
            _graph = dynamicHeightsData._graph;
            _releasedHeights = new HeightsData<HeightsUpdater.UpdateInfo>(dynamicHeightsData._releasedHeights);
            _heights = new HeightsData<HeightsUpdater.UpdateInfo>(dynamicHeightsData._releasedHeights);
        }

        public HeightsUpdater.UpdateResult UpdateLikeEdgeIsActive(UndirectedEdge edge, bool logHistory)
        {
            return _heightsUpdater.UpdateLikeEdgeIsActive(edge, _graph, _releasedHeights, logHistory, logHistory, true);
        }

        public HeightsUpdater.UpdateResult CheckLikeUpdate(UndirectedEdge edge, bool logUpdatedNodes)
        {
            _releasedHeights.CopyTo(_heights);
            return _heightsUpdater.UpdateLikeEdgeIsActive(edge, _graph, _heights, true, false, logUpdatedNodes);
        }

        public HeightsUpdater.UpdateResult CheckLikeUpdateDetailed(UndirectedEdge edge)
        {
            _releasedHeights.CopyTo(_heights);
            return _heightsUpdater.UpdateLikeEdgeIsActive(edge, _graph, _heights, true, true, true);
        }
    }
}