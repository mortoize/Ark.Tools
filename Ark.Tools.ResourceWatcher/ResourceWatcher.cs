﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 

using Ark.Tools.Core;
using EnsureThat;
using Nito.AsyncEx;
using NLog;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ark.Tools.ResourceWatcher
{
    public abstract class ResourceWatcher<T> where T : IResourceState
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly IResourceWatcherConfig _config;
        private readonly IStateProvider _stateProvider;
        private object _lock = new object { };
        private volatile bool _isStarted = false;
        private CancellationTokenSource _cts;
        private Task _task;
        private readonly ResourceWatcherDiagnosticSource _diagnosticSource;

        public ResourceWatcher(IResourceWatcherConfig config, IStateProvider stateProvider)
        {
            EnsureArg.IsNotNull(config);
            EnsureArg.IsNotNull(stateProvider);

            _config = config;
            _stateProvider = stateProvider;
            _diagnosticSource = new ResourceWatcherDiagnosticSource(config.Tenant, _logger);
        }

        public void Start()
        {
            _start();
            _diagnosticSource.HostStartEvent();
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private void _start()
        {
            lock (_lock)
            {
                if (_isStarted == true)
                    throw new InvalidOperationException("Watcher is already started");
                _onBeforeStart();
                _isStarted = true;
            }

            _cts = new CancellationTokenSource();
            _task = Task.Run(async () =>
            {
                try
                {
                    await _runAsync(_cts.Token);
                }
                catch (TaskCanceledException)
                {

                }
            }
            , _cts.Token).FailFastOnException();
        }

        protected virtual void _onBeforeStart()
        {
        }

        public async Task RunOnce(CancellationToken ctk = default)
        {
            lock (_lock)
            {
                if (_isStarted)
                    throw new ApplicationException("Invalid use of RunOnce: the watcher has been started and is working in background or another RunOnce is running.");
                _onBeforeStart();
                _isStarted = true;
            }

            try
            {
                await _runOnce(RunType.Once, ctk).ConfigureAwait(false);
            }
            finally
            {
                _isStarted = false;
            }
        }

        private async Task _runAsync(CancellationToken ctk = default)
        {
            await Task.Yield();

            int exConsecutiveCount = 0;

            while (!ctk.IsCancellationRequested)
            {
                try
                {
                    await _runOnce(RunType.Normal, ctk);

                    exConsecutiveCount = 0;
                }
                catch (Exception ex)
                {
                    if (++exConsecutiveCount == 10)
                    {
                        _diagnosticSource.ReportRunConsecutiveFailureLimitReached(ex, exConsecutiveCount);
                        throw;
                    }
                }

                ctk.ThrowIfCancellationRequested();

                _logger.Info("Going to sleep for {0}s", _config.SleepSeconds);
                await Task.Delay(_config.SleepSeconds * 1000, ctk);
            }
        }

        protected virtual async Task _runOnce(RunType runType, CancellationToken ctk = default)
        {
            var now = DateTime.UtcNow;

            MappedDiagnosticsLogicalContext.Set("RequestID", Guid.NewGuid().ToString());
            var activityRun = _diagnosticSource.RunStart(runType, now);

            try
            {
                //GetResources
                var activityResource = _diagnosticSource.GetResourcesStart();

                var infos = await _getResourcesInfo(ctk).ConfigureAwait(false);

                var bad = infos.GroupBy(x => x.ResourceId).FirstOrDefault(x => x.Count() > 1);
                if (bad != null)
                    _diagnosticSource.ThrowDuplicateResourceIdRetrived(bad.Key);

                if (_config.SkipResourcesOlderThanDays.HasValue)
                    infos = infos
                            .Where(x => _getEarliestModified(x).Value.Date > LocalDateTime.FromDateTime(now).Date.PlusDays(-(int)_config.SkipResourcesOlderThanDays.Value))
                            ;

                var list = infos.ToList();
                _diagnosticSource.GetResourcesSuccessful(activityResource, list.Count);

                //Check State - check which entries are new or have been modified.
                var activityCheckState = _diagnosticSource.CheckStateStart();

                var states = _config.IgnoreState ? Enumerable.Empty<ResourceState>() : await _stateProvider.LoadStateAsync(_config.Tenant, list.Select(i => i.ResourceId).ToArray(), ctk).ConfigureAwait(false);

                var evaluated = _createEvalueteList(list, states);

                _diagnosticSource.CheckStateSuccessful(activityCheckState, evaluated);

                //Process
                var skipped = evaluated.Where(x => x.ResultType == ResultType.Skipped).ToList();
                var toProcess = evaluated.Where(x => !x.ResultType.HasValue).ToList();

                _logger.Info($"Found {skipped.Count} resources to skip");
                _logger.Info($"Found {toProcess.Count} resources to process with parallelism {_config.DegreeOfParallelism}");

                var count = toProcess.Count;
                await toProcess.Parallel((int)_config.DegreeOfParallelism, (i, x, ct) => {
                    x.Total = count;
                    x.Index = i + 1;
                    return _processEntry(x, ct);
                }, ctk);
                
                _diagnosticSource.RunSuccessful(activityRun, evaluated);

                if (activityRun.Duration > _config.RunDurationNotificationLimit)
                    _diagnosticSource.RunTookTooLong(activityRun);
            }
            catch (Exception ex)
            {
                _diagnosticSource.RunFailed(activityRun, ex);
                throw;
            }
        }

        private List<ProcessContext> _createEvalueteList(List<IResourceMetadata> list, IEnumerable<ResourceState> states)
        {
            Tuple<string, LocalDateTime?, LocalDateTime?> a;

            var ev = list.GroupJoin(states, i => i.ResourceId, s => s.ResourceId, (i, s) =>
             {
                 var x = new ProcessContext { CurrentInfo = i, LastState = s.SingleOrDefault() };
                 if (x.LastState == null)
                 {
                     x.ProcessType = ProcessType.New;
                 }
                 else if (x.LastState.RetryCount == 0 && x.IsNewResource(out a))
                 {
                     x.ProcessType = ProcessType.Updated;
                 }
                 else if (x.LastState.RetryCount > 0 && x.LastState.RetryCount <= _config.MaxRetries)
                 {
                     x.ProcessType = ProcessType.Retry;
                 }
                 else if (x.LastState.RetryCount > _config.MaxRetries
                     && x.IsNewResource(out a)
                     && x.LastState.LastEvent + _config.BanDuration < SystemClock.Instance.GetCurrentInstant()
                     // BAN expired and new version                
                     )
                 {
                     x.ProcessType = ProcessType.RetryAfterBan;
                 }
                 else if (x.LastState.RetryCount > _config.MaxRetries
                     && x.IsNewResource(out a)
                     && !(x.LastState.LastEvent + _config.BanDuration < SystemClock.Instance.GetCurrentInstant())
                     // BAN               
                     )
                 {
                     x.ProcessType = ProcessType.Banned;
                 }
                 else
                     x.ProcessType = ProcessType.NothingToDo;

                 if (x.ProcessType == ProcessType.NothingToDo || x.ProcessType == ProcessType.Banned)
                 {
                     x.ResultType = ResultType.Skipped;
                 }

                 return x;
             }).ToList();

            return ev;
        }

        protected abstract Task<IEnumerable<IResourceMetadata>> _getResourcesInfo(CancellationToken ctk = default);
        protected abstract Task<T> _retrievePayload(IResourceMetadata info, IResourceTrackedState lastState, CancellationToken ctk = default);
        protected abstract Task _processResource(ChangedStateContext<T> context, CancellationToken ctk = default);

        private async Task<T> _fetchResource(ProcessContext pc, CancellationToken ctk = default)
        {
            var info = pc.CurrentInfo;
            var lastState = pc.LastState;

            var activity = _diagnosticSource.FetchResourceStart(pc);

            try
            {
                var res = await _retrievePayload(info, lastState, ctk);
                _diagnosticSource.FetchResourceSuccessful(activity, pc);
                return res;
            } catch (Exception ex)
            {
                _diagnosticSource.FetchResourceFailed(activity, pc, ex);
                throw;
            }
        }

        private async Task _processEntry(ProcessContext pc, CancellationToken ctk = default)
        {
            var info = pc.CurrentInfo;
            var lastState = pc.LastState;
            var dataType = pc.ProcessType;
            
            try
            {
                var processActivity = _diagnosticSource.ProcessResourceStart(pc);

                AsyncLazy<T> payload = new AsyncLazy<T>(() => _fetchResource(pc, ctk));

                var state = pc.NewState = new ResourceState()
                {
                    Tenant = _config.Tenant,
                    ResourceId = info.ResourceId,
                    Modified = lastState?.Modified ?? info.Modified, // we want to update modified only on success or Ban or first run
                    ModifiedMultiple = lastState?.ModifiedMultiple ?? info.ModifiedMultiple, // we want to update modified multiple only on success or Ban or first run
                    LastEvent = SystemClock.Instance.GetCurrentInstant(),
                    RetryCount = lastState?.RetryCount ?? 0,
                    CheckSum = lastState?.CheckSum,
                    RetrievedAt = lastState?.RetrievedAt,
                    Extensions = info.Extensions
                };

                try
                {
                    pc.ResultType = ResultType.Normal;
                    IResourceState newState = default;

                    await _processResource(new ChangedStateContext<T>(info, lastState, payload), ctk).ConfigureAwait(false);

                    // if handlers retrived data, fetch the result to check the checksum
                    if (payload.IsStarted)
                    {
                        // if the retrievePayload task gone in exception but the _processResource did not ...
                        // here we care only if we have a payload to use
                        if (payload.Task.Status == TaskStatus.RanToCompletion)
                        {
                            newState = await payload;

                            if (newState != null)
                            {
                                if (!string.IsNullOrWhiteSpace(newState.CheckSum) && state.CheckSum != newState.CheckSum)
                                    _logger.Info("Checksum changed on ResourceId=\"{0}\" from \"{1}\" to \"{2}\"", state.ResourceId, state.CheckSum, newState.CheckSum);

                                state.CheckSum = newState.CheckSum;
                                state.RetrievedAt = newState.RetrievedAt;

                                pc.ResultType = ResultType.Normal;
                            }
                            else // no payload retrived, so no new state. Generally due to a same-checksum
                            {
                                pc.ResultType = ResultType.NoNewData;
                            }

                            state.Extensions = info.Extensions;
                            state.Modified = info.Modified;
                            state.ModifiedMultiple = info.ModifiedMultiple;
                            state.RetryCount = 0; // success
                        }
                        else
                        {
                            throw new NotSupportedException($"({pc.Index}/{pc.Total}) ResourceId=\"{state.ResourceId}\" we cannot reach this point!");
                        }
                    }
                    else // for some reason, no action has been and payload has not been retrieved. We do not change the state
                    {
                        pc.ResultType = ResultType.NoAction;
                    }

                    _diagnosticSource.ProcessResourceSuccessful(processActivity, pc);

                    if(processActivity.Duration > _config.ResourceDurationNotificationLimit)
                        _diagnosticSource.ProcessResourceTookTooLong(info.ResourceId, processActivity);
                }
                catch (Exception ex)
                {
                    state.LastException = ex;
                    pc.ResultType = ResultType.Error;
                    var isBanned = ++state.RetryCount == _config.MaxRetries;

                    state.Extensions = info.Extensions;
                    state.Modified = info.Modified;
                    state.ModifiedMultiple = info.ModifiedMultiple;

                    _diagnosticSource.ProcessResourceFailed(processActivity, pc, isBanned, ex);
                }

                await _stateProvider.SaveStateAsync(new[] { state }, ctk).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                // chomp it, we'll retry this file next time, forever, fuckit
                _diagnosticSource.ProcessResourceSaveFailed(info.ResourceId, ex);
            }
        }

        private LocalDateTime? _getEarliestModified(IResourceMetadata info)
        {
            if (info?.ModifiedMultiple != null && info.ModifiedMultiple.Any())
            {
                return info.ModifiedMultiple.Max(x => x.Value);
            }
            else
            {
                return info?.Modified;
            }
        }

    }

    public interface IResourceWatcherConfig
    {
        string Tenant { get; }
        int SleepSeconds { get; }
        int MaxRetries { get; }
        uint DegreeOfParallelism { get; }
        uint? SkipResourcesOlderThanDays { get; }
        bool IgnoreState { get; }
        Duration BanDuration { get; }
        TimeSpan RunDurationNotificationLimit { get; }
        TimeSpan ResourceDurationNotificationLimit { get; }
    }

    public enum RunType
    {
        Once,
        Normal
    }

    public enum ResultType
    {
        Normal,
        NoAction,
        NoNewData,
        Error,
        Skipped
    }

    public enum ProcessType
    {
        New,
        Updated,
        Retry,
        RetryAfterBan,
        Banned,
        NothingToDo
    }

    public class ProcessContext
    {
        public IResourceMetadata CurrentInfo { get; set; }
        public ResourceState LastState { get; set; }
        public ResourceState NewState { get; set; }
        public ProcessType ProcessType { get; set; }
        public ResultType? ResultType { get; set; }
        public int? Index { get; set; }
        public int? Total { get; set; }

        public bool IsNewResource(out Tuple<string,LocalDateTime?,LocalDateTime?> changed)
        {
            if (CurrentInfo.ModifiedMultiple != null && CurrentInfo.ModifiedMultiple.Any())
            {
                if (LastState?.ModifiedMultiple != null && LastState.ModifiedMultiple.Any())
                {
                    if (CurrentInfo.ModifiedMultiple.Where(x => !LastState.ModifiedMultiple.ContainsKey(x.Key)).Any())
                    {
                        //New State contains new sources modified for the resource
                        changed = new Tuple<string, LocalDateTime?, LocalDateTime?>
                                        (
                                            CurrentInfo.ModifiedMultiple.Where(x => !LastState.ModifiedMultiple.ContainsKey(x.Key)).First().Key,
                                            CurrentInfo.ModifiedMultiple.Where(x => !LastState.ModifiedMultiple.ContainsKey(x.Key)).First().Value,
                                            null
                                        );                           
                        
                        return true;
                    }
                    else if (CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.ModifiedMultiple[x.Key]).Any())
                    {
                        //One or more sources have an updated modified respect the corrisponding source into last state ModifiedMultiple
                        changed = new Tuple<string, LocalDateTime?, LocalDateTime?>
                                        (
                                            CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.ModifiedMultiple[x.Key]).First().Key,
                                            CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.ModifiedMultiple[x.Key]).First().Value,
                                            LastState.ModifiedMultiple[CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.ModifiedMultiple[x.Key]).First().Key]
                                        );

                        return true;
                    }
                }
                else if (LastState?.Modified != default)
                {
                    if (CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.Modified).Any())
                    {
                        //One or more sources have an updated modify respect to Modified
                        changed = new Tuple<string, LocalDateTime?, LocalDateTime?>
                                        (
                                            CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.Modified).First().Key,
                                            CurrentInfo.ModifiedMultiple.Where(x => x.Value > LastState.Modified).First().Value,
                                            LastState.Modified
                                        );
                        return true;
                    }
                }
            }
            else if (CurrentInfo.Modified != default)
            {
                if (LastState?.ModifiedMultiple != null && LastState.ModifiedMultiple.Any())
                {
                    if (LastState.ModifiedMultiple.Where(x => x.Value < CurrentInfo.Modified).Any())
                    {
                        //the new single modified is major at least of one old ModifiedMultiple
                        changed = new Tuple<string, LocalDateTime?, LocalDateTime?>
                                    (
                                        null,
                                        CurrentInfo.Modified,
                                        LastState.ModifiedMultiple.Max(x => x.Value)
                                    );
                        return true;
                    }
                }
                else if (LastState?.Modified != default)
                {
                    if (CurrentInfo.Modified > LastState.Modified)
                    {
                        //the new single modified is major than the old
                        changed = new Tuple<string, LocalDateTime?, LocalDateTime?>
                                    (
                                        null,
                                        CurrentInfo.Modified,
                                        LastState.Modified
                                    );
                        return true;
                    }
                }
            }

            changed = null;

            return false;
        }
    }

    public sealed class ChangedStateContext<T> where T : IResourceState
    {
        public ChangedStateContext(IResourceMetadata info, IResourceTrackedState lastState, AsyncLazy<T> payload)
        {
            EnsureArg.IsNotNull(info);
            EnsureArg.IsNotNull(payload);

            Info = info;
            Payload = payload;
            LastState = lastState;
        }

        public AsyncLazy<T> Payload { get; }
        public IResourceMetadata Info { get; }
        public IResourceTrackedState LastState { get; }
    }
}
