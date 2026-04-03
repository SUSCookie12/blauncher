import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { Analytics } from "@vercel/analytics/next";
import "./globals.css";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "BLauncher | The High-Performance Minecraft Portal",
  description: "Download BLauncher, the ultimate Minecraft launcher featuring multi-instance management, CSPackage Auth, and optimized launch performance. Built by CSPackage.",
  keywords: ["Minecraft Launcher", "BLauncher", "BorgovLauncher", "CSPackage", "Minecraft Modded", "Auth"],
  authors: [{ name: "CSPackage" }],
  openGraph: {
    title: "BLauncher - The High-Performance Minecraft Portal",
    description: "Multi-instance support, CSPackage Auth, and lightning-fast launch speeds.",
    type: "website",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${inter.variable} antialiased scroll-smooth`}
    >
      <body suppressHydrationWarning className="min-h-screen bg-bg-dark text-[#F5F5F5] selection:bg-brand-green/30">
        {children}
        <Analytics />
      </body>
    </html>
  );
}
