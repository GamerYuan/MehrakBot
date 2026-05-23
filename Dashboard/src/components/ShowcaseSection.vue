<script setup>
import CardDeckShowcase from "./CardDeckShowcase.vue";

defineProps({
  title: { type: String, required: true },
  description: { type: String, required: true },
  images: { type: Array, required: true },
  reversed: { type: Boolean, default: false },
});
</script>

<template>
  <section class="showcase-section">
    <template v-if="!reversed">
      <div class="showcase-text">
        <h2 class="showcase-title">{{ title }}</h2>
        <p class="showcase-desc">{{ description }}</p>
      </div>
      <div class="showcase-media">
        <CardDeckShowcase :images="images" />
      </div>
    </template>
    <template v-else>
      <div class="showcase-media">
        <CardDeckShowcase :images="images" />
      </div>
      <div class="showcase-text">
        <h2 class="showcase-title">{{ title }}</h2>
        <p class="showcase-desc">{{ description }}</p>
      </div>
    </template>
  </section>
</template>

<style scoped>
.showcase-section {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 4rem;
  align-items: center;
  max-width: 1200px;
  margin: 0 auto;
  padding: 5rem 2.5rem;
}

.showcase-text {
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
  display: flex;
  align-items: center;
  justify-content: center;
}

@media (max-width: 960px) {
  .showcase-section {
    grid-template-columns: 1fr;
    gap: 2.5rem;
    text-align: center;
    padding: 4rem 1.5rem;
  }

  .showcase-desc {
    max-width: 540px;
    margin: 0 auto;
  }

  .showcase-media {
    order: -1;
  }
}
</style>
