import type { QueueModel } from '../models/contracts';
import { QueueCreateEditPage } from './QueueCreateEditPage';

interface QueueUpdatePageProps {
  accessToken: string;
  queueId: number;
  onSaved?: (queue: QueueModel) => void;
  onCancel?: () => void;
}

export function QueueUpdatePage({ accessToken, queueId, onSaved, onCancel }: QueueUpdatePageProps) {
  return (
    <QueueCreateEditPage
      accessToken={accessToken}
      mode="edit"
      queueId={queueId}
      onSaved={onSaved}
      onCancel={onCancel}
    />
  );
}

