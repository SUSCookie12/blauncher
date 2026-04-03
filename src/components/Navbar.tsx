"use client";

import { motion } from "framer-motion";
import { Download } from "lucide-react";
import Link from "next/link";
import Image from "next/image";
import icon from "@/assets/icon.png";

import { useEffect, useState } from "react";
import { getLauncherMetadata, LauncherMetadata } from "@/lib/metadata";

export default function Navbar() {
  const [metadata, setMetadata] = useState<LauncherMetadata | null>(null);

  useEffect(() => {
    getLauncherMetadata().then(data => {
      if (data) setMetadata(data);
    });
  }, []);

  return (
    <nav className="fixed top-0 left-0 right-0 z-50 flex items-center justify-between px-6 py-3 mx-auto bg-bg-dark border-b border-border-main">
      <div className="flex items-center gap-6 max-w-7xl w-full mx-auto justify-between">
        <div className="flex items-center gap-8">
          <Link href="/" className="flex items-center gap-3 group">
            <div className="relative w-8 h-8 group-hover:scale-105 transition-transform">
              <Image src={icon} alt="BIcon" fill className="object-contain" />
            </div>
            <span className="text-xl font-black tracking-tight uppercase">Launcher</span>
          </Link>

          <div className="hidden md:flex items-center gap-6 text-[11px] font-black uppercase tracking-widest text-[#F5F5F5]/40 mt-1">
            <Link href="#features" className="hover:text-brand-green transition-colors">Features</Link>
            <Link href="#specs" className="hover:text-white transition-colors">Specs</Link>
          </div>
        </div>

        <motion.a
          href={metadata?.downloadUrl || "#"}
          whileHover={{ scale: 1.02 }}
          whileTap={{ scale: 0.98 }}
          className="px-5 py-2.5 btn-primary rounded text-[11px] font-black uppercase tracking-widest flex items-center gap-2"
        >
          <Download size={16} strokeWidth={3} /> GET BLAUNCHER
        </motion.a>
      </div>
    </nav>
  );
}
