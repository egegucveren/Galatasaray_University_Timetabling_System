/** @type {import('next').NextConfig} */
const nextConfig = {
  // Outputs static HTML/CSS/JS into frontend/out/
  output: "export",
  trailingSlash: true,

  // In dev mode, proxy /api/* to the C# backend
  ...(process.env.NODE_ENV !== "production" && {
    async rewrites() {
      return [
        {
          source: "/api/:path*",
          destination: "http://127.0.0.1:5038/api/:path*",
        },
      ];
    },
  }),
};

export default nextConfig;
