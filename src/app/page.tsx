"use client";

import { motion } from "framer-motion";
import Image from "next/image";
import logo from "@/assets/logo.png";
import { 
  Rocket, 
  Shield, 
  ShieldCheck,
  Layers, 
  Cpu, 
  Server, 
  Cloud, 
  Download, 
  CheckCircle2, 
  Zap, 
  Monitor, 
  ChevronRight
} from "lucide-react";
import Navbar from "@/components/Navbar";
import Hero from "@/components/Hero";
import FeatureCard from "@/components/FeatureCard";
import { useEffect, useState } from "react";
import { getLauncherMetadata, LauncherMetadata } from "@/lib/metadata";

export default function Home() {
  const [metadata, setMetadata] = useState<LauncherMetadata | null>(null);

  useEffect(() => {
    getLauncherMetadata().then(data => {
      if (data) setMetadata(data);
    });
  }, []);

  return (
    <main className="min-h-screen bg-bg-dark text-[#F5F5F5] selection:bg-brand-green/30">
      <Navbar />
      <Hero />

      {/* Features Grid */}
      <section id="features" className="py-24 px-6 border-b border-border-main">
        <div className="container mx-auto max-w-7xl">
          <div className="flex items-center gap-4 mb-2">
            <div className="h-1 w-12 bg-brand-green"></div>
            <span className="text-[11px] font-black tracking-widest uppercase text-[#F5F5F5]/30">Key Features</span>
          </div>
          <h2 className="text-4xl font-black mb-16 tracking-tighter uppercase leading-none">
            Built for <span className="text-brand-blue">Performance</span>
          </h2>

          <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-6">
            <FeatureCard 
              icon={Layers}
              title="Profile Isolation" 
              description="Keep your mods and configs separate. Isolated instances for Vanilla, Fabric, Forge, and NeoForge ensure zero conflicts."
              delay={0.1}
            />
            <FeatureCard 
              icon={Rocket}
              title="Parallel Execution" 
              description="The LaunchService manages a batch of 30 concurrent tasks to download libraries and versions at lightning speed."
              delay={0.2}
            />
            <FeatureCard 
              icon={Shield}
              title="Firebase Core" 
              description="Unified authentication with the CSPackage ecosystem. Secure persistent sessions with profile validation."
              delay={0.3}
            />
          </div>
        </div>
      </section>

      {/* Specs Section */}
      <section id="specs" className="py-24 px-6">
        <div className="container mx-auto max-w-5xl">
          <div className="bg-[#1d1d1d] border border-border-main p-10 md:p-16 rounded-xl">
            <div className="grid md:grid-cols-2 gap-20">
              <div>
                <h3 className="text-2xl font-black mb-10 flex items-center gap-3 uppercase tracking-tight"><Monitor size={20} className="text-brand-blue" /> Architecture</h3>
                <div className="space-y-6 text-sm font-bold tracking-tight">
                  <div className="flex justify-between border-b border-border-main pb-4">
                    <span className="text-[#F5F5F5]/30 uppercase text-[10px] tracking-widest">Framework</span>
                    <span>.NET 10.0 runtime</span>
                  </div>
                  <div className="flex justify-between border-b border-border-main pb-4">
                    <span className="text-[#F5F5F5]/30 uppercase text-[10px] tracking-widest">UI Framework</span>
                    <span className="text-brand-green">WinUI 3 (Widnows App SDK)</span>
                  </div>
                  <div className="flex justify-between border-b border-border-main pb-4">
                    <span className="text-[#F5F5F5]/30 uppercase text-[10px] tracking-widest">Persistence</span>
                    <span>Firebase Auth</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-[#F5F5F5]/30 uppercase text-[10px] tracking-widest">Archs</span>
                    <span>x86, x64, ARM64</span>
                  </div>
                </div>
              </div>
              <div className="relative">
                <h3 className="text-2xl font-black mb-10 flex items-center gap-3 uppercase tracking-tight"><ShieldCheck size={20} className="text-brand-green" /> CSPackage Auth</h3>
                <p className="text-[#F5F5F5]/40 text-sm mb-4 leading-snug font-medium">
                  BLauncher utilizes the unified CSPackage authentication system. 
                  Sign in with a single click and have your Minecraft account ready for deployment instantly.
                </p>
                <div className="pt-6 mt-6 border-t border-border-main flex items-center gap-6">
                  <div className="flex flex-col">
                    <span className="text-brand-green font-black leading-none text-2xl">30+</span>
                    <span className="text-[9px] uppercase tracking-tighter text-white/30">Parallel Tasks</span>
                  </div>
                  <div className="flex flex-col">
                    <span className="text-brand-blue font-black leading-none text-2xl">-Xmx4G</span>
                    <span className="text-[9px] uppercase tracking-tighter text-white/30">Memory Optimized</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Simplified Footer */}
      <footer className="py-20 border-t border-border-main bg-[#0B0B0B]">
        <div className="container mx-auto px-6 max-w-7xl flex flex-col md:flex-row justify-between items-center gap-12">
          <div className="relative w-48 h-12 grayscale opacity-50 hover:grayscale-0 hover:opacity-100 transition-all">
            <Image src={logo} alt="BLauncher Logo" fill className="object-contain object-left" />
          </div>

          <div className="flex gap-12 text-[11px] font-black uppercase tracking-widest text-[#F5F5F5]/30">
            <Link href="#" className="hover:text-brand-green transition-colors">Home</Link>
            <Link href="https://cspack.online/apps/blauncher" className="hover:text-brand-green transition-colors">Launcher Home</Link>
            <span className="text-white/10 font-medium tracking-tight">BLauncher {metadata?.version || "v26.0.4"} - CSPackage</span>
          </div>
        </div>
      </footer>
    </main>
  );
}

function Link({ href, children, className }: { href: string; children: React.ReactNode; className?: string }) {
  return (
    <a href={href} className={className}>
      {children}
    </a>
  );
}
