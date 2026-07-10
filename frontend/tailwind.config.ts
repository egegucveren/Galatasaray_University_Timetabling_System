import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./src/**/*.{ts,tsx}",
    "./components/**/*.{ts,tsx}",
    "./lib/**/*.{ts,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        primary: {
          DEFAULT: "#e8185c",
          dark:    "#c4114c",
          light:   "#ff3d7f",
        },
        gsu: {
          red:  "#cc1e1e",
          gold: "#e6a817",
          dark: "#1a1a1a",
        },
        surface:   "#ffffff",
        "bg-page": "#f5f6f8",
        ink:   "#1a1a1a",
        muted: "#666666",
        line:  "#e4e4e4",
      },
      fontFamily: {
        sans: ["Inter", "Segoe UI", "system-ui", "sans-serif"],
      },
      boxShadow: {
        card:       "0 4px 24px rgba(0,0,0,.08)",
        "card-hover": "0 8px 32px rgba(0,0,0,.12)",
      },
    },
  },
  plugins: [],
};

export default config;
