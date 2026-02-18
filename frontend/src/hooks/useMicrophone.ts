import { useCallback, useEffect, useRef, useState } from 'react';
import { requireMediaDevicesWithGetUserMedia } from '../services/mediaDevices';

export type MicrophonePermission = 'unknown' | 'granted' | 'denied';

export function useMicrophone() {
  const [permission, setPermission] = useState<MicrophonePermission>('unknown');
  const [muted, setMuted] = useState(false);
  const streamRef = useRef<MediaStream | null>(null);

  const requestPermission = useCallback(async () => {
    if (streamRef.current) {
      setPermission('granted');
      return;
    }

    try {
      const mediaDevices = requireMediaDevicesWithGetUserMedia();
      const stream = await mediaDevices.getUserMedia({ audio: true, video: false });
      streamRef.current = stream;
      setPermission('granted');
      stream.getAudioTracks().forEach((track) => {
        track.enabled = !muted;
      });
    } catch {
      setPermission('denied');
    }
  }, [muted]);

  useEffect(() => {
    const stream = streamRef.current;
    if (!stream) {
      return;
    }

    stream.getAudioTracks().forEach((track) => {
      track.enabled = !muted;
    });
  }, [muted]);

  useEffect(() => {
    return () => {
      const stream = streamRef.current;
      if (!stream) {
        return;
      }

      stream.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    };
  }, []);

  return {
    permission,
    muted,
    setMuted,
    requestPermission
  };
}
