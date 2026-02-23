/*
Batch 1 contract freeze: Queue module frontend models.

MASTER NAMING TABLE
Concept          Backend/Internal                  Frontend
Queue            QueueDto                          QueueModel
Queue Call       QueueCallEntity / live DTOs       QueueCallLiveModel
Agent            ExtensionEntity / agent DTOs      QueueAgentModel
Queue Live       QueueLiveSnapshotDto              QueueLiveSnapshotModel
SignalR Message  Queue*UpdatedMessage              Queue*UpdatedMessage
*/

export type QueueLifecycleStatus =
  | 'Unknown'
  | 'EnteredQueue'
  | 'Waiting'
  | 'Ringing'
  | 'Answered'
  | 'Transferred'
  | 'Completed'
  | 'Missed'
  | 'Abandoned';

export type QueueCallDisposition =
  | 'Unknown'
  | 'Answered'
  | 'Missed'
  | 'Abandoned'
  | 'Transferred'
  | 'Completed';

export interface QueueODataQueryModel {
  top?: number;
  skip?: number;
  search?: string | null;
  filter?: string | null;
  count?: boolean | null;
  orderBy?: string[];
  select?: string[];
  expand?: string[];
}

export interface QueueListQueryModel {
  search?: string | null;
  isRegistered?: boolean | null;
  queueNumber?: string | null;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDescending: boolean;
}

export interface QueueCallHistoryQueryModel {
  fromUtc?: string | null;
  toUtc?: string | null;
  disposition?: string | null;
  agentId?: number | null;
  search?: string | null;
  page: number;
  pageSize: number;
}

// TODO(BE): Add queue call history endpoint and align this model to the actual backend response.
// Proposed route: GET /api/queues/{queueId}/history
export interface QueueCallHistoryItemModel {
  id: string;
  queueId: number;
  queueCallId?: number | null;
  callerNumber?: string | null;
  callerName?: string | null;
  callType?: string | null;
  callStartUtc: string;
  callEndUtc?: string | null;
  durationMs?: number | null;
  waitMs?: number | null;
  agentId?: number | null;
  agentExtension?: string | null;
  agentDisplayName?: string | null;
  outcome?: string | null;
  disposition?: string | null;
}

export interface QueueAnalyticsQueryModel {
  fromUtc: string;
  toUtc: string;
  bucket: 'hour' | 'day' | 'month' | string;
  slaThresholdSec?: number | null;
  timeZoneId?: string | null;
}

export interface QueuePagedResult<T> {
  totalCount?: number | null;
  items: T[];
}

export interface QueueAgentAssignmentModel {
  extensionId?: number | null;
  extensionNumber: string;
  displayName?: string | null;
  skillGroup?: string | null;
}

export interface QueueManagerAssignmentModel {
  extensionId?: number | null;
  extensionNumber: string;
  displayName?: string | null;
}

export interface QueueResetStatisticsScheduleModel {
  frequency?: string | null;
  dayOfWeek?: string | null;
  time?: string | null;
}

export interface QueueDestinationModel {
  to?: string | null;
  number?: string | null;
  external?: string | null;
  name?: string | null;
  type?: string | null;
  tags?: string[] | null;
}

export interface QueueRouteModel {
  isPromptEnabled?: boolean | null;
  prompt?: string | null;
  route?: QueueDestinationModel | null;
}

export interface QueueSettingsModel {
  agentAvailabilityMode?: boolean | null;
  announcementIntervalSec?: number | null;
  announceQueuePosition?: boolean | null;
  callbackEnableTimeSec?: number | null;
  callbackPrefix?: string | null;
  enableIntro?: boolean | null;
  greetingFile?: string | null;
  introFile?: string | null;
  onHoldFile?: string | null;
  promptSet?: string | null;
  playFullPrompt?: boolean | null;
  priorityQueue?: boolean | null;
  ringTimeoutSec?: number | null;
  masterTimeoutSec?: number | null;
  maxCallersInQueue?: number | null;
  slaTimeSec?: number | null;
  wrapUpTimeSec?: number | null;
  pollingStrategy?: string | null;
  recordingMode?: string | null;
  notifyCodes: string[];
  resetStatisticsScheduleEnabled?: boolean | null;
  resetQueueStatisticsSchedule?: QueueResetStatisticsScheduleModel | null;
  breakRoute?: QueueRouteModel | null;
  holidaysRoute?: QueueRouteModel | null;
  outOfOfficeRoute?: QueueRouteModel | null;
  forwardNoAnswer?: QueueDestinationModel | null;
}

export interface QueueModel {
  id: number;
  pbxQueueId: number;
  queueNumber: string;
  name: string;
  isRegistered?: boolean | null;
  settings: QueueSettingsModel;
  agents: QueueAgentAssignmentModel[];
  managers: QueueManagerAssignmentModel[];
}

export interface CreateQueueRequestModel {
  number: string;
  name: string;
  settings: QueueSettingsModel;
  agents: QueueAgentAssignmentModel[];
  managers: QueueManagerAssignmentModel[];
}

export interface UpdateQueueRequestModel {
  name?: string | null;
  settings?: QueueSettingsModel | null;
  agents?: QueueAgentAssignmentModel[] | null;
  managers?: QueueManagerAssignmentModel[] | null;
  replaceAgents: boolean;
  replaceManagers: boolean;
}

export interface QueueWaitingCallLiveModel {
  callKey: string;
  queueCallId?: number | null;
  pbxCallId?: number | null;
  callerNumber?: string | null;
  callerName?: string | null;
  waitOrder: number;
  waitingMs?: number | null;
  estimatedOrder: boolean;
}

