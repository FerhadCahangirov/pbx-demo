import { QueueNoticeBanner, QueuePanel } from '../components';

interface QueueFeaturePlaceholderPageProps {
  title: string;
  description: string;
  todoMessage: string;
}

export function QueueFeaturePlaceholderPage({ title, description, todoMessage }: QueueFeaturePlaceholderPageProps) {
  return (
    <section className="supervisor-page-stack">
      <QueuePanel title={title} subtitle={description}>
        <QueueNoticeBanner tone="warning" message={todoMessage} />
      </QueuePanel>
    </section>
  );
}

