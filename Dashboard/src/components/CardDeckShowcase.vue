<script setup>
import { ref, computed, onMounted, onUnmounted } from "vue";

const props = defineProps({
  images: { type: Array, required: true },
  interval: { type: Number, default: 3000 },
});

// Ensure at least 3 images for the deck effect
const deckImages = computed(() => {
  const imgs = props.images;
  if (imgs.length >= 3) return imgs;
  // Duplicate to fill if fewer than 3
  const filled = [];
  while (filled.length < 3) {
    filled.push(...imgs);
  }
  return filled.slice(0, Math.max(3, imgs.length));
});

const deck = ref(deckImages.value.slice(0, 3));
const cycling = ref(false);
let timer = null;

function cycle() {
  if (cycling.value) return;
  cycling.value = true;
  setTimeout(() => {
    deck.value.push(deck.value.shift());
    cycling.value = false;
  }, 600);
}

function start() {
  if (timer) clearInterval(timer);
  timer = setInterval(cycle, props.interval);
}

function stop() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
}

onMounted(start);
onUnmounted(stop);
</script>

<template>
  <div class="deck" @mouseenter="stop" @mouseleave="start">
    <div
      v-for="(img, i) in deck"
      :key="img + i"
      class="card"
      :class="{
        'card-front': i === 0,
        'card-mid': i === 1,
        'card-back': i === 2,
        'is-leaving': cycling && i === 0,
      }"
    >
      <img :src="img" :alt="`Showcase ${i + 1}`" />
    </div>
  </div>
</template>

<style scoped>
.deck {
  position: relative;
  width: 100%;
  max-width: 440px;
  aspect-ratio: 4/3;
  margin: 0 auto;
}

.card {
  position: absolute;
  inset: 0;
  border-radius: 14px;
  overflow: hidden;
  transition: all 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: #0a0a10;
}

.card img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  object-position: center;
}

.card-front {
  transform: translate(0, 0) scale(1);
  z-index: 3;
}

.card-mid {
  transform: translate(-10px, 10px) scale(0.96) rotate(-1deg);
  z-index: 2;
  opacity: 0.9;
}

.card-back {
  transform: translate(-20px, 20px) scale(0.92) rotate(1deg);
  z-index: 1;
  opacity: 0.75;
}

.is-leaving {
  transform: translate(-20px, 20px) scale(0.92) rotate(1deg);
  opacity: 0;
  z-index: 0;
}
</style>
