<script setup>
import { useRouter, useRoute } from "vue-router";
import { ref, onMounted, onUnmounted } from "vue";
import Button from "primevue/button";

const router = useRouter();
const route = useRoute();

const navLinks = [
  { label: "Home", path: "/", section: "home" },
  { label: "Features", path: "/#features", section: "features" },
  { label: "Documentations", path: "/docs", section: null },
];

const activeSection = ref("home");
const mobileMenuOpen = ref(false);

function onScroll() {
  const featuresEl = document.getElementById("features");
  if (!featuresEl) {
    activeSection.value = "home";
    return;
  }
  const rect = featuresEl.getBoundingClientRect();
  if (rect.top < window.innerHeight / 2) {
    activeSection.value = "features";
  } else {
    activeSection.value = "home";
  }
}

onMounted(() => {
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();
});

onUnmounted(() => {
  window.removeEventListener("scroll", onScroll);
});

function isActive(link) {
  if (link.section && route.path === "/") {
    return activeSection.value === link.section;
  }
  return route.path === link.path;
}

function handleNavClick(link) {
  mobileMenuOpen.value = false;
  if (link.path === "/") {
    if (route.path === "/") {
      window.scrollTo({ top: 0, behavior: "smooth" });
    } else {
      router.push("/");
    }
    return;
  }

  if (link.path.startsWith("/#")) {
    router.push({ path: "/", hash: link.path.substring(1) });
  } else {
    router.push(link.path);
  }
}
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
      <span class="logo-text">MehrakBot</span>
    </div>

    <div class="nav-center">
      <a
        v-for="link in navLinks"
        :key="link.label"
        href="#"
        class="nav-link"
        :class="{ active: isActive(link) }"
        @click.prevent="handleNavClick(link)"
      >
        {{ link.label }}
      </a>
      <a
        href="https://github.com/GamerYuan/MehrakBot"
        target="_blank"
        rel="noopener noreferrer"
        class="nav-link"
      >
        GitHub
      </a>
    </div>

    <button
      class="mobile-menu-btn"
      aria-label="Toggle menu"
      @click="mobileMenuOpen = !mobileMenuOpen"
    >
      <span class="hamburger-line" :class="{ open: mobileMenuOpen }"></span>
      <span class="hamburger-line" :class="{ open: mobileMenuOpen }"></span>
      <span class="hamburger-line" :class="{ open: mobileMenuOpen }"></span>
    </button>

    <div v-if="mobileMenuOpen" class="mobile-menu">
      <a
        v-for="link in navLinks"
        :key="link.label"
        href="#"
        class="nav-link"
        :class="{ active: isActive(link) }"
        @click.prevent="handleNavClick(link)"
      >
        {{ link.label }}
      </a>
      <a
        href="https://github.com/GamerYuan/MehrakBot"
        target="_blank"
        rel="noopener noreferrer"
        class="nav-link"
      >
        GitHub
      </a>
    </div>

    <div class="nav-right">
      <Button
        as="a"
        label="Invite Bot"
        icon="pi pi-discord"
        href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
        target="_blank"
        rel="noopener noreferrer"
        class="invite-btn"
      />
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
  padding: 1rem 2.5rem;
  background: rgba(8, 8, 12, 0.85);
  backdrop-filter: blur(16px);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  z-index: 100;
}

.nav-logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.logo-icon {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  object-fit: contain;
}

.logo-text {
  font-weight: 700;
  font-size: 1.15rem;
  color: var(--accent);
  letter-spacing: 0.02em;
}

.nav-center {
  display: flex;
  align-items: center;
  gap: 2rem;
  position: absolute;
  left: 50%;
  transform: translateX(-50%);
}

.nav-link {
  color: #a0a0a0;
  text-decoration: none;
  font-size: 0.9rem;
  font-weight: 500;
  transition: color 0.2s ease;
  position: relative;
  padding-bottom: 0.25rem;
}

.nav-link:hover {
  color: #fff;
}

.nav-link.active {
  color: var(--accent);
}

.nav-link.active::after {
  content: "";
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  height: 2px;
  background: var(--accent);
  border-radius: 2px;
}

.nav-right {
  display: flex;
  align-items: center;
}

.invite-btn :deep(.p-button) {
  background: var(--accent);
  border-color: var(--accent);
  color: #000;
  font-weight: 600;
  padding: 0.55rem 1.25rem;
  border-radius: 6px;
}

.invite-btn :deep(.p-button:hover) {
  background: var(--accent-strong);
  border-color: var(--accent-strong);
}

.invite-btn :deep(.p-button-icon) {
  color: #000;
}

@media (max-width: 768px) {
  .nav {
    padding: 1rem;
  }

  .nav-center {
    display: none;
  }

  .mobile-menu-btn {
    display: flex;
    flex-direction: column;
    gap: 4px;
    background: none;
    border: none;
    cursor: pointer;
    padding: 0.5rem;
  }

  .hamburger-line {
    display: block;
    width: 22px;
    height: 2px;
    background: #fff;
    border-radius: 2px;
    transition: transform 0.2s, opacity 0.2s;
  }

  .hamburger-line.open:nth-child(1) {
    transform: translateY(6px) rotate(45deg);
  }

  .hamburger-line.open:nth-child(2) {
    opacity: 0;
  }

  .hamburger-line.open:nth-child(3) {
    transform: translateY(-6px) rotate(-45deg);
  }

  .mobile-menu {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    background: rgba(8, 8, 12, 0.95);
    backdrop-filter: blur(16px);
    border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    display: flex;
    flex-direction: column;
    padding: 1rem;
    gap: 0.75rem;
    z-index: 99;
  }

  .mobile-menu .nav-link {
    font-size: 1rem;
    padding: 0.5rem 0;
  }

  .invite-btn :deep(.p-button-label) {
    display: none;
  }

  .invite-btn :deep(.p-button) {
    padding: 0.55rem;
  }
}

@media (min-width: 769px) {
  .mobile-menu-btn {
    display: none;
  }
}
</style>
