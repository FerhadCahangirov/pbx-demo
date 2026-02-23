import { useEffect, useMemo, useState } from 'react';
import { QueueNoticeBanner, QueuePanel } from '../components';
import { useQueueStore } from '../store';
import {
  buildQueueRoutePath,
  createQueueNavigationItems,
  matchQueueRoute,
  type QueueRouteId,
  type QueueRouteMatch
} from '../routes';
import { QueueCallHistoryPage } from './QueueCallHistoryPage';
import { QueueCreatePage } from './QueueCreatePage';
import { QueueDashboardPage } from './QueueDashboardPage';
import { QueueDetailsPage } from './QueueDetailsPage';
import { QueueFeaturePlaceholderPage } from './QueueFeaturePlaceholderPage';
import { QueueListPage } from './QueueListPage';
import { QueueLiveMonitoringPage } from './QueueLiveMonitoringPage';
import { QueueUpdatePage } from './QueueUpdatePage';

interface QueueSupervisorWorkspaceProps {
  accessToken: string;
}

function getCurrentQueueRouteMatch(): QueueRouteMatch {
  const matched = matchQueueRoute(window.location.pathname);
  if (matched) {
    return matched;
  }

  const fallback = matchQueueRoute(buildQueueRoutePath('queue-list'));
  if (!fallback) {
    throw new Error('Queue route definitions are misconfigured: queue-list route did not match its own path.');
  }

  return fallback;
}

function isSameQueueRoute(left: QueueRouteMatch, right: QueueRouteMatch): boolean {
  return left.route.id === right.route.id && left.params.queueId === right.params.queueId;
}

