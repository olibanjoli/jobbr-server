using System;
using System.Collections.Generic;

using Jobbr.Common.Model;
using Jobbr.Server.Common;
using Jobbr.Server.Logging;

namespace Jobbr.Server.Core
{
    public interface IJobbrRepository
    {
        List<Job> GetAllJobs();

        Job GetJob(long id);

        void UpdateJobRunProgress(long jobRunId, double progress);

        void SetPidForJobRun(JobRun jobRun, int id);

        JobRun GetJobRun(long id);

        List<JobTriggerBase> GetTriggers(long jobId);

        void SaveAddTrigger(RecurringTrigger trigger);

        void UpdatePlannedStartDateTimeUtc(long jobRunId, DateTime plannedStartDateTimeUtc);

        void SaveAddTrigger(ScheduledTrigger trigger);

        void SaveAddTrigger(InstantTrigger trigger);

        JobRun GetLastJobRunByTriggerId(long triggerId);

        JobRun GetNextJobRunByTriggerId(long triggerId);

        bool SaveEnableTrigger(long triggerId, out JobTriggerBase trigger);

        List<JobTriggerBase> GetActiveTriggers();

        JobTriggerBase SaveUpdateTrigger(long id, JobTriggerBase trigger, out bool hadChanges);

        bool CheckParallelExecution(long triggerId);

        List<JobRun> GetJobRunsByState(JobRunState state);

        long AddJob(Job job);

        JobRun SaveNewJobRun(Job job, JobTriggerBase trigger, DateTime startDateTimeUtc);

        void DisableTrigger(long triggerId);

        void Update(JobRun jobRun);

        JobRun GetJobRunById(long jobRunId);

        JobTriggerBase GetTriggerById(long triggerId);

        List<JobTriggerBase> GetTriggersByJobId(long jobId);

        List<JobRun> GetAllJobRuns();

        Job GetJobByUniqueName(string identifier);
    }

    public class JobbrRepository : IJobbrRepository
    {
        private static readonly ILog Logger = LogProvider.For<JobbrRepository>();

        private readonly IJobStorageProvider storageProvider;

        public JobbrRepository(IJobStorageProvider storageProvider)
        {
            this.storageProvider = storageProvider;
        }

        public List<Job> GetAllJobs()
        {
            return storageProvider.GetJobs();
        }

        public Job GetJob(long id)
        {
            return storageProvider.GetJobById(id);
        }

        public void UpdateJobRunProgress(long jobRunId, double progress)
        {
            storageProvider.UpdateProgress(jobRunId, progress);
        }

        public void SetPidForJobRun(JobRun jobRun, int id)
        {
            jobRun.Pid = id;

            storageProvider.Update(jobRun);
        }

        public JobRun GetJobRun(long id)
        {
            return storageProvider.GetJobRunById(id);
        }

        public List<JobTriggerBase> GetTriggers(long jobId)
        {
            return storageProvider.GetTriggersByJobId(jobId);
        }

        public void SaveAddTrigger(RecurringTrigger trigger)
        {
            if (trigger.JobId == 0)
            {
                throw new ArgumentException("JobId is required", "trigger.JobId");
            }

            trigger.Id = this.storageProvider.AddTrigger(trigger);
        }

        public void UpdatePlannedStartDateTimeUtc(long jobRunId, DateTime plannedStartDateTimeUtc)
        {
            var jobRun = this.storageProvider.GetJobRunById(jobRunId);
            jobRun.PlannedStartDateTimeUtc = plannedStartDateTimeUtc;

            this.Update(jobRun);
        }

        public void SaveAddTrigger(ScheduledTrigger trigger)
        {
            if (trigger.JobId == 0)
            {
                throw new ArgumentException("JobId is required", "trigger.JobId");
            }

            trigger.Id = this.storageProvider.AddTrigger(trigger);
        }

        public void SaveAddTrigger(InstantTrigger trigger)
        {
            if (trigger.JobId == 0)
            {
                throw new ArgumentException("JobId is required", "trigger.JobId");
            }

            trigger.Id = this.storageProvider.AddTrigger(trigger);
        }

        public JobRun GetLastJobRunByTriggerId(long triggerId)
        {
            return storageProvider.GetLastJobRunByTriggerId(triggerId);
        }

        public JobRun GetNextJobRunByTriggerId(long triggerId)
        {
            return storageProvider.GetFutureJobRunsByTriggerId(triggerId);
        }

