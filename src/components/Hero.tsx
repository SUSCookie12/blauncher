"use client";

import { motion } from "framer-motion";
import Image from "next/image";
import { Download, Rocket, ShieldCheck, Zap } from "lucide-react";
import mockup from "@/assets/mockup.png";
import { useEffect, useRef, useState } from "react";
import { getLauncherMetadata, LauncherMetadata } from "@/lib/metadata";

export default function Hero() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [isVisible, setIsVisible] = useState(true);
  const [metadata, setMetadata] = useState<LauncherMetadata | null>(null);

  useEffect(() => {
    // Fetch live metadata from Firebase
    getLauncherMetadata().then(data => {
      if (data) setMetadata(data);
    });

    const video = videoRef.current;
    if (!video) return;

    const handleEnded = () => {
      setIsVisible(false); // Fade out
      
      setTimeout(() => {
        video.currentTime = 0;
        video.play();
        setIsVisible(true); // Fade back in once reset
      }, 800); // Wait for fade out to complete
    };

    video.addEventListener("ended", handleEnded);
    return () => {
      video.removeEventListener("ended", handleEnded);
    };
  }, []);

  return (
    <section className="relative min-h-[70vh] flex items-center justify-center pt-32 pb-20 border-b border-border-main bg-bg-dark">
      <div className="container px-6 mx-auto grid lg:grid-cols-2 gap-16 items-center max-w-7xl">
        <motion.div
          initial={{ y: 20, opacity: 0 }}
          animate={{ y: 0, opacity: 1 }}
          transition={{ duration: 0.5 }}
          className="text-left"
        >
          <div className="inline-flex items-center gap-2 px-2.5 py-1 mb-6 rounded bg-[#1d1d1d] border border-border-main text-[11px] font-bold tracking-widest uppercase text-brand-green">
            <Rocket size={12} /> BLauncher {metadata?.version || "v26.0.4"}
          </div>
          
          <h1 className="text-5xl lg:text-9xl font-black mb-6 tracking-tighter leading-none">
            <span className="text-brand-green">B</span>Launcher
          </h1>
          
          <div className="inline-flex items-center px-4 py-2 mb-10 rounded-full bg-brand-green/10 border border-brand-green/20">
            <span className="text-sm font-black uppercase tracking-[0.2em] text-brand-green">
              By Players For Players
            </span>
          </div>

          <div className="flex flex-col sm:flex-row items-center gap-4">
            <a 
              href={metadata?.downloadUrl || "#"} 
              className="w-full sm:w-auto px-10 py-5 btn-primary rounded-lg text-xl font-black flex items-center justify-center gap-4 shadow-2xl shadow-brand-green/20 hover:brightness-110 active:scale-[0.98] transition-all"
            >
              <Download size={24} strokeWidth={3} /> GET BLAUNCHER
            </a>
          </div>

          <div className="mt-12 flex flex-wrap items-center gap-8 text-[11px] font-bold tracking-widest uppercase text-[#F5F5F5]/30">
            <div className="flex items-center gap-2 text-brand-green"><Zap size={14} /> 30x Faster Downloads</div>
            <div className="flex items-center gap-2 text-brand-blue"><ShieldCheck size={14} /> CSPackage Verified</div>
          </div>
        </motion.div>

        <motion.div
          initial={{ x: 20, opacity: 0, rotate: 0, scale: 0.9 }}
          animate={{ x: 0, opacity: 1, rotate: 8, scale: 1.1 }}
          transition={{ duration: 0.9, ease: [0.16, 1, 0.3, 1] }}
          className="relative max-w-5xl mx-auto lg:translate-x-24"
        >
          {/* RGB LED Shadow Light */}
          <motion.div
            animate={{
              rotate: [0, 360],
            }}
            transition={{
              duration: 5,
              repeat: Infinity,
              ease: "linear"
            }}
            className="absolute -inset-6 z-[-1] opacity-30 blur-[80px] rounded-2xl overflow-hidden"
            style={{
              background: "conic-gradient(from 0deg, #1BD964, #3D85C6, #1BD964)"
            }}
          />

          <motion.div 
            animate={{ opacity: isVisible ? 1 : 0 }}
            transition={{ duration: 0.5 }}
            className="relative p-2 bg-[#1d1d1d] rounded-xl border border-border-main shadow-[0_50px_100px_-20px_rgba(0,0,0,1)] overflow-hidden"
          >
            <video 
              ref={videoRef}
              src="https://i.imgur.com/YUcssE8.mp4"
              autoPlay 
              muted 
              playsInline
              className="w-full h-auto rounded-lg"
            />
          </motion.div>
        </motion.div>
      </div>
    </section>
  );
}
