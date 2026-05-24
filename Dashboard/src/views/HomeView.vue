<script setup>
import { useRouter } from "vue-router";
import Button from "primevue/button";
import AppNavbar from "../components/AppNavbar.vue";
import AppFooter from "../components/AppFooter.vue";
import ShowcaseSection from "../components/ShowcaseSection.vue";
import HeroShowcase from "../components/HeroShowcase.vue";

const router = useRouter();

const heroImages = [
  "/showcase/builds-1.webp",
  "/showcase/endgame-3.webp",
  "/showcase/endgame-2.webp",
];

const showcases = [
  {
    title: "Character Builds",
    description:
      "Generate detailed character build cards with artifacts, weapons, talents, and stats across all supported games. Share and compare builds with your community instantly.",
    images: [
      "/showcase/builds-1.webp",
      "/showcase/builds-2.webp",
      "/showcase/builds-3.webp",
      "/showcase/builds-4.webp",
    ],
    reversed: false,
  },
  {
    title: "Character List Summary",
    description:
      "Get a comprehensive overview of your entire roster. View character levels, constellations, and key equipment at a glance.",
    images: ["/showcase/list-1.webp", "/showcase/list-2.webp"],
    reversed: true,
  },
  {
    title: "Endgame",
    description:
      "Track all your Endgame clear records. Analyze floor clears, star counts, and team compositions to optimize your runs.",
    images: [
      "/showcase/endgame-1.webp",
      "/showcase/endgame-2.webp",
      "/showcase/endgame-3.webp",
      "/showcase/endgame-4.webp",
    ],
    reversed: false,
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
        <div class="hero-left">
          <h1 class="hero-title">
            <span class="title-line">Your Ultimate</span>
            <span class="title-line accent">HoYoverse</span>
            <span class="title-line">Companion</span>
          </h1>

          <p class="hero-description">
            Seamlessly integrate Genshin Impact, Honkai: Star Rail, and Zenless
            Zone Zero data directly into your Discord server. View builds,
            endgame clear records, and more.
          </p>

          <div class="game-tags">
            <span class="tag">
              <img src="/genshin.webp" alt="Genshin" class="tag-icon" />
              Genshin
            </span>
            <span class="tag-separator">•</span>
            <span class="tag">
              <img src="/hsr.webp" alt="HSR" class="tag-icon" />
              HSR
            </span>
            <span class="tag-separator">•</span>
            <span class="tag">
              <img src="/zzz.webp" alt="ZZZ" class="tag-icon" />
              ZZZ
            </span>
            <span class="tag-separator">•</span>
            <span class="tag">
              <img src="/hi3.webp" alt="HI3" class="tag-icon" />
              HI3
            </span>
          </div>

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
              label="View Docs"
              icon="pi pi-book"
              severity="secondary"
              outlined
              size="large"
              @click="router.push('/docs')"
              class="docs-btn"
            />
          </div>
        </div>

        <div class="hero-right">
          <HeroShowcase :images="heroImages" />
        </div>
      </div>
    </section>

    <section id="features" class="features-section">
      <div class="section-header">
        <h2 class="section-title">Features</h2>
        <p class="section-subtitle">
          Everything you need for your HoYoverse gaming experience
        </p>
      </div>
    </section>

    <div class="showcases-wrapper">
      <ShowcaseSection
        v-for="showcase in showcases"
        :key="showcase.title"
        v-bind="showcase"
      />
    </div>

    <section class="cta-section">
      <div class="cta-content">
        <h2 class="cta-title">Ready to get started?</h2>
        <p class="cta-subtitle">Add MehrakBot to your server in seconds</p>
        <Button
          as="a"
          label="Add to Discord"
          icon="pi pi-discord"
          href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
          target="_blank"
          rel="noopener noreferrer"
          size="large"
          class="cta-btn"
        />
      </div>
    </section>

    <AppFooter />
  </div>
</template>

<style scoped>
.landing {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: linear-gradient(to bottom, #08080c, #0e0e14);
  overflow-x: hidden;
}

/* ── Hero ── */
.hero {
  position: relative;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 8rem 2.5rem 4rem;
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
  opacity: 0.35;
}

.orb-1 {
  width: 600px;
  height: 600px;
  background: linear-gradient(135deg, var(--accent), var(--accent-strong));
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
  left: 30%;
  transform: translate(-50%, -50%);
  animation: float 15s ease-in-out infinite;
  opacity: 0.15;
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
  z-index: 1;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 4rem;
  align-items: center;
  max-width: 1200px;
  width: 100%;
}

/* ── Hero Left ── */
.hero-left {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.hero-title {
  margin: 0;
  line-height: 1.05;
  font-size: clamp(2.8rem, 5vw, 4.2rem);
  font-weight: 800;
  color: #f0f0f5;
  letter-spacing: -0.02em;
}

.title-line {
  display: block;
}

.accent {
  color: var(--accent);
}

.hero-description {
  font-size: 1.05rem;
  color: #888;
  max-width: 480px;
  margin: 0;
  line-height: 1.7;
}

.game-tags {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.tag {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  color: #a0a0a0;
  font-size: 0.85rem;
  font-weight: 500;
}

.tag-icon {
  width: 18px;
  height: 18px;
  border-radius: 4px;
  object-fit: cover;
}

.tag-separator {
  color: #444;
  font-size: 0.7rem;
}

.hero-actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
  margin-top: 0.5rem;
}

:deep(.p-button.discord-btn) {
  background: var(--accent);
  border-color: var(--accent);
  color: #000;
  font-weight: 600;
  padding: 0.9rem 2rem;
}

:deep(.p-button.discord-btn:hover) {
  background: var(--accent-strong);
  border-color: var(--accent-strong);
}

:deep(.p-button.discord-btn .p-button-icon) {
  color: #000;
}

:deep(.p-button.docs-btn) {
  border-color: rgba(255, 255, 255, 0.15);
  color: #e0e0e0;
  font-weight: 500;
  padding: 0.9rem 2rem;
}

:deep(.p-button.docs-btn:hover) {
  background: rgba(255, 255, 255, 0.05);
  border-color: rgba(255, 255, 255, 0.25);
}

/* ── Hero Right / Showcase ── */
.hero-right {
  display: flex;
  align-items: center;
  justify-content: center;
}

/* ── Command Carousel ── */
.carousel-section {
  padding: 5rem 0 4rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2rem;
  overflow-x: hidden;
}

.carousel-header {
  text-align: center;
  padding: 0 1.5rem;
}

.carousel-title {
  font-size: 1.8rem;
  font-weight: 700;
  color: #f0f0f5;
  margin: 0 0 0.5rem;
  letter-spacing: -0.01em;
}

.carousel-subtitle {
  color: #666;
  font-size: 1rem;
  margin: 0;
}

.carousel-actions {
  display: flex;
  justify-content: center;
}

:deep(.p-button.carousel-docs-btn) {
  border-color: rgba(255, 255, 255, 0.15);
  color: #e0e0e0;
  font-weight: 500;
  padding: 0.85rem 2rem;
}

:deep(.p-button.carousel-docs-btn:hover) {
  background: rgba(255, 255, 255, 0.05);
  border-color: rgba(255, 255, 255, 0.25);
}

/* ── Features ── */
.features-section {
  padding: 6rem 2.5rem 0 2.5rem;
  background: linear-gradient(
    to bottom,
    transparent,
    rgba(255, 255, 255, 0.015),
    transparent
  );
}

.section-header {
  text-align: center;
  margin-bottom: 4rem;
}

.section-title {
  font-size: 2.5rem;
  font-weight: 700;
  color: #f0f0f5;
  margin: 0 0 0.5rem;
  letter-spacing: -0.01em;
}

.section-subtitle {
  color: #666;
  font-size: 1.05rem;
  margin: 0;
}

/* ── CTA ── */
.cta-section {
  padding: 6rem 2.5rem;
  text-align: center;
}

.cta-content {
  max-width: 520px;
  margin: 0 auto;
  padding: 3rem;
  background: linear-gradient(
    135deg,
    rgba(var(--accent-rgb), 0.1),
    rgba(var(--accent-rgb), 0.03)
  );
  border: 1px solid rgba(var(--accent-rgb), 0.2);
  border-radius: 20px;
}

.cta-title {
  font-size: 1.9rem;
  font-weight: 700;
  color: #f0f0f5;
  margin: 0 0 0.5rem;
}

.cta-subtitle {
  color: #888;
  margin: 0 0 2rem;
  font-size: 1rem;
}

:deep(.p-button.cta-btn) {
  background: var(--accent);
  border-color: var(--accent);
  color: #000;
  font-weight: 600;
  padding: 0.9rem 2rem;
}

:deep(.p-button.cta-btn:hover) {
  background: var(--accent-strong);
  border-color: var(--accent-strong);
}

:deep(.p-button.cta-btn .p-button-icon) {
  color: #000;
}

/* ── Showcases ── */
.showcases-wrapper {
  background: linear-gradient(
    to bottom,
    transparent,
    rgba(255, 255, 255, 0.01) 10%,
    rgba(255, 255, 255, 0.01) 90%,
    transparent
  );
}

/* ── Responsive ── */
@media (max-width: 960px) {
  .hero-content {
    grid-template-columns: 1fr;
    gap: 3rem;
    text-align: center;
  }

  .hero-left {
    align-items: center;
  }

  .hero-description {
    max-width: 540px;
  }

  .hero-actions {
    justify-content: center;
  }

  .showcase-container {
    max-width: 340px;
  }
}

@media (max-width: 640px) {
  .hero {
    padding: 7rem 1.5rem 3rem;
  }

  .hero-actions {
    flex-direction: column;
    align-items: center;
    width: 100%;
  }

  .discord-btn :deep(.p-button),
  .docs-btn :deep(.p-button) {
    width: 100%;
    max-width: 280px;
    justify-content: center;
  }

  .showcase-container {
    max-width: 280px;
  }

  .features-section,
  .cta-section {
    padding: 4rem 1.5rem;
  }
}
</style>
