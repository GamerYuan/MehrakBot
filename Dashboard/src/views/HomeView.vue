<script setup>
import { useRouter } from "vue-router";
import Button from "primevue/button";
import AppNavbar from "../components/AppNavbar.vue";

const router = useRouter();

const features = [
  {
    title: "Common Utilities",
    icon: "pi-cog",
    gradient: "gradient-violet",
    description: "Multi-profile support, HoYoLAB Daily Check In",
  },
  {
    title: "Genshin Impact",
    iconImg: "/genshin.webp",
    gradient: "gradient-amber",
    description:
      "Character build card, Abyss summary card, Code redemption, and more.",
  },
  {
    title: "Honkai: Star Rail",
    iconImg: "/hsr.webp",
    gradient: "gradient-blue",
    description:
      "Character build card, Memory of Chaos summary card, Code redemption, and more.",
  },
  {
    title: "Zenless Zone Zero",
    iconImg: "/zzz.webp",
    gradient: "gradient-pink",
    description:
      "Character build card, Shiyu Defense summary card, Code redemption, and more.",
  },
  {
    title: "Honkai Impact 3rd",
    iconImg: "/hi3.webp",
    gradient: "gradient-emerald",
    description: "Character build card, and more coming soon.",
  },
];
</script>

<template>
  <div class="landing">
    <AppNavbar />

    <section class="hero">
      <div class="hero-bg">
        <div class="gradient-orb orb-1"></div>
        <div class="gradient-orb orb-2"></div>
        <div class="gradient-orb orb-3"></div>
        <div class="grid-overlay"></div>
      </div>

      <div class="hero-content">
        <div class="badge">
          <span class="badge-dot"></span>
          Discord Bot
        </div>

        <h1 class="hero-title">
          <span class="title-line">Mehrak<span class="accent">Bot</span></span>
        </h1>

        <p class="hero-subtitle">
          Your All-in-One <span class="highlight">HoYoverse</span> Games Toolbox
        </p>

        <p class="hero-description">
          Manage your game accounts, generate build cards, redeem codes, and
          track your progress across all HoYoverse titles.
        </p>

        <div class="hero-actions">
          <Button
            as="a"
            label="Add to Discord"
            icon="pi pi-discord"
            href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
            target="_blank"
            rel="noopener noreferrer"
            size="large"
            class="discord-btn"
          />
          <Button
            label="Read Documentation"
            icon="pi pi-book"
            severity="secondary"
            outlined
            size="large"
            @click="router.push('/docs')"
          />
        </div>

        <div class="hero-stats">
          <div class="stat">
            <span class="stat-value">5+</span>
            <span class="stat-label">Games Supported</span>
          </div>
          <div class="stat-divider"></div>
          <div class="stat">
            <span class="stat-value">Free</span>
            <span class="stat-label">Open Source</span>
          </div>
        </div>
      </div>
    </section>

    <section class="features-section">
      <div class="section-header">
        <h2>Features</h2>
        <p>Everything you need for your HoYoverse gaming experience</p>
      </div>

      <div class="features-grid">
        <div
          v-for="feature in features"
          :key="feature.title"
          class="feature-card"
        >
          <div :class="['feature-icon', { [feature.gradient]: !feature.iconImg }]">
            <img v-if="feature.iconImg" :src="feature.iconImg" :alt="feature.title" />
            <i v-else :class="['pi', feature.icon]"></i>
          </div>
          <h3>{{ feature.title }}</h3>
          <p>{{ feature.description }}</p>
        </div>
      </div>
    </section>

    <section class="cta-section">
      <div class="cta-content">
        <h2>Ready to get started?</h2>
        <p>Add MehrakBot to your server in seconds</p>
        <Button
          as="a"
          label="Add to Discord"
          icon="pi pi-discord"
          href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
          target="_blank"
          rel="noopener noreferrer"
          size="large"
        />
      </div>
    </section>

    <footer class="footer">
      <div class="footer-content">
        <div class="footer-brand">
          <img src="/logo.webp" alt="MehrakBot" class="logo-icon" />
          <span>MehrakBot</span>
        </div>
        <p class="footer-disclaimer">
          Not affiliated with HoYoverse. All game titles and trademarks belong
          to their respective owners.
        </p>
        <p class="footer-copyright">&copy; 2026 MehrakBot</p>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.landing {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: linear-gradient(to bottom, #0a0a0f, #111118);
  overflow-x: hidden;
}

.logo-icon {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  object-fit: contain;
}

.hero {
  position: relative;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 8rem 2rem 4rem;
}

.hero-bg {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
}

.gradient-orb {
  position: absolute;
  border-radius: 50%;
  filter: blur(100px);
  opacity: 0.4;
}

.orb-1 {
  width: 600px;
  height: 600px;
  background: linear-gradient(135deg, #5865f2, #7289da);
  top: -200px;
  right: -100px;
  animation: float 20s ease-in-out infinite;
}

.orb-2 {
  width: 500px;
  height: 500px;
  background: linear-gradient(135deg, #ffc107, #ff9800);
  bottom: -150px;
  left: -100px;
  animation: float 25s ease-in-out infinite reverse;
}

.orb-3 {
  width: 400px;
  height: 400px;
  background: linear-gradient(135deg, #e91e63, #9c27b0);
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  animation: float 15s ease-in-out infinite;
  opacity: 0.2;
}

.grid-overlay {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.02) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.02) 1px, transparent 1px);
  background-size: 60px 60px;
  mask-image: radial-gradient(ellipse at center, black 20%, transparent 70%);
}

@keyframes float {
  0%,
  100% {
    transform: translate(0, 0) scale(1);
  }
  33% {
    transform: translate(30px, -30px) scale(1.05);
  }
  66% {
    transform: translate(-20px, 20px) scale(0.95);
  }
}

.hero-content {
  position: relative;
  text-align: center;
  max-width: 800px;
  z-index: 1;
}

.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 1rem;
  background: rgba(88, 101, 242, 0.15);
  border: 1px solid rgba(88, 101, 242, 0.3);
  border-radius: 100px;
  font-size: 0.85rem;
  color: #a0a0ff;
  margin-bottom: 1.5rem;
}

