﻿using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DiagnosticAdapter;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ark.Tools.ResourceWatcher.ApplicationInsights
{ 
    public class ResourceWatcherDiagnosticListener : DiagnosticListenerBase, IObserver<DiagnosticListener>, IDisposable
    {
        protected readonly TelemetryClient Client;
        protected readonly TelemetryConfiguration Configuration;

        private readonly List<IDisposable> subscription = new List<IDisposable>();
        private const string _type = "ProcessStep";

        public ResourceWatcherDiagnosticListener(TelemetryConfiguration configuration)
        {
            this.Configuration = configuration;
            this.Client = new TelemetryClient(configuration);
            this.Client.InstrumentationKey = configuration.InstrumentationKey;

            this.subscription.Add(DiagnosticListener.AllListeners.Subscribe(this));
        }

        public void Dispose()
        {
            foreach (var sub in subscription)
            {
                sub.Dispose();
            }
        }

        void IObserver<DiagnosticListener>.OnCompleted()
        {

        }

        void IObserver<DiagnosticListener>.OnError(Exception error)
        {

        }

        void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
        {
            if (value.Name == "Ark.Tools.ResourceWatcher")
            {
                this.subscription.Add(value.SubscribeWithAdapter(this));
            }
        }

        #region Event
        [DiagnosticName("Ark.Tools.ResourceWatcher.HostStartEvent")]
        public override void OnHostStartEvent()
        {
            var telemetry = new EventTelemetry
            {
                Name = "Ark.Tools.ResourceWatcher.HostStartEvent",
            };
            
            this.Client.TrackEvent(telemetry);
        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.RunTookTooLong")]
        public override void RunTookTooLong(string tenant, TimeSpan elapsed)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new EventTelemetry
            {
                Name = currentActivity.OperationName,
            };

            // properly fill dependency telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;
            telemetry.Timestamp = currentActivity.StartTimeUtc;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Metrics.Add("ElapsedSeconds", elapsed.TotalSeconds);

            this.Client.TrackEvent(telemetry);
        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.ProcessResourceTookTooLong")]
        public override void OnProcessResourceTookTooLong(string tenant, string resourceId, TimeSpan elapsed)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new EventTelemetry
            {
                Name = currentActivity.OperationName,   
            };

            // properly fill dependency telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;
            telemetry.Timestamp = currentActivity.StartTimeUtc;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Properties.Add("ResourceId", resourceId);
            telemetry.Metrics.Add("ElapsedSeconds", elapsed.TotalSeconds);

            this.Client.TrackEvent(telemetry);
        }
        #endregion

        #region Exception
        [DiagnosticName("Ark.Tools.ResourceWatcher.ThrowDuplicateResourceIdRetrived")]
        public override void OnDuplicateResourceIdRetrived(string tenant, Exception exception)
        {
            var telemetryException = new ExceptionTelemetry
            {
                Exception = exception,
                Message = exception.Message
            };

            telemetryException.Properties.Add("Tenant", tenant);

            this.Client.TrackException(telemetryException);
        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.ReportRunConsecutiveFailureLimitReached")]
        public override void OnReportRunConsecutiveFailureLimitReached(string tenant, Exception exception)
        {
            var telemetryException = new ExceptionTelemetry
            {
                Exception = exception,
                Message = exception.Message
            };

            telemetryException.Properties.Add("Tenant", tenant);

            this.Client.TrackException(telemetryException);
        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.ProcessResourceSaveFailed")]
        public override void OnProcessResourceSaveFailed(string resourceId, string tenant, Exception exception)
        {
            var telemetryException = new ExceptionTelemetry
            {
                Exception = exception,
                Message = exception.Message
            };

            telemetryException.Properties.Add("Tenant", tenant);

            this.Client.TrackException(telemetryException);
        }
        #endregion

        #region Run
        [DiagnosticName("Ark.Tools.ResourceWatcher.Run")]
        public override void OnRun()
        {
        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.Run.Start")]
        public override void OnRunStart(RunType runType)
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.Run.Stop")]
        public override void OnRunStop(int totalResources, string tenant, Exception exception)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new RequestTelemetry
            {
                Id = currentActivity.Id,
                Duration = currentActivity.Duration,
                Name = currentActivity.OperationName,
                Success = exception == null ? true : false,
                Timestamp = currentActivity.StartTimeUtc,
            };

            //Telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Metrics.Add("TotalResources", totalResources);

            //Exception
            if (exception != null)
            {
                var telemetryException = new ExceptionTelemetry
                {
                    Exception = exception,
                    Message = exception.Message
                };

                telemetryException.Properties.Add("Tenant", tenant);
                telemetryException.Metrics.Add("TotalResources", totalResources);

                this.Client.TrackException(telemetryException);
            }

            this.Client.TrackRequest(telemetry);
        }
        #endregion 

        #region GetResources
        [DiagnosticName("Ark.Tools.ResourceWatcher.GetResources")]
        public override void OnGetResources()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.GetResources.Start")]
        public override void OnGetResourcesStart()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.GetResources.Stop")]
        public override void OnGetResourcesStop(int resourcesFound, TimeSpan elapsed, string tenant, Exception exception)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new DependencyTelemetry
            {
                Id = currentActivity.Id,
                Duration = currentActivity.Duration,
                Name = currentActivity.OperationName,
                Success = exception == null ? true : false,
                Timestamp = currentActivity.StartTimeUtc,
                Type = _type
            };

            //Telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Metrics.Add("ResourcesFound", resourcesFound);

            //Exception
            if (exception != null)
            {
                var telemetryException = new ExceptionTelemetry
                {
                    Exception = exception,
                    Message = exception.Message
                };

                telemetryException.Properties.Add("Tenant", tenant);
                telemetryException.Metrics.Add("ResourcesFound", resourcesFound);

                this.Client.TrackException(telemetryException);
            }

            this.Client.TrackDependency(telemetry);
        }
        #endregion

        #region CheckState
        [DiagnosticName("Ark.Tools.ResourceWatcher.CheckState")]
        public override void OnCheckState()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.CheckState.Start")]
        public override void OnCheckStateStart()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.CheckState.Stop")]
        public override void OnCheckStateStop(     int resourcesNew
                                                , int resourcesUpdated
                                                , int resourcesRetried
                                                , int resourcesRetriedAfterBan
                                                , string tenant
                                                , Exception exception)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new DependencyTelemetry
            {
                Id = currentActivity.Id,
                Duration = currentActivity.Duration,
                Name = currentActivity.OperationName,
                Success = exception == null ? true : false,
                Timestamp = currentActivity.StartTimeUtc,
                Type = _type
            };

            //Telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Metrics.Add("ResourcesNew", resourcesNew);
            telemetry.Metrics.Add("ResourcesUpdated", resourcesUpdated);
            telemetry.Metrics.Add("ResourcesRetried", resourcesRetried);
            telemetry.Metrics.Add("ResourcesRetriedAfterBan", resourcesRetriedAfterBan);

            //Exception
            if (exception != null)
            {
                var telemetryException = new ExceptionTelemetry
                {
                    Exception = exception,
                    Message = exception.Message
                };

                telemetryException.Properties.Add("Tenant", tenant);

                this.Client.TrackException(telemetryException);
            }

            this.Client.TrackDependency(telemetry);
        }
        #endregion

        #region ProcessResource
        [DiagnosticName("Ark.Tools.ResourceWatcher.ProcessResource")]
        public override void OnProcessResource()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.ProcessResource.Start")]
        public override void OnProcessResourceStart()
        {

        }

        [DiagnosticName("Ark.Tools.ResourceWatcher.ProcessResource.Stop")]
        public override void OnProcessResourceStop(string resourceId, ProcessDataType processDataType, IResourceState state, string tenant, Exception exception)
        {
            Activity currentActivity = Activity.Current;

            var telemetry = new DependencyTelemetry
            {
                Id = currentActivity.Id,
                Duration = currentActivity.Duration,
                Name = currentActivity.OperationName,
                Success = exception == null ? true : false,
                Timestamp = currentActivity.StartTimeUtc,
                Type = _type
            };

            //Telemetry operation context
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;

            //Properties and metrics
            telemetry.Properties.Add("Tenant", tenant);
            telemetry.Properties.Add("ResourceId", resourceId);
            telemetry.Properties.Add("ProcessDataType", processDataType.ToString());

            if (state != default)
            {
                telemetry.Properties.Add("RetrievedAt", state.RetrievedAt.ToString());
                telemetry.Properties.Add("CheckSum", state.CheckSum);
            }

            //Exception
            if (exception != null)
            {
                var telemetryException = new ExceptionTelemetry
                {
                    Exception = exception,
                    Message = exception.Message
                };

                telemetryException.Properties.Add("Tenant", tenant);
                telemetryException.Properties.Add("ResourceId", resourceId);
                
                this.Client.TrackException(telemetryException);
            }

            this.Client.TrackDependency(telemetry);
        }
        #endregion
    }
}