export function QueueSupervisorWorkspace({ accessToken }: QueueSupervisorWorkspaceProps) {
  const activeQueueId = useQueueStore((state) => state.activeQueueId);
  const setActiveQueueId = useQueueStore((state) => state.setActiveQueueId);
  const [routeMatch, setRouteMatch] = useState<QueueRouteMatch>(() => getCurrentQueueRouteMatch());

  useEffect(() => {
    const syncRoute = () => {
      const next = getCurrentQueueRouteMatch();
      setRouteMatch((current) => (isSameQueueRoute(current, next) ? current : next));
    };

    syncRoute();
    window.addEventListener('popstate', syncRoute);
    return () => {
      window.removeEventListener('popstate', syncRoute);
    };
  }, []);

  useEffect(() => {
    const routeQueueId = routeMatch.params.queueId ?? null;
    if (routeQueueId && routeQueueId > 0 && routeQueueId !== activeQueueId) {
      setActiveQueueId(routeQueueId);
    }
  }, [activeQueueId, routeMatch.params.queueId, setActiveQueueId]);

  const navigate = (routeId: QueueRouteId, params?: { queueId?: number | null }) => {
    const path = buildQueueRoutePath(routeId, params);
    if (window.location.pathname !== path) {
      window.history.pushState({}, '', path);
      window.dispatchEvent(new PopStateEvent('popstate'));
      return;
    }

    const next = matchQueueRoute(path);
    if (next) {
      setRouteMatch(next);
    }
  };

  const currentQueueId = routeMatch.params.queueId ?? activeQueueId ?? null;
  const navItems = useMemo(() => createQueueNavigationItems(currentQueueId), [currentQueueId]);

  const renderPage = () => {
    switch (routeMatch.route.id) {
      case 'queue-list':
        return (
          <QueueListPage
            accessToken={accessToken}
            onCreateQueue={() => navigate('queue-create')}
            onEditQueue={(queueId) => navigate('queue-update', { queueId })}
            onOpenQueueDetails={(queueId) => navigate('queue-details', { queueId })}
            onOpenLiveMonitoring={(queueId) => navigate('queue-live-monitor', { queueId })}
            onOpenAnalytics={(queueId) => navigate('queue-analytics', { queueId })}
            onOpenCallHistory={(queueId) => navigate('queue-call-history', { queueId })}
          />
        );

      case 'queue-create':
        return (
          <QueueCreatePage
            accessToken={accessToken}
            onSaved={(queue) => navigate('queue-details', { queueId: queue.id })}
            onCancel={() => navigate('queue-list')}
          />
        );

      case 'queue-details': {
        if (!routeMatch.params.queueId) {
          return <QueueNoticeBanner tone="error" message="Queue details route requires a queueId." />;
        }

        const queueId = routeMatch.params.queueId;
        return (
          <QueueDetailsPage
            accessToken={accessToken}
            queueId={queueId}
            onEditQueue={(id) => navigate('queue-update', { queueId: id })}
            onOpenLiveMonitoring={(id) => navigate('queue-live-monitor', { queueId: id })}
            onOpenAnalytics={(id) => navigate('queue-analytics', { queueId: id })}
            onOpenCallHistory={(id) => navigate('queue-call-history', { queueId: id })}
          />
        );
      }

      case 'queue-update': {
        if (!routeMatch.params.queueId) {
          return <QueueNoticeBanner tone="error" message="Queue update route requires a queueId." />;
        }

        const queueId = routeMatch.params.queueId;
        return (
          <QueueUpdatePage
            accessToken={accessToken}
            queueId={queueId}
            onSaved={(queue) => navigate('queue-details', { queueId: queue.id })}
            onCancel={() => navigate('queue-details', { queueId })}
          />
        );
      }

      case 'queue-live-monitor': {
        if (!routeMatch.params.queueId) {
          return <QueueNoticeBanner tone="error" message="Queue live monitoring route requires a queueId." />;
        }

        return (
          <QueueLiveMonitoringPage
            accessToken={accessToken}
            queueId={routeMatch.params.queueId}
            onSelectQueue={(id) => navigate('queue-live-monitor', { queueId: id })}
          />
        );
      }

      case 'queue-analytics': {
        if (!routeMatch.params.queueId) {
          return <QueueNoticeBanner tone="error" message="Queue analytics route requires a queueId." />;
        }

        return (
          <QueueDashboardPage
            accessToken={accessToken}
            queueId={routeMatch.params.queueId}
            onSelectQueue={(id) => navigate('queue-analytics', { queueId: id })}
          />
        );
      }

      case 'queue-call-history': {
        if (!routeMatch.params.queueId) {
          return <QueueNoticeBanner tone="error" message="Queue call history route requires a queueId." />;
        }

        return <QueueCallHistoryPage accessToken={accessToken} queueId={routeMatch.params.queueId} />;
      }

      case 'queue-agent-activity':
        return (
          <QueueFeaturePlaceholderPage
            title="Queues / Agent Activity"
            description="Route is available, but no dedicated backend endpoint/controller is exposed yet for agent activity queries."
            todoMessage="TODO(BE): Add queue agent activity endpoint (e.g. GET /api/queue-analytics/agent-activity or /api/queues/agent-activity)."
          />
        );

      case 'queue-sla-dashboard':
        return (
          <QueueFeaturePlaceholderPage
            title="Queues / SLA Dashboard"
            description="Route is available for a cross-queue SLA dashboard, but a dedicated backend summary endpoint is not exposed yet."
            todoMessage="TODO(BE): Add aggregate SLA dashboard endpoint across queues for supervisor summary use cases."
          />
        );

      default:
        return (
          <QueuePanel title="Queues" subtitle="Unknown queue route">
            <QueueNoticeBanner tone="error" message={`Unsupported queue route: ${routeMatch.route.id}`} />
          </QueuePanel>
        );
    }
  };

  return (
    <section className="supervisor-page-stack">
      <QueuePanel title="Queue Operations" subtitle="Supervisor queue management module with CRUD, analytics, live monitoring, and history placeholder.">
        <div className="flex flex-wrap gap-2">
          {navItems.map((item) => {
            const itemMatch = matchQueueRoute(item.href);
            const isActive = itemMatch?.route.id === routeMatch.route.id;
            return (
              <button
                key={item.id}
                type="button"
                className={isActive ? 'primary-button' : 'secondary-button'}
                onClick={() => {
                  if (itemMatch?.route.requiresQueueId && !currentQueueId) {
                    navigate('queue-list');
                    return;
                  }
                  window.history.pushState({}, '', item.href);
                  window.dispatchEvent(new PopStateEvent('popstate'));
                }}
              >
                {item.label}
              </button>
            );
          })}
        </div>
      </QueuePanel>

      {renderPage()}
    </section>
  );
}

