using System;
using System.Collections.Generic;
using System.Linq;
using static MapartSolving.GridUtils;

namespace MapartSolving
{
    public class MapartGraph
    {
        private readonly SharedArray<Edge> _nodes;

        public NodeToGrid[] NodesToGrid { get; }
        private readonly int[,] _gridToNode;
        public int Width => _gridToNode.GetLength(0);
        public int Height => _gridToNode.GetLength(1);
        public int NodesCount => _nodes.ArraysCount;

        #region Graph Creation
        public static MapartGraph CreateGraphWithoutFlatEdges(Direction[,] input)
        {
            int nodesCount = CountNodes(input, out int[,] gridToNode);
            var nodesToGrid = MapNodesToGrid(gridToNode, nodesCount, input);
            var nodes = CreateGraph(nodesToGrid, gridToNode, input, true);
            return new MapartGraph(nodes, nodesToGrid, gridToNode);
        }
    
        private static int CountNodes(Direction[,] input, out int[,] gridToNode)
        {
            GetSizes(input, out int width, out int inputHeight);
            gridToNode = new int[width, inputHeight + 1];
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                gridToNode[x, 0] = count++;
                for (int y = 0; y < inputHeight; y++)
                    gridToNode[x, y + 1] = input[x, y] == Direction.Flat ?  gridToNode[x, y] : count++;
            }

            return count;
        }

        private static NodeToGrid[] MapNodesToGrid(int[,] gridToNode, int nodesCount, Direction[,] input)
        {
            GetSizes(input, out int width, out int inputHeight);
            int height = inputHeight + 1;
        
            var nodesToGrid = new NodeToGrid[nodesCount];

            for (int x = 0; x < width; x++)
            {
                int currentNode = gridToNode[x, 0];
                nodesToGrid[currentNode].X = x;
                nodesToGrid[currentNode].MinY = 0;

                for (int y = 1; y < height; y++)
                {
                    if (gridToNode[x, y] == currentNode)
                        continue;
                    nodesToGrid[currentNode].MaxY = y - 1;
                
                    currentNode = gridToNode[x, y];
                    nodesToGrid[currentNode].X = x;
                    nodesToGrid[currentNode].MinY = y;
                }
            
                nodesToGrid[currentNode].MaxY = height - 1;
            }

            return nodesToGrid;
        }

        private static SharedArray<Edge> CreateGraph(NodeToGrid[] nodesToGrid, int[,] gridToNode, Direction[,] input,
            bool disableFlatEdges)
        {
            GetSizes(gridToNode, out int width, out int height);
            var edgesList = new List<Edge>();
            var nodesStarts = new List<int>();

            for (int currentNode = 0; currentNode < nodesToGrid.Length; currentNode++)
            {
                var nodeToGrid = nodesToGrid[currentNode];
                int x = nodeToGrid.X;
                nodesStarts.Add(edgesList.Count);

            
                if (x > 0)
                    AddFlatEdges(x - 1);
                if (x < width - 1)
                    AddFlatEdges(x + 1);
                if (nodeToGrid.MinY > 0)
                {
                    bool isEdgeUp = input[x, nodeToGrid.MinY - 1] == Direction.Down;
                    edgesList.Add(new Edge(gridToNode[x, nodeToGrid.MinY - 1], isEdgeUp ? EdgeType.Up : EdgeType.Down));
                }

                if (nodeToGrid.MaxY < height - 1)
                {
                    bool isEdgeUp = input[x, nodeToGrid.MaxY] == Direction.Up;
                    edgesList.Add(new Edge(gridToNode[x, nodeToGrid.MaxY + 1], isEdgeUp ? EdgeType.Up : EdgeType.Down));
                }

                continue;

                void AddFlatEdges(int targetX)
                {
                    var flatEdgeType = disableFlatEdges ? EdgeType.DisabledFlat : EdgeType.Flat;
                    edgesList.Add(new Edge(gridToNode[targetX, nodeToGrid.MinY], flatEdgeType));
                    for (int y = nodeToGrid.MinY + 1; y <= nodeToGrid.MaxY; y++)
                    {
                        int node = gridToNode[targetX, y];
                        if (edgesList[^1].ToNode == node) 
                            continue;
                        edgesList.Add(new Edge(node, flatEdgeType));
                    }
                }
            }

            return SharedArray<Edge>.Create(nodesStarts, edgesList);
        }
        #endregion

        private MapartGraph(SharedArray<Edge> nodes, NodeToGrid[] nodesToGrid, int[,] gridToNode)
        {
            _nodes = nodes;
            NodesToGrid = nodesToGrid;
            _gridToNode = gridToNode;
        }

        public MapartGraph(MapartGraph graph)
        {
            _nodes = graph._nodes.ClonePointers(e => e);
            NodesToGrid = graph.NodesToGrid.ToArray();
            _gridToNode = graph._gridToNode;
        }

        public int GetNodeId(int x, int y) => _gridToNode[x, y];

        public ReadOnlySpan<Edge> GetEdges(int node) => GetWritableEdges(node);
        private Span<Edge> GetWritableEdges(int node) => _nodes[node];

        public Edge GetEdge(int fromNode, int toNode)
        {
            foreach (var edge in GetEdges(fromNode))
                if (edge.ToNode == toNode)
                    return edge;

            throw new ArgumentException();
        }

        public bool SetFlatEdge(int node1, int node2, bool enableEdge)
        {
            bool removed1 = FindAndProcessEdge(node1, node2);
            bool removed2 = FindAndProcessEdge(node2, node1);
            return removed1 && removed2;

            bool FindAndProcessEdge(int fromNode, int toNode)
            {
                foreach (ref var edge in GetWritableEdges(fromNode))
                    if (edge.ToNode == toNode)
                    {
                        if (edge.EdgeType != (enableEdge ? EdgeType.DisabledFlat : EdgeType.Flat))
                            break;
                        edge.EdgeType = enableEdge ? EdgeType.Flat : EdgeType.DisabledFlat;
                        return true;
                    }

                return false;
            }
        }

        public struct Edge
        {
            public Edge(int toNode, EdgeType edgeType)
            {
                ToNode = toNode;
                EdgeType = edgeType;
            }

            public int ToNode { get; }
            public EdgeType EdgeType { get; set; }
            public bool IsFlat => EdgeType is EdgeType.Flat or EdgeType.DisabledFlat;
            public bool IsEnabledFlat => EdgeType is EdgeType.Flat;
            public bool IsDisabledFlat => EdgeType is EdgeType.DisabledFlat;
            public bool IsUp => EdgeType is EdgeType.Up;
            public bool IsDown =>  EdgeType is EdgeType.Down;
        }

        public struct NodeToGrid
        {
            public int X;
            public int MinY;
            public int MaxY;
        }

        public enum EdgeType : sbyte
        {
            Up = 1,
            Flat = 0,
            Down = -1,
            DisabledFlat = -2,
        }
    }
}