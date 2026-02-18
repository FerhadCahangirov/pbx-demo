import { HubConnection, HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { buildApiUrl } from './httpClient';

export function createSoftphoneSignalR(accessToken: string, forceWebSockets = false): HubConnection {
  const transportOptions = forceWebSockets
    ? {
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true
      }
    : {};

  return new HubConnectionBuilder()
    .withUrl(buildApiUrl('/hubs/softphone'), {
      accessTokenFactory: () => accessToken,
      withCredentials: false,
      ...transportOptions
    })
    .withAutomaticReconnect([0, 1000, 3000, 6000, 10000])
    .configureLogging(LogLevel.Warning)
    .build();
}
