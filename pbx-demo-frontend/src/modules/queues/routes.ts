export type QueueRouteId =
  | 'queue-list'
  | 'queue-create'
  | 'queue-details'
  | 'queue-update'
  | 'queue-live-monitor'
  | 'queue-analytics'
  | 'queue-call-history'
  | 'queue-agent-activity'
  | 'queue-sla-dashboard';

export interface QueueRouteDefinition {
  id: QueueRouteId;
  title: string;
  pathTemplate: string;
  pathPattern: RegExp;
  pageBatch: 10;
  requiresQueueId: boolean;
}

export interface QueueRouteMatch {
  route: QueueRouteDefinition;
  params: {
    queueId?: number;
  };
}

export interface QueueRouteNavItem {
  id: QueueRouteId;
  label: string;
  href: string;
}

export const QUEUE_ROUTE_PREFIX = '/supervisor/queues';

const queueRouteDefinitionsInternal: QueueRouteDefinition[] = [
  {
    id: 'queue-list',
    title: 'Queue List',
    pathTemplate: QUEUE_ROUTE_PREFIX,
    pathPattern: /^\/supervisor\/queues\/?$/i,
    pageBatch: 10,
    requiresQueueId: false
  },
  {
    id: 'queue-create',
    title: 'Create Queue',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/new`,
    pathPattern: /^\/supervisor\/queues\/new\/?$/i,
    pageBatch: 10,
    requiresQueueId: false
  },
  {
    id: 'queue-details',
    title: 'Queue Details',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/:queueId`,
    pathPattern: /^\/supervisor\/queues\/(?<queueId>\d+)\/?$/i,
    pageBatch: 10,
    requiresQueueId: true
  },
  {
    id: 'queue-update',
    title: 'Update Queue',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/:queueId/edit`,
    pathPattern: /^\/supervisor\/queues\/(?<queueId>\d+)\/edit\/?$/i,
    pageBatch: 10,
    requiresQueueId: true
  },
  {
    id: 'queue-live-monitor',
    title: 'Live Monitoring Dashboard',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/:queueId/live`,
    pathPattern: /^\/supervisor\/queues\/(?<queueId>\d+)\/live\/?$/i,
    pageBatch: 10,
    requiresQueueId: true
  },
  {
    id: 'queue-analytics',
    title: 'Queue Analytics Dashboard',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/:queueId/analytics`,
    pathPattern: /^\/supervisor\/queues\/(?<queueId>\d+)\/analytics\/?$/i,
    pageBatch: 10,
    requiresQueueId: true
  },
  {
    id: 'queue-call-history',
    title: 'Queue Call History',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/:queueId/history`,
    pathPattern: /^\/supervisor\/queues\/(?<queueId>\d+)\/history\/?$/i,
    pageBatch: 10,
    requiresQueueId: true
  },
  {
    id: 'queue-agent-activity',
    title: 'Agent Activity',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/agent-activity`,
    pathPattern: /^\/supervisor\/queues\/agent-activity\/?$/i,
    pageBatch: 10,
    requiresQueueId: false
  },
  {
    id: 'queue-sla-dashboard',
    title: 'SLA Dashboard',
    pathTemplate: `${QUEUE_ROUTE_PREFIX}/sla`,
    pathPattern: /^\/supervisor\/queues\/sla\/?$/i,
    pageBatch: 10,
    requiresQueueId: false
  }
];

export const queueRouteDefinitions = queueRouteDefinitionsInternal as readonly QueueRouteDefinition[];

export function buildQueueRoutePath(routeId: QueueRouteId, params?: { queueId?: number | null }): string {
  const route = queueRouteDefinitionsInternal.find((item) => item.id === routeId);
  if (!route) {
    throw new Error(`Unknown queue route '${routeId}'.`);
  }

  if (!route.requiresQueueId) {
    return route.pathTemplate;
  }

  const queueId = params?.queueId ?? null;
  if (!queueId || !Number.isFinite(queueId) || queueId <= 0) {
    throw new Error(`Route '${routeId}' requires a positive queueId.`);
  }

  return route.pathTemplate.replace(':queueId', String(queueId));
}

export function matchQueueRoute(pathname: string): QueueRouteMatch | null {
  for (const route of queueRouteDefinitionsInternal) {
    const match = route.pathPattern.exec(pathname);
    if (!match) {
      continue;
    }

    const queueIdRaw = match.groups?.queueId;
    const queueId = queueIdRaw ? Number(queueIdRaw) : undefined;

    return {
      route,
      params: {
        queueId: Number.isFinite(queueId) ? queueId : undefined
      }
    };
  }

  return null;
}

export function isQueueRoutePath(pathname: string): boolean {
  return matchQueueRoute(pathname) !== null;
}

export function createQueueNavigationItems(defaultQueueId?: number | null): QueueRouteNavItem[] {
  const queueId = defaultQueueId && defaultQueueId > 0 ? defaultQueueId : null;

  return [
    { id: 'queue-list', label: 'Queues', href: buildQueueRoutePath('queue-list') },
    { id: 'queue-create', label: 'New Queue', href: buildQueueRoutePath('queue-create') },
    {
      id: 'queue-live-monitor',
      label: 'Queue Live',
      href: queueId ? buildQueueRoutePath('queue-live-monitor', { queueId }) : buildQueueRoutePath('queue-list')
    },
    {
      id: 'queue-analytics',
      label: 'Queue Analytics',
      href: queueId ? buildQueueRoutePath('queue-analytics', { queueId }) : buildQueueRoutePath('queue-list')
    },
    {
      id: 'queue-call-history',
      label: 'Call History',
      href: queueId ? buildQueueRoutePath('queue-call-history', { queueId }) : buildQueueRoutePath('queue-list')
    },
    { id: 'queue-agent-activity', label: 'Agent Activity', href: buildQueueRoutePath('queue-agent-activity') },
    { id: 'queue-sla-dashboard', label: 'SLA Dashboard', href: buildQueueRoutePath('queue-sla-dashboard') }
  ];
}
