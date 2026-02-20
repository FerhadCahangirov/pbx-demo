const DIAL_KEYS = [
  { digit: '1', letters: '' },
  { digit: '2', letters: 'ABC' },
  { digit: '3', letters: 'DEF' },
  { digit: '4', letters: 'GHI' },
  { digit: '5', letters: 'JKL' },
  { digit: '6', letters: 'MNO' },
  { digit: '7', letters: 'PQRS' },
  { digit: '8', letters: 'TUV' },
  { digit: '9', letters: 'WXYZ' },
  { digit: '*', letters: '' },
  { digit: '0', letters: '+' },
  { digit: '#', letters: '' }
];

interface DialPadProps {
  onDigit: (digit: string) => void;
  disabled?: boolean;
}

export function DialPad({ onDigit, disabled = false }: DialPadProps) {
  return (
    <div className="grid grid-cols-3 gap-2">
      {DIAL_KEYS.map((key) => (
        <button
          key={key.digit}
          type="button"
          disabled={disabled}
          className="group flex h-14 flex-col items-center justify-center rounded-2xl border border-border bg-white text-ink shadow-sm transition-all duration-150 hover:-translate-y-0.5 hover:border-primary-300 hover:bg-surface-2 disabled:cursor-not-allowed disabled:opacity-55"
          onClick={() => onDigit(key.digit)}
          aria-label={`Dial ${key.digit}`}
        >
          <span className="font-display text-lg font-semibold">{key.digit}</span>
          <span className="text-[10px] font-semibold tracking-wide text-muted/75">{key.letters || '\u00A0'}</span>
        </button>
      ))}
    </div>
  );
}
