const DIGITS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '*', '0', '#'];

interface DialPadProps {
  onDigit: (digit: string) => void;
}

export function DialPad({ onDigit }: DialPadProps) {
  return (
    <div className="dialpad-grid">
      {DIGITS.map((digit) => (
        <button key={digit} className="dialpad-button" type="button" onClick={() => onDigit(digit)}>
          {digit}
        </button>
      ))}
    </div>
  );
}
