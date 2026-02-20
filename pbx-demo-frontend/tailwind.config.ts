import type { Config } from 'tailwindcss';

export default {
  content: ['./src/**/*.{js,jsx,ts,tsx}'],
  theme: {
    extend: {
      colors: {
        bg: '#f2f6fb',
        surface: '#ffffff',
        'surface-2': '#f7fafe',
        border: '#d8e4f0',
        ink: '#10263a',
        muted: '#567086',
        'muted-strong': '#3d5970',
        primary: {
          50: '#eff8ff',
          100: '#d8eeff',
          200: '#b3dcff',
          300: '#7fc4ff',
          400: '#46a7fb',
          500: '#1f86ee',
          600: '#1169d1',
          700: '#0f53a9'
        },
        secondary: {
          100: '#dff6f7',
          200: '#b5e9eb',
          300: '#7fd8dc',
          400: '#40c0c8',
          500: '#1ca4b0',
          600: '#188592'
        },
        accent: {
          400: '#f9b151',
          500: '#f0932f'
        },
        success: {
          100: '#dcfce9',
          500: '#159c69',
          700: '#0f7d53'
        },
        warning: {
          100: '#fff5dc',
          500: '#d99525',
          700: '#a16408'
        },
        danger: {
          100: '#feeaea',
          500: '#df4c4c',
          700: '#b32828'
        },
        info: {
          100: '#e5f2ff',
          500: '#3285ca',
          700: '#1e639d'
        }
      },
      boxShadow: {
        soft: '0 8px 24px rgba(16, 38, 58, 0.08)',
        card: '0 20px 48px -26px rgba(15, 43, 69, 0.26)',
        focus: '0 0 0 4px rgba(31, 134, 238, 0.18)'
      },
      borderRadius: {
        xl2: '1.25rem'
      },
      fontFamily: {
        sans: ['"Plus Jakarta Sans"', '"Segoe UI"', 'sans-serif'],
        display: ['"Outfit"', '"Plus Jakarta Sans"', 'sans-serif']
      },
      keyframes: {
        'panel-in': {
          '0%': { opacity: '0', transform: 'translateY(8px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' }
        },
        'toast-in': {
          '0%': { opacity: '0', transform: 'translateY(12px) scale(0.98)' },
          '100%': { opacity: '1', transform: 'translateY(0) scale(1)' }
        }
      },
      animation: {
        'panel-in': 'panel-in 260ms ease-out',
        'toast-in': 'toast-in 220ms ease-out'
      }
    }
  },
  plugins: []
} satisfies Config;
