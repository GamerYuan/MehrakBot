<script setup>
import { useRouter } from "vue-router";
import Button from "primevue/button";

const router = useRouter();
</script>

<template>
  <nav class="nav">
    <div
      class="nav-logo"
      role="button"
      tabindex="0"
      @click="router.push('/')"
      @keydown.enter.prevent="router.push('/')"
      @keydown.space.prevent="router.push('/')"
    >
      <img src="/logo.webp" alt="MehrakBot" class="logo-icon" />
      <span>MehrakBot</span>
    </div>
    <div class="nav-links">
      <a href="#" @click.prevent="router.push('/docs')">Docs</a>
      <a
        href="https://github.com/GamerYuan/MehrakBot"
        target="_blank"
        rel="noopener noreferrer"
      >
        <i class="pi pi-github"></i>
        GitHub
      </a>
    </div>
  </nav>
</template>

<style scoped>
.nav {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 2rem;
  background: rgba(10, 10, 15, 0.8);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  z-index: 100;
}

.nav-logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.2rem;
  cursor: pointer;
}

.logo-icon {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  object-fit: contain;
}

.nav-links {
  display: flex;
  align-items: center;
  gap: 1.5rem;
}

.nav-links a {
  color: #a0a0a0;
  text-decoration: none;
  font-size: 0.9rem;
  transition: color 0.2s;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.nav-links a:hover {
  color: #fff;
}

@media (max-width: 640px) {
  .nav {
    padding: 1rem;
    flex-wrap: wrap;
    gap: 0.75rem;
  }

  .nav-logo {
    font-size: 1rem;
  }

  .nav-links {
    gap: 0.75rem;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .nav-links a {
    font-size: 0.85rem;
  }
}
</style>
