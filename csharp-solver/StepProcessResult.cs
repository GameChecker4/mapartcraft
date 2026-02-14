using System;
using System.Collections.Generic;

namespace MapartSolving
{
    [Serializable]
    public readonly struct StepProcessResult
    {
        public readonly UndirectedEdge? Edge;
        public readonly List<int>? FoundCycle;
        public readonly List<int>? UpdatedHeight;
        public readonly bool IsBreaksToMuch;
        public readonly int? NewConnections;
        public readonly List<int>? ConnectedNodes;
        public readonly List<int>? DisconnectedNodes;

        public bool IsAddedEdge => Edge.HasValue && FoundCycle == null && !IsBreaksToMuch;
        public bool IsAddedEdgeWithHeightUpdate => IsAddedEdge && UpdatedHeight?.Count > 0;

        public StepProcessResult(UndirectedEdge edge, List<int>? updatedHeight = null, bool isBreaksToMuch = false,
            int? newConnections = null, List<int>? connectedNodes = null, List<int>? disconnectedNodes = null)
        {
            Edge = edge;
            FoundCycle = null;
            UpdatedHeight = updatedHeight;
            IsBreaksToMuch = isBreaksToMuch;
            NewConnections = newConnections;
            ConnectedNodes = connectedNodes;
            DisconnectedNodes = disconnectedNodes;
        }

        public StepProcessResult(UndirectedEdge edge, List<int> foundCycle)
        {
            Edge = edge;
            FoundCycle = foundCycle;
            UpdatedHeight = null;
            IsBreaksToMuch = false;
            NewConnections = null;
            ConnectedNodes = null;
            DisconnectedNodes = null;
        }
    }
}