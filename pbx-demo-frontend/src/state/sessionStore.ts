import type {
  BrowserCallView,
  SessionSnapshotResponse,
  SoftphoneEventEnvelope,
  StoredAuthSession
} from '../domain/softphone';

export interface SessionState {
  auth: StoredAuthSession | null;
  snapshot: SessionSnapshotResponse | null;
  browserCalls: BrowserCallView[];
  events: SoftphoneEventEnvelope[];
  bootstrapLoading: boolean;
  busy: boolean;
  errorMessage: string | null;
}

export type SessionAction =
  | { type: 'SET_AUTH'; payload: StoredAuthSession | null }
  | { type: 'SET_SNAPSHOT'; payload: SessionSnapshotResponse | null }
  | { type: 'SET_BROWSER_CALLS'; payload: BrowserCallView[] }
  | { type: 'UPSERT_BROWSER_CALL'; payload: BrowserCallView }
  | { type: 'SET_BOOTSTRAP_LOADING'; payload: boolean }
  | { type: 'SET_BUSY'; payload: boolean }
  | { type: 'SET_ERROR'; payload: string | null }
  | { type: 'PUSH_EVENT'; payload: SoftphoneEventEnvelope }
  | { type: 'CLEAR_EVENTS' }
  | { type: 'CLEAR_BROWSER_CALLS' };

export function createInitialSessionState(auth: StoredAuthSession | null): SessionState {
  return {
    auth,
    snapshot: null,
    browserCalls: [],
    events: [],
    bootstrapLoading: Boolean(auth),
    busy: false,
    errorMessage: null
  };
}

export function sessionReducer(state: SessionState, action: SessionAction): SessionState {
  switch (action.type) {
    case 'SET_AUTH':
      return {
        ...state,
        auth: action.payload
      };
    case 'SET_SNAPSHOT':
      return {
        ...state,
        snapshot: action.payload
      };
    case 'SET_BROWSER_CALLS':
      return {
        ...state,
        browserCalls: sortCalls(action.payload)
      };
    case 'UPSERT_BROWSER_CALL': {
      const callIndex = state.browserCalls.findIndex((current) => current.callId === action.payload.callId);
      if (callIndex < 0) {
        return {
          ...state,
          browserCalls: sortCalls([action.payload, ...state.browserCalls]).slice(0, 20)
        };
      }

      const updated = [...state.browserCalls];
      updated[callIndex] = action.payload;
      return {
        ...state,
        browserCalls: sortCalls(updated).slice(0, 20)
      };
    }
    case 'SET_BOOTSTRAP_LOADING':
      return {
        ...state,
        bootstrapLoading: action.payload
      };
    case 'SET_BUSY':
      return {
        ...state,
        busy: action.payload
      };
    case 'SET_ERROR':
      return {
        ...state,
        errorMessage: action.payload
      };
    case 'PUSH_EVENT':
      return {
        ...state,
        events: [action.payload, ...state.events].slice(0, 30)
      };
    case 'CLEAR_EVENTS':
      return {
        ...state,
        events: []
      };
    case 'CLEAR_BROWSER_CALLS':
      return {
        ...state,
        browserCalls: []
      };
    default:
      return state;
  }
}

function sortCalls(calls: BrowserCallView[]): BrowserCallView[] {
  return [...calls].sort((left, right) => {
    const leftOrder = statusOrder(left.status);
    const rightOrder = statusOrder(right.status);
    if (leftOrder !== rightOrder) {
      return leftOrder - rightOrder;
    }

    const rightDate = Date.parse(right.createdAtUtc);
    const leftDate = Date.parse(left.createdAtUtc);
    return rightDate - leftDate;
  });
}

function statusOrder(status: BrowserCallView['status']): number {
  switch (status) {
    case 'Ringing':
      return 0;
    case 'Connecting':
      return 1;
    case 'Connected':
      return 2;
    case 'Ended':
    default:
      return 3;
  }
}
