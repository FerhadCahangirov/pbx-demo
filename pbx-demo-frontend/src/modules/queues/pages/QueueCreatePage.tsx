import type { QueueModel } from '../models/contracts';
import { QueueCreateEditPage } from './QueueCreateEditPage';

interface QueueCreatePageProps {
  accessToken: string;
  onSaved?: (queue: QueueModel) => void;
  onCancel?: () => void;
}

export function QueueCreatePage({ accessToken, onSaved, onCancel }: QueueCreatePageProps) {
  return <QueueCreateEditPage accessToken={accessToken} mode="create" onSaved={onSaved} onCancel={onCancel} />;
}

