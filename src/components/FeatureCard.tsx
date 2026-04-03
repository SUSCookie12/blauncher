"use client";

import { motion } from "framer-motion";
import { LucideIcon } from "lucide-react";

interface FeatureCardProps {
  title: string;
  description: string;
  icon: LucideIcon;
  delay?: number;
}

export default function FeatureCard({ title, description, icon: Icon, delay = 0 }: FeatureCardProps) {
  return (
    <motion.div
      initial={{ y: 20, opacity: 0 }}
      whileInView={{ y: 0, opacity: 1 }}
      transition={{ delay, duration: 0.4 }}
      viewport={{ once: true }}
      className="p-8 modrinth-card h-full flex flex-col items-start gap-4"
    >
      <div className="w-12 h-12 rounded bg-white/5 flex items-center justify-center text-brand-green">
        <Icon size={24} />
      </div>
      <h3 className="text-xl font-black tracking-tight uppercase leading-none">{title}</h3>
      <p className="text-[#F5F5F5]/40 leading-snug font-medium text-sm">{description}</p>
    </motion.div>
  );
}
