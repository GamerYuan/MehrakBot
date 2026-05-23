<script setup>
import { useRouter } from "vue-router";

const router = useRouter();
</script>

<template>
  <footer class="footer mt-auto">
    <div class="footer-content">
      <div class="footer-left">
        <div class="footer-brand">
          <img src="/logo.webp" alt="MehrakBot" class="logo-icon" />
          <span class="brand-text">MehrakBot</span>
        </div>
        <p class="footer-disclaimer">
          Licensed under GPL-3.0. MehrakBot is not affiliated with HoYoverse.
        </p>
      </div>
      <div class="footer-right">
        <a href="#" @click.prevent="router.push('/docs')">Documentation</a>
        <a href="#" @click.prevent="router.push('/privacy')">Privacy Policy</a>
        <a
          href="https://discord.gg/VUHv2kmx9Z"
          target="_blank"
          rel="noopener noreferrer"
        >
          Support Server
        </a>
        <a
          href="https://github.com/GamerYuan/MehrakBot"
          target="_blank"
          rel="noopener noreferrer"
        >
          Source Code
        </a>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.footer {
  padding: 2.5rem 2.5rem;
  border-top: 1px solid rgba(255, 255, 255, 0.05);
  background: rgba(0, 0, 0, 0.3);
}

.footer-content {
  max-width: 1200px;
  margin: 0 auto;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 1.5rem;
}

.footer-left {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.footer-brand {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.logo-icon {
  width: 24px;
  height: 24px;
  border-radius: 6px;
  object-fit: contain;
}

.brand-text {
  font-weight: 700;
  font-size: 1rem;
  color: var(--accent);
  letter-spacing: 0.02em;
}

.footer-disclaimer {
  color: #555;
  font-size: 0.8rem;
  margin: 0;
}

.footer-right {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  flex-wrap: wrap;
}

.footer-right a {
  color: #888;
  text-decoration: none;
  font-size: 0.85rem;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  transition: color 0.2s;
}

.footer-right a:hover {
  color: #fff;
}

@media (max-width: 640px) {
  .footer-content {
    flex-direction: column;
    text-align: center;
  }

  .footer-left {
    align-items: center;
  }

  .footer-right {
    justify-content: center;
  }
}
</style>
