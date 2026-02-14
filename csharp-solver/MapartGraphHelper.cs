using System;
using System.Collections.Generic;

namespace MapartSolving
{
    public static class MapartGraphHelper
    {
        /// <summary>
        /// Only works on correct graph. May enter into an infinite loop
        /// </summary>
        public static int[,] ComputeHeights(this MapartGraph graph)
        {
            var nodeHeights = graph.ComputeNodeHeights();
            return graph.ToGrid(node => nodeHeights[node]);
        }

        /// <summary>
        /// Only works on correct graph. May enter into an infinite loop
        /// </summary>
        public static int[] ComputeNodeHeights(this MapartGraph graph)
        {
            int[] nodeHeights = new int[graph.NodesCount];
            Array.Fill(nodeHeights, -1);
            _currentNodes.UnionWith(graph.FindStartNodes(true));
            UpdateNodeHeights(graph, nodeHeights, 0, out _, out _, false);

            return nodeHeights;
        }

        public static void UpdateNodeHeights(this MapartGraph graph, int[] heights, IEnumerable<int> nodes, int newHeight, out bool updated,
            out int brokenDiagonals)
        {
            _currentNodes.UnionWith(nodes);
            UpdateNodeHeights(graph, heights, newHeight, out updated, out brokenDiagonals, true);
        }

        private static HashSet<int> _currentNodes = new();
        private static HashSet<int> _nextNodes = new();
        private static void UpdateNodeHeights(this MapartGraph graph, int[] heights, int newHeight, out bool updatedSomething, out int brokenDiagonals, bool countBrokenDiagonals)
        {
            bool updated = false;
            int broken = 0;
            int currentHeight = newHeight;
            while (_currentNodes.Count > 0)
            {
                foreach (int node in _currentNodes)
                    ProcessNodeRecursive(node);
                currentHeight++;
                (_currentNodes, _nextNodes) = (_nextNodes, _currentNodes);
                _nextNodes.Clear();
                if (currentHeight > graph.Width * graph.Height)
                    throw new Exception("Invalid optimized graph");
                
            }

            updatedSomething = updated;
            brokenDiagonals = broken;
            return;

            void ProcessNodeRecursive(int node)
            {
                int prevHeight = heights[node]; 
                if (prevHeight >= currentHeight)
                    return;
                updated = true;
                heights[node] = currentHeight;
                if (countBrokenDiagonals)
                    foreach (var edge in graph.GetEdges(node))
                    {
                        if (edge.IsFlat)
                            continue;
                        if (edge.IsUp && heights[edge.ToNode] == currentHeight + 1)
                            broken--;
                        else if (!edge.IsUp && heights[edge.ToNode] == prevHeight - 1)
                            broken++;
                    }
                foreach (var edge in graph.GetEdges(node))
                {
                    if (edge.IsUp)
                        _nextNodes.Add(edge.ToNode);
                    else if (edge.IsEnabledFlat)
                        ProcessNodeRecursive(edge.ToNode);    
                }
            }
        }

        public static List<int> FindStartNodes(this MapartGraph graph, bool isLowestNodes)
        {
            List<int> startNodes = new();
            for (int node = 0; node < graph.NodesCount; node++)
            {
                bool startsLine = graph.NodesToGrid[node].MinY == 0;
                bool endsLine = graph.NodesToGrid[node].MaxY == graph.Height - 1;
                if (startsLine || endsLine)
                {
                    if (startsLine && endsLine)
                        startNodes.Add(node);
                    else
                        TryAdd(1);
                }
                else
                    TryAdd(2);

                continue;

                void TryAdd(int lastCount)
                {
                    var edges = graph.GetEdges(node);
                    if (edges.Length >= lastCount)
                        for (int i = 1; i <= lastCount; i++)
                            if (edges[^i].IsUp != isLowestNodes)
                                return;
                    startNodes.Add(node);
                }
            }

            return startNodes;
        }

        public delegate T Selector<out T>(int node);
        public static T[,] ToGrid<T>(this MapartGraph graph, Selector<T> selector)
        {
            var grid = new T[graph.Width, graph.Height];
            for (int node = 0; node < graph.NodesCount; node++)
            {
                int x = graph.NodesToGrid[node].X;
                for (int y = graph.NodesToGrid[node].MinY; y <= graph.NodesToGrid[node].MaxY; y++)
                    grid[x, y] = selector(node);
            }

            return grid;
        }

        public static int GetEdgeWide(this MapartGraph graph, int node1, int node2) =>
            Math.Min(graph.NodesToGrid[node1].MaxY, graph.NodesToGrid[node2].MaxY) -
            Math.Max(graph.NodesToGrid[node1].MinY, graph.NodesToGrid[node2].MinY) + 1;
    }
}