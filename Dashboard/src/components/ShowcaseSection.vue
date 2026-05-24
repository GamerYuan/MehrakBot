<script setup>
import { ref, onMounted, onUnmounted } from "vue";
import CardDeckShowcase from "./CardDeckShowcase.vue";

const props = defineProps({
  title: { type: String, required: true },
  description: { type: String, required: true },
  images: { type: Array, required: true },
  reversed: { type: Boolean, default: false },
});

const sectionRef = ref(null);
const isVisible = ref(false);
let observer = null;

onMounted(() => {
  if (!sectionRef.value) return;
  observer = new IntersectionObserver(
    ([entry]) => {
      if (entry.isIntersecting) {
        isVisible.value = true;
        observer.disconnect();
      }
    },
    { threshold: 0.2 },
  );
  observer.observe(sectionRef.value);
});

onUnmounted(() => {
  if (observer) observer.disconnect();
});
</script>

<template>
  <section
    ref="sectionRef"
    class="showcase-section"
    :class="{ reversed, visible: isVisible }"
  >
    <div class="showcase-text">
      <h2 class="showcase-title">{{ title }}</h2>
      <p class="showcase-desc">{{ description }}</p>
    </div>
    <div class="showcase-media">
      <CardDeckShowcase :images="images" :active="isVisible" />
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  display: flex;
  align-items: center;
  gap: 4rem;
  max-width: 1400px;
  margin: 0 auto;
  padding: 5rem 2.5rem;
  overflow-x: hidden;
}

.showcase-section.reversed {
  flex-direction: row-reverse;
}

/* Initial hidden state */
.showcase-text,
.showcase-media {
  opacity: 0;
  transition:
    opacity 0.7s ease-out,
    transform 0.7s ease-out;
}

/* Text starts from left, media from right (default) */
.showcase-section:not(.reversed) .showcase-text {
  transform: translateX(-60px);
}
.showcase-section:not(.reversed) .showcase-media {
  transform: translateX(60px);
}

/* Reversed: media from left, text from right */
.showcase-section.reversed .showcase-media {
  transform: translateX(-60px);
}
.showcase-section.reversed .showcase-text {
  transform: translateX(60px);
}

/* Visible state */
.showcase-section.visible .showcase-text,
.showcase-section.visible .showcase-media {
  opacity: 1;
  transform: translateX(0);
}

.showcase-text {
  flex: 0 0 35%;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.showcase-title {
  font-size: clamp(1.8rem, 3.5vw, 2.6rem);
  font-weight: 700;
  color: #f0f0f5;
  margin: 0;
  letter-spacing: -0.01em;
  line-height: 1.15;
}

.showcase-desc {
  font-size: 1rem;
  color: #888;
  margin: 0;
  line-height: 1.7;
  max-width: 440px;
}

.showcase-media {
  flex: 1 1 auto;
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 0;
}

@media (max-width: 960px) {
  .showcase-section,
  .showcase-section.reversed {
    flex-direction: column;
    gap: 2.5rem;
    text-align: center;
    padding: 4rem 1.5rem;
  }

  .showcase-text {
    flex: none;
    width: 100%;
  }

  .showcase-desc {
    max-width: 540px;
    margin: 0 auto;
  }

  .showcase-media {
    order: -1;
    width: 100%;
  }

  /* Mobile: both slide up instead */
  .showcase-section:not(.reversed) .showcase-text,
  .showcase-section:not(.reversed) .showcase-media,
  .showcase-section.reversed .showcase-text,
  .showcase-section.reversed .showcase-media {
    transform: translateY(40px);
  }

  .showcase-section.visible .showcase-text,
  .showcase-section.visible .showcase-media {
    transform: translateY(0);
  }
}
</style>
