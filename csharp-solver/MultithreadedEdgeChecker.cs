using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MapartSolving
{
    public class MultithreadedEdgeChecker : IDisposable
    {
        private readonly UndirectedEdge[] _inputs;
        private readonly ResultItem[] _results;

        private readonly Task[] _workers;
        private readonly EdgeChecker _mainEdgeChecker;

        private readonly object _lock = new();

        private ManualResetEventSlim _startProcessingNextEvent = new(false);
        private ManualResetEventSlim _startProcessingCurrentEvent = new(false);
        private readonly AutoResetEvent _workersIsNotWorkingEvent = new(false);
        private readonly CountdownEvent _workersReadyEvent;
        private volatile int _workersWorking;
        private readonly ConcurrentIncrementer _inputIndexer = new();

        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        private readonly Stopwatch _totalTime = new();
        private readonly Stopwatch _toAndFro = new();
        private readonly Stopwatch _mainCallbackTimer = new();
        private readonly Stopwatch _parallelCallbackTimer = new();
        private readonly Stopwatch _mainWaitedForReady = new();
        private readonly Stopwatch _mainWaitedForResults = new();
        private readonly Stopwatch[] _workersWorked;
        private readonly Stopwatch _wakeUpFirstWorker = new();
        private volatile int _stoppedWakeUpWorkerStopwatch;
        private readonly Stopwatch _wakeUpMain = new();
        private volatile int _startedWakeUpMainStopwatch;
        private readonly Stopwatch _workersProcessingTime = new();
        private volatile int _startedWorkersProcessingStopwatch;
        private readonly Stopwatch _betweenFirstWorkerFinishedAndDataProcessed = new();
        private volatile int _firstWorkerFinished;
        private readonly Stopwatch _pushInput = new();
        private readonly Stopwatch _iterateResults = new();
        private readonly Stopwatch _mainWorked = new();

        private int _workerEdgesTotal;
        private int _workerChecksTotal;
        private int _mainChecksTotal;
        private int _mainEdgesTotal;

        private const bool DebugTime = false;
        private const bool DebugMain = DebugTime;
        private const bool DebugWorkers = DebugTime;
        private const bool DebugWakeUp = DebugTime;

        private readonly int _degree;

        public MultithreadedEdgeChecker(MapartGraph graph, HeightsData<HeightsUpdater.UpdateInfo> heights,
            CancellationToken cancellationToken, bool findCacheOptimizationErrors = false)
        {
            _degree = Environment.ProcessorCount;

            _mainEdgeChecker = ConstructEdgeChecker();

            var maxEdgesCount = graph.Height * (graph.Width - 1);
            _inputs = new UndirectedEdge[maxEdgesCount];
            _results = new ResultItem[maxEdgesCount];

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cts.Token;

            _workersReadyEvent = new CountdownEvent(_degree);
            _workers = new Task[_degree];
            _workersWorked = new Stopwatch[_degree];
            for (int i = 0; i < _degree; i++)
            {
                _workersWorked[i] = new Stopwatch();
                int workerId = i;
                _workers[i] = Task.Run(() =>
                {
                    Thread.CurrentThread.Name = "Mapart EdgeChecker";
                    Worker(workerId, ConstructEdgeChecker(), token);
                }, token);
            }

            return;

            EdgeChecker ConstructEdgeChecker() => new(graph, heights)
            {
                FindCacheOptimizationErrors = findCacheOptimizationErrors,
            };
        }

        public IEnumerable<CheckedEdge> Check(IEnumerable<UndirectedEdge> edges, Action onWorkersStartedCallback, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(MultithreadedEdgeChecker));
                cancellationToken.ThrowIfCancellationRequested();

                _totalTime.Start();

                if (DebugMain)
                    _pushInput.Start();
                int itemsCount = 0;
                foreach (var edge in edges)
                {
                    _inputs[itemsCount++] = edge;
                }
                if (DebugMain)
                    _pushInput.Stop();

                try
                {
                    if (itemsCount > 3) // TODO: compute in runtime
                        ParallelCheck(itemsCount, onWorkersStartedCallback, cancellationToken);
                    else
                        CheckStraightAway(itemsCount, onWorkersStartedCallback, cancellationToken);
                }
                catch
                {
                    _totalTime.Stop();
                    throw;
                }

                if (DebugMain)
                    _iterateResults.Start();
                for (int i = 0; i < itemsCount; i++)
                    yield return new CheckedEdge(_inputs[i], _results[i].Data, _results[i].IsCycled);
                if (DebugMain)
                    _iterateResults.Stop();

                _totalTime.Stop();
            }
        }

        private void ParallelCheck(int itemsCount, Action onWorkersStartedCallback, CancellationToken cancellationToken)
        {
            try
            {
                _workerChecksTotal++;
                _workerEdgesTotal += itemsCount;

                if (DebugMain)
                    _mainWaitedForReady.Start();
                _workersReadyEvent.Wait(cancellationToken);
                _workersReadyEvent.Reset();
                if (DebugMain)
                    _mainWaitedForReady.Stop();

                if (DebugWakeUp)
                {
                    _startedWakeUpMainStopwatch = 0;
                    _stoppedWakeUpWorkerStopwatch = 0;
                }

                if (DebugWorkers)
                {
                    _startedWorkersProcessingStopwatch = 0;
                    _firstWorkerFinished = 0;
                }

                _workersIsNotWorkingEvent.Reset();
                _startProcessingCurrentEvent.Reset();

                _inputIndexer.Reset(itemsCount - 1);


                (_startProcessingNextEvent, _startProcessingCurrentEvent) =
                    (_startProcessingCurrentEvent, _startProcessingNextEvent);

                if (DebugMain)
                    _toAndFro.Start();

                if (DebugWakeUp)
                    _wakeUpFirstWorker.Start();

                _startProcessingCurrentEvent.Set();

                if (DebugMain)
                    _parallelCallbackTimer.Start();
                onWorkersStartedCallback.Invoke();
                if (DebugMain)
                {
                    _parallelCallbackTimer.Stop();
                    _mainWaitedForResults.Start();
                }

                do
                    _cts.Token.ThrowIfCancellationRequested();
                while (!_workersIsNotWorkingEvent.WaitOne(250));

                if (DebugWakeUp)
                    _wakeUpMain.Stop();

                if (DebugMain)
                {
                    _mainWaitedForResults.Stop();
                    _toAndFro.Stop();
                }
            }
            catch
            {
                if (DebugMain)
                {
                    _toAndFro.Stop();
                    _mainWaitedForReady.Stop();
                    _mainWaitedForResults.Stop();
                    _parallelCallbackTimer.Stop();
                }
                if (DebugWakeUp)
                {
                    _wakeUpFirstWorker.Stop();
                    _wakeUpMain.Stop();
                }

                throw;
            }
        }

        private void CheckStraightAway(int itemsCount, Action callback, CancellationToken cancellationToken)
        {
            _mainChecksTotal++;
            _mainEdgesTotal += itemsCount;
            if (DebugMain)
                _mainWorked.Start();
            for (int i = 0; i < itemsCount; i++)
                DoWork(i, _mainEdgeChecker);
            if (DebugMain)
                _mainWorked.Stop();

            cancellationToken.ThrowIfCancellationRequested();

            if (DebugMain)
                _mainCallbackTimer.Start();
            callback.Invoke();
            if (DebugMain)
                _mainCallbackTimer.Stop();
        }

        private void Worker(int id, EdgeChecker edgeChecker, CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    var startEvent = _startProcessingNextEvent;

                    _workersReadyEvent.Signal();

                    if (!WaitForStartSignalOrCancellation(startEvent))
                        break;

                    if (DebugWakeUp && Interlocked.CompareExchange(ref _stoppedWakeUpWorkerStopwatch, 1, 0) == 0)
                        _wakeUpFirstWorker.Stop();

                    if (!_inputIndexer.IsEnded)
                    {
                        if (DebugWorkers && Interlocked.CompareExchange(ref _startedWorkersProcessingStopwatch, 1, 0) == 0)
                            _workersProcessingTime.Start();

                        Interlocked.Increment(ref _workersWorking);

                        if (DebugWorkers)
                            _workersWorked[id].Start();
                        while (_inputIndexer.TryGetNext(out int batchIndex))
                            DoWork(batchIndex, edgeChecker);
                        if (DebugWorkers)
                            _workersWorked[id].Stop();

                        Interlocked.Decrement(ref _workersWorking);
                    }

                    if (_workersWorking == 0)
                    {
                        if (DebugWorkers && Interlocked.CompareExchange(ref _firstWorkerFinished, 2, 1) == 1)
                            _betweenFirstWorkerFinishedAndDataProcessed.Stop();
                        if (DebugWorkers && Interlocked.CompareExchange(ref _startedWorkersProcessingStopwatch, 2, 1) == 1)
                            _workersProcessingTime.Stop();
                        if (DebugWakeUp && Interlocked.CompareExchange(ref _startedWakeUpMainStopwatch, 1, 0) == 0)
                            _wakeUpMain.Start();
                        _workersIsNotWorkingEvent.Set();
                    }
                    else if (DebugWorkers && Interlocked.CompareExchange(ref _firstWorkerFinished, 1, 0) == 0)
                        _betweenFirstWorkerFinishedAndDataProcessed.Start();
                    continue;

                    bool WaitForStartSignalOrCancellation(ManualResetEventSlim e)
                    {
                        do
                        {
                            if (_disposed)
                                return false;
                        } while (!e.Wait(250, cancellationToken));

                        return !_disposed;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _cts.Cancel();
                OnException();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _cts.Cancel();
                OnException();
            }

            return;

            void OnException()
            {
                if (DebugWorkers)
                {
                    if (Interlocked.Exchange(ref _startedWorkersProcessingStopwatch, 2) == 1)
                        _workersProcessingTime.Stop();
                    if (Interlocked.Exchange(ref _firstWorkerFinished, 2) == 1)
                        _betweenFirstWorkerFinishedAndDataProcessed.Stop();
                    _workersWorked[id].Stop();
                }
            }
        }

        private void DoWork(int inputIndex, EdgeChecker edgeChecker)
        {
            var edge = _inputs[inputIndex];
            var data = edgeChecker.Check(edge, out var isCycled);
            _results[inputIndex] = new ResultItem(data, isCycled);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _startProcessingNextEvent.Set();
            _startProcessingCurrentEvent.Set();
            if (!Task.WaitAll(_workers, 1000))
            {
                Debug.LogError($"Workers are not disposed within 1 second");
            }
            _startProcessingNextEvent.Dispose();
            _startProcessingCurrentEvent.Dispose();
            _workersIsNotWorkingEvent.Dispose();
            _workersReadyEvent.Dispose();
            _cts.Dispose();
        }

        public string GetTimersReport()
        {
            var workersWorkedSum = SumSw(_workersWorked);
            var workerWorked = workersWorkedSum / _degree;
            var toAndFroUncount = _toAndFro.Elapsed - (_wakeUpFirstWorker.Elapsed + _workersProcessingTime.Elapsed + _wakeUpMain.Elapsed);
            int checksTotal = _workerChecksTotal + _mainChecksTotal;
            return $"Workers count: {_degree} Checks: {checksTotal} Edges: {_workerEdgesTotal + _mainEdgesTotal}\n" +
                   $"EdgeChecker time: {_totalTime.Elapsed.ToStringCompact()}\n" +
                   $"├─Push input: {TimeCSw(_pushInput, checksTotal)}\n" +
                   $"├─Parallel processing (Checks: {_workerChecksTotal} Edges: {_workerEdgesTotal})\n" +
                   $"│ ├─Wait for ready: {TimeCSw(_mainWaitedForReady, _workerChecksTotal)}\n" +
                   $"│ ├─Execute callback on main: {TimeCSw(_parallelCallbackTimer, _workerChecksTotal)}\n" +
                   $"│ ├─Main waited for results: {TimeCSw(_mainWaitedForResults, _workerChecksTotal)}\n" +
                   $"│ └─To and fro: {TimeCSw(_toAndFro, _workerChecksTotal)}\n" +
                   $"│   ├─Wake up first worker: {TimeCSw(_wakeUpFirstWorker, _workerChecksTotal)}\n" +
                   $"│   ├─Processing time: {TimeCeSw(_workersProcessingTime, _workerChecksTotal, _workerEdgesTotal)}\n" +
                   $"│   │ ├─Workers worked mean: {TimeCeMean(workerWorked, workersWorkedSum, _workerChecksTotal, _workerEdgesTotal)}\n" +
                   $"│   │ └─Between first worker finished and data ended processing: {TimeCSw(_betweenFirstWorkerFinishedAndDataProcessed, _workerChecksTotal)}\n" +
                   $"│   ├─Wake up main (or wait for): {TimeCSw(_wakeUpMain, _workerChecksTotal)}\n" +
                   $"│   └─(Uncount: {TimeC(toAndFroUncount, _workerChecksTotal)})\n" +
                   $"├─Main processing (Checks: {_mainChecksTotal} Edges: {_mainEdgesTotal})\n" +
                   $"│ ├─Execute callback on main: {TimeCSw(_mainCallbackTimer, _mainChecksTotal)}\n" +
                   $"│ └─Main worked: {TimeCeSw(_mainWorked, _mainChecksTotal, _mainEdgesTotal)}\n" +
                   $"└─Iterate results: {TimeCSw(_iterateResults, checksTotal)}\n" +
                   $"";
            TimeSpan SumSw(IEnumerable<Stopwatch> sws) => sws.Aggregate(TimeSpan.Zero, (s, sw) => s + sw.Elapsed);

            static string TimeDetailed(TimeSpan ts) => ts.ToStringCompact(true);

            static string TimeC(TimeSpan time, int checks) => $"{time.ToStringCompact()} (Per check: {TimeDetailed(time / checks)})";
            static string TimeCe(TimeSpan time, int checks, int edges) => $"{TimeC(time, checks)} (Per edge: {TimeDetailed(time / edges)})";
            static string TimeCeMean(TimeSpan timeMean, TimeSpan timeSum, int checks, int edges) =>
                $"{timeMean.ToStringCompact()} (Per check sum: {TimeDetailed(timeSum / checks)}) (Per edge sum: {TimeDetailed(timeSum / edges)})";

            static string TimeCSw(Stopwatch sw, int checks) => TimeC(sw.Elapsed, checks);
            static string TimeCeSw(Stopwatch sw, int checks, int edges) => TimeCe(sw.Elapsed, checks, edges);
        }


        public readonly struct CheckedEdge
        {
            public readonly UndirectedEdge Edge;
            public readonly BenefitCache.Data Data;
            public readonly bool IsCycled;

            public CheckedEdge(UndirectedEdge edge, BenefitCache.Data data, bool isCycled)
            {
                Edge = edge;
                Data = data;
                IsCycled = isCycled;
            }
        }

        private readonly struct ResultItem
        {
            public readonly BenefitCache.Data Data;
            public readonly bool IsCycled;

            public ResultItem(BenefitCache.Data data, bool isCycled)
            {
                Data = data;
                IsCycled = isCycled;
            }
        }
    }
}