.badge-dot {
  width: 6px;
  height: 6px;
  background: #5865f2;
  border-radius: 50%;
  animation: pulse 2s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.4;
  }
}

.hero-title {
  margin: 0 0 1rem;
  font-size: clamp(3rem, 10vw, 6rem);
  font-weight: 800;
  letter-spacing: -0.02em;
  line-height: 1.1;
}

.title-line {
  display: block;
}

.accent {
  background: linear-gradient(135deg, #5865f2, #a855f7);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-subtitle {
  font-size: clamp(1.2rem, 3vw, 1.8rem);
  color: #e0e0e0;
  margin: 0 0 1rem;
  font-weight: 500;
}

.highlight {
  color: #ffc107;
}

.hero-description {
  font-size: 1.1rem;
  color: #808080;
  max-width: 600px;
  margin: 0 auto 2.5rem;
  line-height: 1.7;
}

.hero-actions {
  display: flex;
  justify-content: center;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 3rem;
}

.hero-actions :deep(.p-button) {
  padding: 0.9rem 2rem;
}

.discord-btn :deep(.p-button-label) {
  font-weight: 600;
}

.hero-stats {
  display: inline-flex;
  align-items: center;
  gap: 2rem;
  padding: 1rem 2rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 12px;
}

.stat {
  text-align: center;
}

.stat-value {
  display: block;
  font-size: 1.5rem;
  font-weight: 700;
  color: #fff;
}

.stat-label {
  font-size: 0.8rem;
  color: #666;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stat-divider {
  width: 1px;
  height: 40px;
  background: rgba(255, 255, 255, 0.1);
}

.features-section {
  padding: 6rem 2rem;
  background: linear-gradient(
    to bottom,
    transparent,
    rgba(255, 255, 255, 0.02),
    transparent
  );
}

.section-header {
  text-align: center;
  margin-bottom: 4rem;
}

.section-header h2 {
  font-size: clamp(2rem, 5vw, 3rem);
  font-weight: 700;
  margin: 0 0 0.5rem;
  background: linear-gradient(135deg, #fff, #a0a0a0);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.section-header p {
  color: #666;
  font-size: 1.1rem;
  margin: 0;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 1.5rem;
  max-width: 1200px;
  margin: 0 auto;
}

.feature-card {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 2rem;
  transition: all 0.3s ease;
  text-align: left;
}

.feature-card:hover {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.15);
  transform: translateY(-4px);
}

.feature-icon {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 1.25rem;
  font-size: 1.3rem;
  color: white;
}

.feature-icon i {
  color: white;
}

.feature-icon img {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  object-fit: cover;
}

.gradient-violet {
  background: linear-gradient(135deg, #8b5cf6, #9333ea);
}

.gradient-amber {
  background: linear-gradient(135deg, #f59e0b, #ea580c);
}

.gradient-blue {
  background: linear-gradient(135deg, #3b82f6, #06b6d4);
}

.gradient-pink {
  background: linear-gradient(135deg, #ec4899, #e11d48);
}

.gradient-emerald {
  background: linear-gradient(135deg, #10b981, #0d9488);
}

.feature-card h3 {
  font-size: 1.2rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
  color: #fff;
}

.feature-card p {
  font-size: 0.95rem;
  color: #707070;
  margin: 0;
  line-height: 1.6;
}

.cta-section {
  padding: 6rem 2rem;
  text-align: center;
}

.cta-content {
  max-width: 500px;
  margin: 0 auto;
  padding: 3rem;
  background: linear-gradient(
    135deg,
    rgba(88, 101, 242, 0.1),
    rgba(168, 85, 247, 0.1)
  );
  border: 1px solid rgba(88, 101, 242, 0.2);
  border-radius: 24px;
}

.cta-content h2 {
  font-size: 2rem;
  font-weight: 700;
  margin: 0 0 0.5rem;
}

.cta-content p {
  color: #888;
  margin: 0 0 2rem;
}

.footer {
  padding: 3rem 2rem;
  border-top: 1px solid rgba(255, 255, 255, 0.05);
  background: rgba(0, 0, 0, 0.3);
}

.footer-content {
  max-width: 600px;
  margin: 0 auto;
  text-align: center;
}

.footer-brand {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.1rem;
  margin-bottom: 1rem;
}

.footer-disclaimer {
  color: #555;
  font-size: 0.85rem;
  margin: 0 0 0.5rem;
}

.footer-copyright {
  color: #444;
  font-size: 0.8rem;
  margin: 0;
}

@media (max-width: 640px) {
  .hero-stats {
    flex-direction: column;
    gap: 1rem;
    padding: 1.5rem;
  }

  .stat-divider {
    width: 60px;
    height: 1px;
  }

  .hero-actions {
    flex-direction: column;
    align-items: center;
  }

  .hero-actions :deep(.p-button) {
    width: 100%;
    max-width: 280px;
    justify-content: center;
  }
}
</style>
