using System.Linq;

namespace MapartSolving
{
    public class HeightsData<T> where T: new()
    {
        public readonly int[] Heights;
        public int MaxHeight { get; set; }
        public T UpdateInfo { get; set; }

        private HeightsData(int[] heights, int maxHeight)
        {
            Heights = new int[heights.Length];
            heights.CopyTo(Heights, 0);
            MaxHeight = maxHeight;
            UpdateInfo = new T();
        }

        public HeightsData(int[] heights) : this(heights, heights.Max()) {}

        public HeightsData(int nodesCount)
        {
            Heights = new int[nodesCount];
            MaxHeight = 0;
            UpdateInfo = new T();
        }
        
        public HeightsData(HeightsData<T> other) : this(other.Heights.Length)
        {
            other.CopyTo(this);
        }

        public void CopyTo(HeightsData<T> other)
        {
            Debug.Assert(other.Heights.Length == Heights.Length);
            Heights.CopyTo(other.Heights, 0);
            other.MaxHeight = MaxHeight;
            other.UpdateInfo = UpdateInfo;
        }

        public int this[int node]
        {
            get => Heights[node];
            set => Heights[node] = value;
        }
    }
}