export interface QueueActiveCallLiveModel {
  callKey: string;
  queueCallId?: number | null;
  pbxCallId?: number | null;
  status: string;
  agentId?: number | null;
  agentExtension?: string | null;
  talkingMs?: number | null;
}

export interface QueueAgentLiveStatusModel {
  agentId: number;
  extensionNumber: string;
  displayName?: string | null;
  queueStatus: string;
  activityType: string;
  currentCallKey?: string | null;
  atUtc: string;
}

export interface QueueStatsSummaryModel {
  queueId: number;
  asOfUtc: string;
  waitingCount: number;
  activeCount: number;
  loggedInAgents: number;
  availableAgents: number;
  averageWaitingMs?: number | null;
  slaPct?: number | null;
  answeredCount: number;
  abandonedCount: number;
}

export interface QueueLiveSnapshotModel {
  queueId: number;
  asOfUtc: string;
  version: number;
  waitingCalls: QueueWaitingCallLiveModel[];
  activeCalls: QueueActiveCallLiveModel[];
  agentStatuses: QueueAgentLiveStatusModel[];
  stats: QueueStatsSummaryModel;
}

export interface QueueWaitingListUpdatedMessage {
  queueId: number;
  asOfUtc: string;
  version: number;
  waitingCalls: QueueWaitingCallLiveModel[];
}

export interface QueueActiveCallsUpdatedMessage {
  queueId: number;
  asOfUtc: string;
  version: number;
  activeCalls: QueueActiveCallLiveModel[];
}

export interface QueueAgentStatusChangedMessage {
  queueId?: number | null;
  agentId: number;
  extensionNumber: string;
  queueStatus: string;
  activityType: string;
  currentCallKey?: string | null;
  atUtc: string;
}

export interface QueueStatsUpdatedMessage {
  queueId: number;
  asOfUtc: string;
  stats: QueueStatsSummaryModel;
}

export interface QueueCongestionSignalBreakdownModel {
  waitingLoadScore: number;
  waitingTimeScore: number;
  slaBreachScore: number;
  compositeScore: number;
}

export interface QueueTimeSeriesBucketModel {
  queueId: number;
  bucket: string;
  timeZoneId: string;
  bucketStartUtc: string;
  bucketEndUtc: string;
  bucketLabel: string;
  totalCalls: number;
  answeredCalls: number;
  abandonedCalls: number;
  missedCalls: number;
  averageWaitingMs?: number | null;
  averageTalkingMs?: number | null;
  slaPct?: number | null;
  queueCongestionIndex?: number | null;
  peakConcurrency?: number | null;
}

export interface QueueComparisonComponentBreakdownModel {
  serviceLevelComponent: number;
  waitingInverseComponent: number;
  abandonmentInverseComponent: number;
}

export interface QueueComparisonScoreModel {
  queueId: number;
  rank: number;
  mqi: number;
  components: QueueComparisonComponentBreakdownModel;
}

export interface QueueComparisonSummaryModel {
  normalizationMethod: string;
  scores: QueueComparisonScoreModel[];
}

export interface QueueAgentRankingComponentsModel {
  slaComplianceComponent: number;
  answerRateComponent: number;
  handleTimeInverseComponent: number;
  utilizationComponent: number;
}

export interface QueueAgentRankingModel {
  queueId: number;
  agentId: number;
  extensionNumber: string;
  displayName?: string | null;
  rank: number;
  answeredCalls: number;
  averageWaitingMs?: number | null;
  averageTalkingMs?: number | null;
  slaCompliancePct?: number | null;
  utilizationPct: number;
  occupancyPct: number;
  agentRankingScore: number;
  components: QueueAgentRankingComponentsModel;
}

export interface QueueAnalyticsOverviewModel {
  queueId: number;
  query: QueueAnalyticsQueryModel;
  generatedAtUtc: string;
  totalCalls: number;
  answeredCalls: number;
  abandonedCalls: number;
  missedCalls: number;
  shortAbandonCount: number;
  abandonmentRatePct?: number | null;
  averageWaitingMs?: number | null;
  averageWaitingMsAnswered?: number | null;
  averageWaitingMsAll?: number | null;
  averageTalkingMs?: number | null;
  p90WaitingMs?: number | null;
  p95WaitingMs?: number | null;
  p90TalkingMs?: number | null;
  peakConcurrency?: number | null;
  peakConcurrencyAtUtc?: string | null;
  slaThresholdSec?: number | null;
  slaEligibleCalls: number;
  slaWithinThresholdCalls?: number | null;
  slaBreachCalls?: number | null;
  slaPct?: number | null;
  queueCongestionIndex?: number | null;
  congestionSignals?: QueueCongestionSignalBreakdownModel | null;
  realtimeClassification?: string | null;
  comparisonScore?: QueueComparisonScoreModel | null;
  timeSeries: QueueTimeSeriesBucketModel[];
  agentRankings: QueueAgentRankingModel[];
  buckets: unknown[];
}

export interface QueueAnalyticsComparisonModel {
  query: QueueAnalyticsQueryModel;
  generatedAtUtc: string;
  queues: QueueAnalyticsOverviewModel[];
  comparison: QueueComparisonSummaryModel;
}

export interface QueueOutboxPublishResultModel {
  processed: number;
}

export interface QueueSnapshotPublishAcceptedModel {
  queueId: number;
}

export const QUEUE_MODULE_BATCH1_FOLDER_STRUCTURE = [
  'modules/queues/pages',
  'modules/queues/components',
  'modules/queues/hooks',
  'modules/queues/services',
  'modules/queues/models',
  'modules/queues/store',
] as const;
