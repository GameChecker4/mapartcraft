using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MapartSolving
{
    public class MapartSolver
    {
        public static bool HeightLimit = true;
        public static bool CacheOptimization = true;
        public static bool FindCacheOptimizationErrors;
        public static bool StopOnTooLowBenefit = true;
        public static bool Multithreaded = true;
        public static SolveMethodType SolveMethod = SolveMethodType.DiagonalOther;

        private ISolutionHistory? _solutionHistory;
        private bool _detailedHistory;

        private Func<Task>? _yieldCallback;

        private string? _edgeCheckerReport;
        private readonly int _heightLimit = 256;

        private readonly BenefitCache _benefitCache = new();

        private readonly object _statusLock = new();

        private SolvingStatus _status = new()
        {
            PhaseName = "Initial"
        };

        public SolvingStatus Status
        {
            get
            {
                lock (_statusLock)
                    return _status;
            }
        }

        private const bool DebugTime = false;

        private readonly Stopwatch _totalTimer = new();
        private readonly Stopwatch _preparingTimer = new();
        private readonly Stopwatch _checkingAndComparingEdgesTimer = new();
        private readonly Stopwatch _writingHistoryTimer = new();
        private readonly Stopwatch _updatingHeightsTimer = new();
        private readonly Stopwatch _clearingCacheTimer = new();
        private readonly Stopwatch _settingEdgeTimer = new();
        private readonly Stopwatch _updatingStatusTimer = new();

        public async Task<MapartGraph> Solve(Direction[,] input, CancellationToken cancellationToken)
        {
            if (DebugTime)
            {
                _totalTimer.Start();
                _preparingTimer.Start();
            }

            var graph = MapartGraph.CreateGraphWithoutFlatEdges(input);

            _solutionHistory?.AddInput(input);

            await Solve(graph, input, cancellationToken);

            SetPhase("Done", 0);
            _solutionHistory = null;
            _detailedHistory = false;

            if (DebugTime)
                _totalTimer.Stop();

            return graph;
        }

        public void StoreSolution(ISolutionHistory solutionHistory, bool detailed)
        {
            _solutionHistory = solutionHistory;
            _detailedHistory = detailed;
        }

        public void SetYieldCallback(Func<Task> yieldCallback)
        {
            _yieldCallback = yieldCallback;
        }

        private async Task Solve(MapartGraph graph, Direction[,] input, CancellationToken cancellationToken)
        {
            var heights = new DynamicHeightsData(graph);
            MultithreadedEdgeChecker multithreadedEdgeChecker = null!;
            EdgeChecker edgeChecker = null!;
            if (Multithreaded)
                multithreadedEdgeChecker = new MultithreadedEdgeChecker(graph, heights.Heights, cancellationToken,
                    FindCacheOptimizationErrors);
            else
                edgeChecker = new EdgeChecker(graph, heights.Heights)
                {
                    FindCacheOptimizationErrors = FindCacheOptimizationErrors,
                };
            using (multithreadedEdgeChecker)
            {
                try
                {
                    switch (SolveMethod)
                    {
                        case SolveMethodType.All:
                            var allHorizontalEdges = CollectAllHorizontalEdges(graph);
                            SetPhase("All edges", allHorizontalEdges.Count);
                            if (DebugTime)
                            {
                                _updatingStatusTimer.Reset();
                                _preparingTimer.Stop();
                            }
                            await AddBeneficialEdges(graph, heights, multithreadedEdgeChecker, edgeChecker,
                                allHorizontalEdges,
                                false, cancellationToken);
                            break;
                        case SolveMethodType.DiagonalOther:
                            GetGroupedEdges(graph, input, out var diagonalEdges, out var otherEdges);

                            SetPhase("Diagonal edges", diagonalEdges.Count);
                            if (DebugTime)
                            {
                                _updatingStatusTimer.Reset();
                                _preparingTimer.Stop();
                            }
                            await AddBeneficialEdges(graph, heights, multithreadedEdgeChecker, edgeChecker, diagonalEdges,
                                true,
                                cancellationToken);

                            SetPhase("Other edges", otherEdges.Count);
                            await AddBeneficialEdges(graph, heights, multithreadedEdgeChecker, edgeChecker, otherEdges, false,
                                cancellationToken);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                finally
                {
                    if (Multithreaded)
                    {
                        if (DebugTime)
                        {
                            _totalTimer.Stop();
                            _preparingTimer.Stop();
                            _checkingAndComparingEdgesTimer.Stop();
                            _writingHistoryTimer.Stop();
                            _updatingHeightsTimer.Stop();
                            _clearingCacheTimer.Stop();
                            _settingEdgeTimer.Stop();
                            _updatingStatusTimer.Stop();
                        }

                        _edgeCheckerReport = multithreadedEdgeChecker.GetTimersReport();
                    }
                }
            }
        }

        private async Task AddBeneficialEdges(
            MapartGraph graph,
            DynamicHeightsData heights,
            MultithreadedEdgeChecker multithreadedEdgeChecker,
            EdgeChecker edgeChecker,
            HashSet<UndirectedEdge> edges,
            bool allowZeroBenefit,
            CancellationToken cancellationToken)
        {
            UndirectedEdge? debugPrevEdge = null;
            BenefitCache? debugOptimizedCache = null;

            BestEdgeAccumulator bestEdgeAccumulator = new();

            _benefitCache.DirtyEdges.AddRange(edges);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DebugTime)
                    _checkingAndComparingEdgesTimer.Start();
                CheckDirtyAndCompareAllEdges(multithreadedEdgeChecker, edgeChecker, bestEdgeAccumulator, cancellationToken);
                if (DebugTime)
                    _checkingAndComparingEdgesTimer.Stop();

                await UpdateRemainingAndStepStatus(_benefitCache.Count());
                if (!bestEdgeAccumulator.TryGetAndReset(out var edge))
                    break;

                if (FindCacheOptimizationErrors && debugPrevEdge.HasValue)
                {
                    var difference = debugOptimizedCache!.CompareWithValid(_benefitCache, out int _);
                    if (difference.Count != 0)
                    {
                        Debug.LogWarning($"DifferencesCount: {difference.Count}. " +
                                         $"Affected: {difference.Contains(edge)}. " +
                                         $"Edge: ({debugPrevEdge.Value.MinNode},{debugPrevEdge.Value.MaxNode}) " +
                                         $"Edges: {string.Join(", ", difference.Select(e => $"({e.MinNode},{e.MaxNode})"))}");
                    }
                }

                var updateData = _benefitCache.Get(edge);
                _benefitCache.Remove(edge);

                if (HeightLimit && updateData.MaxHeight >= _heightLimit && _heightLimit >= 0)
                {
                    if (_detailedHistory) // log failure
                    {
                        if (DebugTime)
                            _writingHistoryTimer.Start();
                        var logUpdateResult = heights.CheckLikeUpdateDetailed(edge);
                        var history = logUpdateResult.History!.Value;
                        var updateInfo = logUpdateResult.UpdatedHeights.UpdateInfo;
                        _solutionHistory!.EdgeProcessed(new StepProcessResult(edge,
                            logUpdateResult.UpdatedNodes!.ToList(),
                            true, updateInfo.NewConnections!.Value, history.ConnectedNodes.ToList(),
                            history.DisconnectedNodes.ToList()));
                        if (DebugTime)
                            _writingHistoryTimer.Stop();
                    }

                    continue;
                }

                if (updateData.Benefit <= (allowZeroBenefit ? -1 : 0))
                {
                    if (StopOnTooLowBenefit)
                        break;

                    if (_detailedHistory) // log failure
                    {
                        if (DebugTime)
                            _writingHistoryTimer.Start();
                        var logUpdateResult = heights.CheckLikeUpdateDetailed(edge);
                        var history = logUpdateResult.History!.Value;
                        var updateInfo = logUpdateResult.UpdatedHeights.UpdateInfo;
                        _solutionHistory!.EdgeProcessed(new StepProcessResult(edge,
                            logUpdateResult.UpdatedNodes!.ToList(),
                            true, updateInfo.NewConnections!.Value, history.ConnectedNodes.ToList(),
                            history.DisconnectedNodes.ToList()));
                        if (DebugTime)
                            _writingHistoryTimer.Stop();
                    }

                    continue;
                }

                if (DebugTime)
                    _updatingHeightsTimer.Start();
                var updateResult = heights.UpdateLikeEdgeIsActive(edge, _detailedHistory);
                if (DebugTime)
                    _updatingHeightsTimer.Stop();

                if (DebugTime)
                    _clearingCacheTimer.Start();
                if (FindCacheOptimizationErrors)
                {
                    debugPrevEdge = edge;
                    debugOptimizedCache ??= new BenefitCache();
                    _benefitCache.CopyCacheTo(debugOptimizedCache);
                    MarkWastedCacheAsDirty(edge, graph, heights, debugOptimizedCache, updateResult.UpdatedNodes!);
                    _benefitCache.MarkAllDirty();
                }
                else if (CacheOptimization)
                    MarkWastedCacheAsDirty(edge, graph, heights, _benefitCache, updateResult.UpdatedNodes!);
                else
                    _benefitCache.MarkAllDirty();
                if (DebugTime)
                    _clearingCacheTimer.Stop();

                if (DebugTime)
                    _settingEdgeTimer.Start();
                if (!graph.SetFlatEdge(edge.MinNode, edge.MaxNode, true))
                    throw new Exception();
                if (DebugTime)
                    _settingEdgeTimer.Stop();


                if (DebugTime)
                    _writingHistoryTimer.Start();
                if (_detailedHistory)
                {
                    var history = updateResult.History!.Value;
                    var updateInfo = updateResult.UpdatedHeights.UpdateInfo;
                    _solutionHistory!.EdgeProcessed(new StepProcessResult(edge, updateResult.UpdatedNodes!.ToList(), 
                        false, updateInfo.NewConnections!.Value, history.ConnectedNodes.ToList(), history.DisconnectedNodes.ToList()));
                }
                else
                    _solutionHistory?.EdgeProcessed(new StepProcessResult(edge));
                if (DebugTime)
                    _writingHistoryTimer.Stop();
            }
            _benefitCache.Clear();
        }

        private static void GetGroupedEdges(MapartGraph graph, Direction[,] input, out HashSet<UndirectedEdge> diagonalEdges, out HashSet<UndirectedEdge> otherEdges)
        {
            otherEdges = CollectAllHorizontalEdges(graph);
            int inputHeight = graph.Height - 1;
            diagonalEdges = new  HashSet<UndirectedEdge>();
            foreach (var edge in otherEdges)
            {
                int node1Type = GetNodeType(edge.MinNode);
                int node2Type = GetNodeType(edge.MaxNode);
                if ((node1Type, node2Type) is (1, 1) or (-1, -1))
                    diagonalEdges.Add(edge);
            }
            otherEdges.ExceptWith(diagonalEdges);
            return;

            int GetNodeType(int node)
            {
                // 2 = single uppest
                // 1 = goes up
                // 0 = big flat
                // -1 = goes down
                // -2 = single lowest
                var ng = graph.NodesToGrid[node];
                if (ng.MaxY != ng.MinY)
                    return 0;
                
                int x = ng.X;
                int y = ng.MinY;

                if (y == 0)
                    return input[x, y] switch
                    {
                        Direction.Up => 1,
                        Direction.Down => -1,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                
                if (y >= inputHeight)
                    return input[x, y - 1] switch
                    {
                        Direction.Up => 1,
                        Direction.Down => -1,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                return (input[x, y - 1], input[x, y]) switch
                {
                    (Direction.Up, Direction.Down) => 2,
                    (Direction.Up, Direction.Up) => 1,
                    (Direction.Down, Direction.Down) => -1,
                    (Direction.Down, Direction.Up) => -2,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }

        private static HashSet<UndirectedEdge> CollectAllHorizontalEdges(MapartGraph graph)
        {
            var edges = new HashSet<UndirectedEdge>();
            for (int node = 0; node < graph.NodesCount; node++)
                foreach (var edge in graph.GetEdges(node)) 
                    if (edge.IsFlat)
                        edges.Add(new UndirectedEdge(node, edge.ToNode));
            return edges;
        }

        private void CheckDirtyAndCompareAllEdges(MultithreadedEdgeChecker multithreadedEdgeChecker,
            EdgeChecker edgeChecker, BestEdgeAccumulator bestEdgeAccumulator, CancellationToken cancellationToken)
        {
            if (Multithreaded)
            {
                foreach (var result in multithreadedEdgeChecker.Check(_benefitCache.DirtyEdges, AddCachedToCompare, cancellationToken))
                    CacheAndAddToCompareIfValid(result.Edge, result.Data, result.IsCycled);
            }
            else
            {
                AddCachedToCompare();
                foreach (var edge in _benefitCache.DirtyEdges)
                {
                    var data = edgeChecker.Check(edge, out bool isCycled);
                    CacheAndAddToCompareIfValid(edge, data, isCycled);
                }
            }

            _benefitCache.DirtyEdges.Clear();
            return;

            void AddCachedToCompare()
            {
                foreach (var (edge, data) in _benefitCache)
                    bestEdgeAccumulator.AddToCompare(edge, data.Benefit, data.EdgeHeight);
            }

            void CacheAndAddToCompareIfValid(UndirectedEdge edge, BenefitCache.Data data, bool isCycled)
            {
                if (isCycled)
                    return;
                if (!_benefitCache.TryAdd(edge, data))
                    throw new Exception();
                bestEdgeAccumulator.AddToCompare(edge, data.Benefit, data.EdgeHeight);
            }
        }

        private readonly PriorityQueue<int, int> _nodesQueue = new();
        private readonly HashSet<int> _queuedNodes = new();
        private readonly HashSet<int> _visited = new();
        private void MarkWastedCacheAsDirty(UndirectedEdge updatedEdge, MapartGraph graph, DynamicHeightsData heights, BenefitCache benefitCache, IEnumerable<int> updatedNodes)
        {
            foreach (int updatedNode in updatedNodes)
                EnqueueModifiedNodeAndNeighbours(updatedNode);
            EnqueueModifiedNodeAndNeighbours(updatedEdge.MinNode);
            EnqueueModifiedNodeAndNeighbours(updatedEdge.MaxNode);
            while (_nodesQueue.TryDequeue(out int dequeuedNode, out _))
            {
                VisitGroupRecursiveAndEnqueueAndMarkDirty(dequeuedNode);
                continue;

                void VisitGroupRecursiveAndEnqueueAndMarkDirty(int node)
                {
                    if (!_visited.Add(node))
                        return;

                    foreach (var edge in graph.GetEdges(node))
                        if (edge.IsDown)
                            EnqueueIfNotQueuedPreviously(edge.ToNode);

                    foreach (var edge in graph.GetEdges(node))
                        if (edge.IsDisabledFlat)
                            benefitCache.MarkDirty(new UndirectedEdge(node, edge.ToNode));

                    foreach (var edge in graph.GetEdges(node))
                        if (edge.IsEnabledFlat)
                            VisitGroupRecursiveAndEnqueueAndMarkDirty(edge.ToNode);
                }
                
            }
            _nodesQueue.Clear();
            _queuedNodes.Clear();
            _visited.Clear();
            return;

            void EnqueueModifiedNodeAndNeighbours(int node)
            {
                EnqueueIfNotQueuedPreviously(node);
                foreach (var edge in graph.GetEdges(node))
                    if (edge.IsUp || edge.IsDisabledFlat)
                        if (!_queuedNodes.Contains(edge.ToNode))
                            if ((edge.IsUp && heights.Heights[edge.ToNode] == heights.Heights[node] + 1) ||
                                (edge.IsDisabledFlat && heights.Heights[edge.ToNode] <= heights.Heights[node]))
                                EnqueueIfNotQueuedPreviously(edge.ToNode);
            }

            void EnqueueIfNotQueuedPreviously(int node)
            {
                if (_queuedNodes.Add(node))
                    _nodesQueue.Enqueue(node, -heights.Heights[node]);
            }
        }

        private void SetPhase(string phaseName, int initialEdges)
        {
            if (DebugTime)
                _updatingStatusTimer.Start();
            lock (_statusLock)
            {
                if (_status.InitialEdges != 0)
                {
                    _solutionHistory?.EndPhase(_status.PhaseName);
                }

                _status = new SolvingStatus
                {
                    PhaseName = phaseName,
                    Step = 0,
                    InitialEdges = initialEdges,
                    RemainingEdges = initialEdges,
                };

                if (initialEdges == 0)
                {
                    _solutionHistory?.EndPhase(phaseName);
                }
            }
            if (DebugTime)
                _updatingStatusTimer.Stop();
        }

        private async Task UpdateRemainingAndStepStatus(int remaining)
        {
            if (DebugTime)
                _updatingStatusTimer.Start();
            lock (_statusLock)
            {
                _status.Step++;
                _status.RemainingEdges = remaining;
            }

            var yieldTask = _yieldCallback?.Invoke();
            if (yieldTask != null)
                await yieldTask;

            if (DebugTime)
                _updatingStatusTimer.Stop();
        }

        public string GetTimersReport()
        {
            var shortage = _totalTimer.Elapsed - (_preparingTimer.Elapsed + _checkingAndComparingEdgesTimer.Elapsed +
                                                  _writingHistoryTimer.Elapsed + _updatingHeightsTimer.Elapsed +
                                                  _clearingCacheTimer.Elapsed + _settingEdgeTimer.Elapsed + _updatingStatusTimer.Elapsed);
            return $"Solve: {Time(_totalTimer)}\n" +
                   $"├─Preparing: {Time(_preparingTimer)}\n" +
                   $"├─Checking and comparing edges: {Time(_checkingAndComparingEdgesTimer)}\n" +
                   $"├─Writing a history: {Time(_writingHistoryTimer)}\n" +
                   $"├─Updating heights: {Time(_updatingHeightsTimer)}\n" +
                   $"├─Clearing cache: {Time(_clearingCacheTimer)}\n" +
                   $"├─Setting the edge: {Time(_settingEdgeTimer)}\n" +
                   $"├─Updating status {Time(_updatingStatusTimer)}\n" +
                   $"└─(Uncount: {shortage.ToStringCompact()})\n" +
                   _edgeCheckerReport;
            //├─│ └─
            static string Time(Stopwatch sw) => sw.Elapsed.ToStringCompact();
        }

        public enum SolveMethodType
        {
            All,
            DiagonalOther,
        }

        public struct SolvingStatus : IEquatable<SolvingStatus>
        {
            public string PhaseName;
            public int Step;
            public int InitialEdges;
            public int RemainingEdges;

            public bool Equals(SolvingStatus other)
            {
                return PhaseName == other.PhaseName &&
                       Step == other.Step &&
                       InitialEdges == other.InitialEdges &&
                       RemainingEdges == other.RemainingEdges;
            }
        }
    }
}