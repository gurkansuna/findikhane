import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Docker imajını küçük tutmak için gerekli tüm dosyaları .next/standalone altında toplar.
  output: "standalone"
};

export default nextConfig;
