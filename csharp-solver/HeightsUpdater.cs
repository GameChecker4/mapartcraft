using System;
using System.Collections.Generic;
using System.Linq;

namespace MapartSolving
{
    public class HeightsUpdater
    {
        private readonly HashSet<int> _updatedNodes = new();
        private readonly HashSet<int> _historyConnectedNodes = new();
        private readonly HashSet<int> _historyDisconnectedNodes = new();
        private readonly HashSet<UndirectedEdge> _historyConnectedEdges = new();
        private readonly HashSet<UndirectedEdge> _historyDisconnectedEdges = new();
        private readonly HashSet<UndirectedEdge> _debugIntersection = new();

        private HashSet<int> _currentNodes = new();
        private HashSet<int> _nextNodes = new();
        
        public UpdateResult UpdateLikeEdgeIsActive(UndirectedEdge updatedEdge, MapartGraph graph,
            HeightsData<UpdateInfo> heights, bool countConnections, bool logHistory, bool logUpdatedNodes)
        {
            countConnections |= logHistory;
            logUpdatedNodes |= logHistory;
            _updatedNodes.Clear();
            _historyConnectedEdges.Clear();
            _historyDisconnectedEdges.Clear();

            int updatesCount = 0;
            int connections = 0;
            int currentHeight = Math.Max(heights[updatedEdge.MinNode], heights[updatedEdge.MaxNode]);
            _currentNodes.Add(updatedEdge.MinNode);
            _currentNodes.Add(updatedEdge.MaxNode);
            while (_currentNodes.Count > 0)
            {
                foreach (int node in _currentNodes)
                    ProcessNodeRecursive(node);
                
                currentHeight++;
                _currentNodes.Clear();
                (_currentNodes, _nextNodes) = (_nextNodes, _currentNodes);
            }

            heights.MaxHeight = Math.Max(heights.MaxHeight, currentHeight - 1);

            if (logHistory)
            {
                _debugIntersection.Clear();
                _debugIntersection.UnionWith(_historyConnectedEdges);
                _debugIntersection.IntersectWith(_historyDisconnectedEdges);
                if (_debugIntersection.Count > 0)
                    Debug.LogWarning(
                        $"updatedEdge:{updatedEdge.MinNode}-{updatedEdge.MaxNode}, intersections: {_debugIntersection.Count}");
                _historyConnectedEdges.ExceptWith(_debugIntersection);
                _historyDisconnectedEdges.ExceptWith(_debugIntersection);
                var edgesConnections = _historyConnectedEdges.Aggregate(0, (prev, e) => prev + GetEdgeConnectionsCost(e, graph))
                                       - _historyDisconnectedEdges.Aggregate(0, (prev, e) => prev + GetEdgeConnectionsCost(e, graph));

                if (edgesConnections != connections)
                    Debug.LogWarning(
                        $"updatedEdge:{updatedEdge.MinNode}-{updatedEdge.MaxNode}, connections: {connections}, edgeConnections: {edgesConnections}");

                _historyConnectedNodes.Clear();
                foreach (var e in _historyConnectedEdges)
                {
                    if (_updatedNodes.Contains(e.MinNode) && _updatedNodes.Contains(e.MaxNode))
                    {
                        _historyConnectedNodes.Add(e.MinNode);
                        _historyConnectedNodes.Add(e.MaxNode);
                    }
                    else if (!_updatedNodes.Contains(e.MinNode))
                        _historyConnectedNodes.Add(e.MinNode);
                    else if (!_updatedNodes.Contains(e.MaxNode))
                        _historyConnectedNodes.Add(e.MaxNode);
                }

                _historyDisconnectedNodes.Clear();
                foreach (var e in _historyDisconnectedEdges)
                {
                    if (_updatedNodes.Contains(e.MinNode) && _updatedNodes.Contains(e.MaxNode))
                    {
                        _historyDisconnectedNodes.Add(e.MinNode);
                        _historyDisconnectedNodes.Add(e.MaxNode);
                    }
                    else if (!_updatedNodes.Contains(e.MinNode))
                        _historyDisconnectedNodes.Add(e.MinNode);
                    else if (!_updatedNodes.Contains(e.MaxNode))
                        _historyDisconnectedNodes.Add(e.MaxNode);
                }
            }

            heights.UpdateInfo = new UpdateInfo
            {
                NewConnections = countConnections ? connections : 0,
                UpdatedSomething = updatesCount > 0,
                IsCycled = heights[updatedEdge.MinNode] != heights[updatedEdge.MaxNode],
            };

            return new UpdateResult
            {
                UpdatedHeights = heights,
                UpdatedNodes = logUpdatedNodes ? _updatedNodes : null,
                History = logHistory ? new UpdateResult.HistoryTempData
                {
                    ConnectedNodes = _historyConnectedNodes,
                    DisconnectedNodes = _historyDisconnectedNodes,
                } : null,
            };
            
            void ProcessNodeRecursive(int node)
            {
                int prevHeight = heights[node]; 
                if (prevHeight >= currentHeight)
                    return;
                updatesCount++;
                if (logUpdatedNodes)
                    _updatedNodes.Add(node);
                heights[node] = currentHeight;
                if (countConnections)
                    foreach (var edge in graph.GetEdges(node))
                    {
                        var undirectedEdge = new UndirectedEdge(node, edge.ToNode);
                        if (!edge.IsFlat)
                        {
                            if (edge.IsUp)
                            {
                                if (heights[edge.ToNode] > prevHeight + 1 && heights[edge.ToNode] <= currentHeight + 1)
                                {
                                    connections++;
                                    if (logHistory)
                                        if (!_historyDisconnectedEdges.Remove(undirectedEdge))
                                            if (!_historyConnectedEdges.Add(undirectedEdge))
                                                Debug.LogError($"Edge {undirectedEdge.MaxNode}-{undirectedEdge.MaxNode} is already connected");
                                }
                            }
                            else if (heights[edge.ToNode] >= prevHeight - 1 && heights[edge.ToNode] < currentHeight - 1)
                            {
                                connections--;
                                if (logHistory)
                                    if (!_historyConnectedEdges.Remove(undirectedEdge))
                                        if (!_historyDisconnectedEdges.Add(undirectedEdge))
                                            Debug.LogError($"Edge {undirectedEdge.MaxNode}-{undirectedEdge.MaxNode} is already disconnected");
                            }
                        }
                        else if (edge.IsDisabledFlat)
                        {
                            if (heights[edge.ToNode] == currentHeight)
                            {
                                connections += graph.GetEdgeWide(undirectedEdge.MinNode, undirectedEdge.MaxNode);
                                if (logHistory)
                                    if (!_historyDisconnectedEdges.Remove(undirectedEdge))
                                        if (!_historyConnectedEdges.Add(undirectedEdge))
                                            Debug.LogError($"Edge {undirectedEdge.MaxNode}-{undirectedEdge.MaxNode} is already connected");
                            }
                            else if (heights[edge.ToNode] == prevHeight)
                            {
                                connections -= graph.GetEdgeWide(undirectedEdge.MinNode, undirectedEdge.MaxNode);
                                if (logHistory)
                                    if (!_historyConnectedEdges.Remove(undirectedEdge))
                                        if (!_historyDisconnectedEdges.Add(undirectedEdge))
                                            Debug.LogError($"Edge {undirectedEdge.MaxNode}-{undirectedEdge.MaxNode} is already disconnected");
                            }
                        }
                    }
                foreach (var edge in graph.GetEdges(node))
                    if (edge.IsUp)
                        _nextNodes.Add(edge.ToNode);
                foreach (var edge in graph.GetEdges(node))
                    if (edge.IsEnabledFlat)
                        ProcessNodeRecursive(edge.ToNode);
            }
        }
        
        private static int GetEdgeConnectionsCost(UndirectedEdge edge, MapartGraph graph)
        {
            return Math.Abs(graph.NodesToGrid[edge.MinNode].X - graph.NodesToGrid[edge.MaxNode].X) switch
            {
                0 => 1,
                1 => graph.GetEdgeWide(edge.MinNode, edge.MaxNode),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public struct UpdateInfo
        {
            public int? NewConnections;
            public bool UpdatedSomething;
            public bool IsCycled;
        }

        public struct UpdateResult
        {
            public HeightsData<UpdateInfo> UpdatedHeights;
            
            public HistoryTempData? History;
            
            public HashSet<int>? UpdatedNodes;

            public struct HistoryTempData
            {
                public HashSet<int> ConnectedNodes;
                public HashSet<int> DisconnectedNodes;
            }
        }
    }
}