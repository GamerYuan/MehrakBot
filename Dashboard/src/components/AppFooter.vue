<script setup>
import { useRouter } from "vue-router";

const router = useRouter();
</script>

<template>
  <footer class="footer mt-auto">
    <div class="footer-content">
      <div class="footer-brand">
        <img src="/logo.webp" alt="MehrakBot" class="logo-icon" />
        <span>MehrakBot</span>
      </div>
      <div class="footer-links">
        <a href="#" @click.prevent="router.push('/privacy')">Privacy Policy</a>
        <span class="divider">•</span>
        <a href="#" @click.prevent="router.push('/terms')">Terms of Service</a>
      </div>
      <p class="footer-disclaimer">
        Not affiliated with HoYoverse. All game titles and trademarks belong
        to their respective owners.
      </p>
      <p class="footer-copyright">&copy; 2026 MehrakBot</p>
    </div>
  </footer>
</template>

<style scoped>
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

.logo-icon {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  object-fit: contain;
}

.footer-links {
  margin-bottom: 1.5rem;
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 1rem;
}

.divider {
  color: #555;
}

.footer-links a {
  color: #a0a0a0;
  text-decoration: none;
  font-size: 0.9rem;
  transition: color 0.2s;
}

.footer-links a:hover {
  color: #fff;
  text-decoration: underline;
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
</style>