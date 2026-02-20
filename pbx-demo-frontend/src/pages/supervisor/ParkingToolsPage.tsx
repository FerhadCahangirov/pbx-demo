interface ParkingToolsPageProps {
  busy: boolean;
  parkingGroupIds: string;
  parkingNumber: string;
  parkingInfo: string;
  onParkingGroupIdsChange: (value: string) => void;
  onParkingNumberChange: (value: string) => void;
  onCreateParking: () => Promise<void>;
  onFindParking: () => Promise<void>;
  onDeleteParking: () => Promise<void>;
}

export function ParkingToolsPage({
  busy,
  parkingGroupIds,
  parkingNumber,
  parkingInfo,
  onParkingGroupIdsChange,
  onParkingNumberChange,
  onCreateParking,
  onFindParking,
  onDeleteParking
}: ParkingToolsPageProps) {
  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <h3>Shared Parking Tools</h3>
        <p className="history-summary">
          Create, lookup, and delete shared parking resources for selected 3CX groups.
        </p>
      </article>

      <article className="card">
        <div className="form-grid">
          <input
            className="input"
            placeholder="Group IDs (comma-separated)"
            value={parkingGroupIds}
            onChange={(event) => onParkingGroupIdsChange(event.target.value)}
          />
          <button className="secondary-button" type="button" onClick={() => void onCreateParking()} disabled={busy}>
            Create Shared Parking
          </button>
          <input
            className="input"
            placeholder="Parking Number (e.g., SP11)"
            value={parkingNumber}
            onChange={(event) => onParkingNumberChange(event.target.value)}
          />
          <div className="grid-two">
            <button className="secondary-button" type="button" onClick={() => void onFindParking()} disabled={busy}>
              Get Parking Details
            </button>
            <button className="danger-button" type="button" onClick={() => void onDeleteParking()} disabled={busy}>
              Delete Parking
            </button>
          </div>
          {parkingInfo && <div className="status-chip status-info">{parkingInfo}</div>}
        </div>
      </article>
    </section>
  );
}
