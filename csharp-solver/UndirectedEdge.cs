using System;

namespace MapartSolving
{
    [Serializable]
    public readonly struct UndirectedEdge : IComparable<UndirectedEdge>, IEquatable<UndirectedEdge>
    {
        public int MinNode { get; }
        public int MaxNode { get; }
        
        public UndirectedEdge(int node1, int node2)
        {
            MinNode = Math.Min(node1, node2);
            MaxNode = Math.Max(node1, node2);
        }
        
        public int CompareTo(UndirectedEdge other)
        {
            int comparison = MinNode.CompareTo(other.MinNode);
            if (comparison == 0) 
                comparison = MaxNode.CompareTo(other.MaxNode);
            return comparison;
        }

        public bool Equals(UndirectedEdge other)
        {
            return MinNode == other.MinNode && MaxNode == other.MaxNode;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MinNode, MaxNode);
        }
    }
}