        public bool SaveEnableTrigger(long triggerId, out JobTriggerBase trigger)
        {
            trigger = this.storageProvider.GetTriggerById(triggerId);

            if (trigger.IsActive)
            {
                return false;
            }

            trigger.IsActive = true;
            this.storageProvider.EnableTrigger(triggerId);
            return true;
        }

        public List<JobTriggerBase> GetActiveTriggers()
        {
            try
            {
                return this.storageProvider.GetActiveTriggers();
            }
            catch (Exception e)
            {
                Logger.FatalException("Cannot read active triggers from storage provider due to an exception. Returning empty list.", e);

                return new List<JobTriggerBase>();
            }
        }

        public JobTriggerBase SaveUpdateTrigger(long id, JobTriggerBase trigger, out bool hadChanges)
        {
            var triggerFromDb = this.storageProvider.GetTriggerById(id);

            hadChanges = false;

            if (trigger.IsActive != triggerFromDb.IsActive)
            {
                // Activated or deactivated
                triggerFromDb.IsActive = trigger.IsActive;
                hadChanges = true;
            }

            hadChanges = hadChanges || this.ApplyOtherChanges(triggerFromDb as dynamic, trigger as dynamic);

            if (hadChanges)
            {
                if (trigger is InstantTrigger)
                {
                    this.storageProvider.Update(trigger as InstantTrigger);
                }

                if (trigger is ScheduledTrigger)
                {
                    this.storageProvider.Update(trigger as ScheduledTrigger);
                }

                if (trigger is RecurringTrigger)
                {
                    this.storageProvider.Update(trigger as RecurringTrigger);
                }
            }
            return triggerFromDb;
        }


        private bool ApplyOtherChanges(RecurringTrigger fromDb, RecurringTrigger updatedOne)
        {
            if (fromDb.Definition != updatedOne.Definition)
            {
                fromDb.Definition = updatedOne.Definition;
                return true;
            }

            return false;
        }

        private bool ApplyOtherChanges(ScheduledTrigger fromDb, ScheduledTrigger updatedOne)
        {
            if (fromDb.StartDateTimeUtc != updatedOne.StartDateTimeUtc)
            {
                fromDb.StartDateTimeUtc = updatedOne.StartDateTimeUtc;

                return true;
            }

            return false;
        }

        private bool ApplyOtherChanges(InstantTrigger fromDb, InstantTrigger updatedOne)
        {
            Logger.WarnFormat("Cannot change an instant trigger!");

            return false;
        }

        private bool ApplyOtherChanges(object fromDb, object updatedOne)
        {
            Logger.WarnFormat("Unknown trigger types: From: {1}, To: {2}!", fromDb.GetType(), updatedOne.GetType());
            return false;
        }

        public bool CheckParallelExecution(long triggerId)
        {
            return this.storageProvider.CheckParallelExecution(triggerId);
        }

        public List<JobRun> GetJobRunsByState(JobRunState state)
        {
            return this.storageProvider.GetJobRunsByState(state);
        }

        public long AddJob(Job job)
        {
            return this.storageProvider.AddJob(job);
        }

        public JobRun SaveNewJobRun(Job job, JobTriggerBase trigger, DateTime startDateTimeUtc)
        {
            var jobRun = new JobRun
            {
                JobId = job.Id,
                TriggerId = trigger.Id,
                JobParameters = job.Parameters,
                InstanceParameters = trigger.Parameters,
                UniqueId = Guid.NewGuid().ToString(),
                State = JobRunState.Scheduled,
                PlannedStartDateTimeUtc = startDateTimeUtc
            };

            jobRun.Id = this.storageProvider.AddJobRun(jobRun);
            return jobRun;
        }

        public void DisableTrigger(long triggerId)
        {
            this.storageProvider.DisableTrigger(triggerId);
        }

        public void Update(JobRun jobRun)
        {
            this.storageProvider.Update(jobRun);
        }

        public JobRun GetJobRunById(long jobRunId)
        {
            return this.storageProvider.GetJobRunById(jobRunId);
        }

        public JobTriggerBase GetTriggerById(long triggerId)
        {
            return this.storageProvider.GetTriggerById(triggerId);
        }

        public List<JobTriggerBase> GetTriggersByJobId(long jobId)
        {
            return this.storageProvider.GetTriggersByJobId(jobId);
        }

        public List<JobRun> GetAllJobRuns()
        {
            return this.storageProvider.GetJobRuns();
        }

        public Job GetJobByUniqueName(string identifier)
        {
            return this.storageProvider.GetJobByUniqueName(identifier);
        }
    }
}